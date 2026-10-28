using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ZPI4M.Banking.Services.Contracts;
using ZPI4M.Banking.Tink;

namespace ZPI4M.Banking.IntegrationTests;

public sealed class ConnectionFlowTests : PostgresFixture
{
	[PostgresFact]
	public async Task ConnectsMultipleBanksReusesPermanentUserAndRejectsReplay()
	{
		var user = await AddUserAsync();
		var other = await AddUserAsync();
		using var session = OpenSession();
		var start = await session.Service.StartConnectionAsync(user);
		Assert.True(start.IsSuccess);
		var callback = Callback(start.Value!, "credentials-1");
		var stored = await session.Database.ConnectionAttempts.AsNoTracking().SingleAsync();
		Assert.NotEqual(callback.State, stored.CorrelationStateHash);
		Assert.Equal(64, stored.CorrelationStateHash.Length);
		Assert.Equal(BankConnectionAttemptStatus.Pending, stored.Status);
		Assert.Equal(BankingErrorCode.NotFound, (await session.Service.GetConnectionAttemptAsync(other, start.Value!.AttemptId)).Error);
		var result = await session.Service.CompleteAsync(callback);
		Assert.True(result.IsSuccess);
		Assert.Equal(BankConnectionAttemptStatus.Completed, result.Value!.Status);
		var connection = await session.Service.GetConnectionAsync(user, result.Value.ConnectionId!.Value);
		Assert.Equal(BankSynchronizationStatus.NotStarted, connection.Value!.SyncStatus);
		Assert.Equal(BankingErrorCode.NotFound, (await session.Service.GetConnectionAsync(other, connection.Value.Id)).Error);
		Assert.Equal(BankingErrorCode.Conflict, (await session.Service.CompleteAsync(callback)).Error);
		var second = await session.Service.StartConnectionAsync(user);
		Assert.True((await session.Service.CompleteAsync(Callback(second.Value!, "credentials-2"))).IsSuccess);
		Assert.Equal(2, (await session.Service.GetConnectionsAsync(user)).Value!.Count);
		Assert.Equal(1, Tink.CreateCalls);
	}

	[PostgresFact]
	public async Task LinkFailureAndInactiveUserLeaveDurableFailedAttempts()
	{
		var user = await AddUserAsync();
		var inactive = await AddUserAsync(active: false);
		using var session = OpenSession();
		Tink.FailLink = true;
		var failed = await session.Service.StartConnectionAsync(user);
		Assert.Equal(BankingErrorCode.TemporarilyUnavailable, failed.Error);
		var stored = await session.Service.GetConnectionAttemptAsync(user, failed.AttemptId!.Value);
		Assert.Equal(BankConnectionAttemptStatus.Failed, stored.Value!.Status);
		Assert.Equal("TINK_UNAVAILABLE", stored.Value.FailureCode);
		var denied = await session.Service.StartConnectionAsync(inactive);
		Assert.Equal(BankingErrorCode.Forbidden, denied.Error);
		Assert.Equal("USER_INACTIVE", (await session.Service.GetConnectionAttemptAsync(inactive, denied.AttemptId!.Value)).Value!.FailureCode);
		Assert.Equal(2, await session.Database.ConnectionAttempts.CountAsync());
	}

	[PostgresFact]
	public async Task TamperedCallbackDoesNotConsumeAttemptAndExpirySurvivesNewSession()
	{
		var user = await AddUserAsync(tinkUserId: "existing-user");
		StartBankConnectionResult start;
		using (var session = OpenSession())
		{
			start = (await session.Service.StartConnectionAsync(user)).Value!;
			Assert.Equal(BankingErrorCode.InvalidCallback, (await session.Service.CompleteAsync(new BankingCallback { State = "invalid", CredentialsId = "id" })).Error);
			Tink.RejectCredentials = true;
			Assert.Equal(BankingErrorCode.InvalidCallback, (await session.Service.CompleteAsync(Callback(start, "wrong"))).Error);
			Assert.Equal(BankConnectionAttemptStatus.Pending, (await session.Service.GetConnectionAttemptAsync(user, start.AttemptId)).Value!.Status);
		}
		Clock.Advance(TimeSpan.FromMinutes(16));
		using var restarted = OpenSession();
		Assert.Equal(BankConnectionAttemptStatus.Expired, (await restarted.Service.GetConnectionAttemptAsync(user, start.AttemptId)).Value!.Status);
		Assert.Equal(BankingErrorCode.Conflict, (await restarted.Service.CompleteAsync(Callback(start, "wrong"))).Error);
		Assert.Empty((await restarted.Service.GetConnectionsAsync(user)).Value!);
		Assert.Equal(0, Tink.CreateCalls);
	}

	[PostgresTheory]
	[InlineData("USER_CANCELLED", BankConnectionAttemptStatus.Cancelled, "USER_CANCELLED")]
	[InlineData("USER_DECLINED_CONSENT", BankConnectionAttemptStatus.Failed, "CONSENT_DECLINED")]
	[InlineData("untrusted error text", BankConnectionAttemptStatus.Failed, "BANK_AUTHORIZATION_FAILED")]
	public async Task CancellationAndErrorsStoreOnlySafeCodes(string reason, BankConnectionAttemptStatus expected, string code)
	{
		var user = await AddUserAsync();
		using var session = OpenSession();
		var start = (await session.Service.StartConnectionAsync(user)).Value!;
		var state = Callback(start, "unused").State;
		var result = await session.Service.CompleteAsync(new BankingCallback { State = state, Error = "USER_ERROR", ErrorReason = reason });
		Assert.True(result.IsSuccess);
		Assert.Equal(expected, result.Value!.Status);
		Assert.Equal(code, result.Value.FailureCode);
		Assert.Empty((await session.Service.GetConnectionsAsync(user)).Value!);
	}

	[PostgresFact]
	public async Task TemporaryVerificationFailureCanBeRetried()
	{
		var user = await AddUserAsync();
		using var session = OpenSession();
		var start = (await session.Service.StartConnectionAsync(user)).Value!;
		Tink.FailVerification = true;
		Assert.Equal(BankingErrorCode.TemporarilyUnavailable, (await session.Service.CompleteAsync(Callback(start, "credential"))).Error);
		Assert.Equal(BankConnectionAttemptStatus.Pending, (await session.Service.GetConnectionAttemptAsync(user, start.AttemptId)).Value!.Status);
		Tink.FailVerification = false;
		Assert.True((await session.Service.CompleteAsync(Callback(start, "credential"))).IsSuccess);
		// Local reads do not contact Tink even when it fails again.
		Tink.FailLink = Tink.FailVerification = true;
		Assert.Single((await session.Service.GetConnectionsAsync(user)).Value!);
	}

	[PostgresFact]
	public async Task ParallelStartsCreateOnePermanentUserAndParallelCallbacksFinishOnce()
	{
		var user = await AddUserAsync();
		var starts = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
		{
			using var session = OpenSession();
			return await session.Service.StartConnectionAsync(user);
		}));
		Assert.All(starts, x => Assert.True(x.IsSuccess));
		Assert.Equal(1, Tink.CreateCalls);
		var entered = 0;
		var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		Tink.BeforeVerify = async () =>
		{
			if (Interlocked.Increment(ref entered) == 2) bothEntered.SetResult();
			await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		};
		var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
		{
			using var session = OpenSession();
			return await session.Service.CompleteAsync(Callback(starts[0].Value!, "same-credentials"));
		}));
		Assert.Single(results, x => x.IsSuccess);
		Assert.Single(results, x => x.Error == BankingErrorCode.Conflict);
		using var check = OpenSession();
		Assert.Equal(1, await check.Database.Connections.CountAsync());
	}

	[PostgresFact]
	public async Task CredentialsCannotBeAttachedToAnotherLocalOwner()
	{
		var owner = await AddUserAsync();
		var other = await AddUserAsync();
		using var session = OpenSession();
		var first = (await session.Service.StartConnectionAsync(owner)).Value!;
		var second = (await session.Service.StartConnectionAsync(other)).Value!;
		Assert.True((await session.Service.CompleteAsync(Callback(first, "same-credentials"))).IsSuccess);
		Assert.Equal(BankingErrorCode.Conflict, (await session.Service.CompleteAsync(Callback(second, "same-credentials"))).Error);
		Assert.Empty((await session.Service.GetConnectionsAsync(other)).Value!);
	}

	[PostgresFact]
	public async Task ProvisioningDoesNotBlockUserProfileOrLoginWrites()
	{
		var user = await AddUserAsync();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		Tink.BeforeCreate = async () => { entered.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(10)); };
		using var provisioning = OpenSession();
		var start = provisioning.Service.StartConnectionAsync(user);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			await using var profile = new ZPI4M.User.Database.UserDbContext(
			 new DbContextOptionsBuilder<ZPI4M.User.Database.UserDbContext>().UseNpgsql(ConnectionString).Options);
			var updated = await profile.Users.Where(x => x.Id == user)
			 .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Name, "Updated during provisioning"))
			 .WaitAsync(TimeSpan.FromSeconds(3));
			Assert.Equal(1, updated);
		}
		finally { release.TrySetResult(); }
		Assert.True((await start).IsSuccess);
	}

	[PostgresFact]
	public async Task ConsentReadsAreScopedAndLeavePersistedStateUnchanged()
	{
		var owner = await AddUserAsync(tinkUserId: "tink-owner");
		var other = await AddUserAsync(tinkUserId: "tink-other");
		using var session = OpenSession();
		var start = (await session.Service.StartConnectionAsync(owner)).Value!;
		var completed = (await session.Service.CompleteAsync(Callback(start, "owned-credential"))).Value!;
		var otherStart = (await session.Service.StartConnectionAsync(other)).Value!;
		await session.Service.CompleteAsync(Callback(otherStart, "other-credential"));
		var snapshot = System.Text.Json.JsonSerializer.Serialize(await session.Database.Connections.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync());
		var attemptSnapshot = System.Text.Json.JsonSerializer.Serialize(await session.Database.ConnectionAttempts.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync());
		Tink.Consents =
		[
			new("owned-credential", "UPDATED", Clock.GetUtcNow().AddDays(30), true),
			new("other-credential", "UPDATED", null, null),
			new("remote-only", "UPDATED", null, null)
		];
		var result = await session.Service.GetConsentsAsync(owner);
		Assert.True(result.IsSuccess);
		Assert.Equal(completed.ConnectionId, Assert.Single(result.Value!).ConnectionId);
		Assert.Equal(snapshot, System.Text.Json.JsonSerializer.Serialize(await session.Database.Connections.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()));
		Assert.Equal(attemptSnapshot, System.Text.Json.JsonSerializer.Serialize(await session.Database.ConnectionAttempts.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()));
		Assert.Equal(0, Tink.CreateCalls);
		Assert.Equal(1, Tink.ConsentCalls);
	}

	private static BankingCallback Callback(StartBankConnectionResult start, string credentials)
	 => new() { State = QueryHelpers.ParseQuery(new Uri(start.AuthorizationUrl).Query)["state"].ToString(), CredentialsId = credentials };
}
