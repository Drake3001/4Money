using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using ZPI4M.Banking.Models;
using ZPI4M.Banking.Repositories.BankingConnectionRepository;
using ZPI4M.Banking.Services.Connections;
using ZPI4M.Banking.Services.Contracts;
using ZPI4M.Banking.Tink;
using ZPI4M.Banking.Tink.Configuration;
using ZPI4M.Banking.Tink.Http;
using ZPI4M.User.Services.BankingProfile;

namespace ZPI4M.Banking.UnitTests;

public sealed class BankConsentServiceTests
{
	[Fact]
	public async Task MissingUserReturnsNotFoundWithoutContactingTink()
	{
		var dependencies = new Dependencies { Profile = null };
		Assert.Equal(BankingErrorCode.NotFound, (await Service(dependencies).GetConsentsAsync(dependencies.UserId)).Error);
		Assert.Equal(0, dependencies.ConsentCalls);
	}

	[Fact]
	public async Task InactiveUserReturnsForbiddenWithoutReadingConnections()
	{
		var dependencies = new Dependencies();
		dependencies.Profile = dependencies.Profile! with { IsActive = false };
		Assert.Equal(BankingErrorCode.Forbidden, (await Service(dependencies).GetConsentsAsync(dependencies.UserId)).Error);
		Assert.Equal(0, dependencies.ConnectionReads);
		Assert.Equal(0, dependencies.ConsentCalls);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" ")]
	public async Task MissingMappingDoesNotProvisionUser(string? tinkUserId)
	{
		var dependencies = new Dependencies();
		dependencies.Profile = dependencies.Profile! with { TinkUserId = tinkUserId };
		var result = await Service(dependencies).GetConsentsAsync(dependencies.UserId);
		Assert.True(result.IsSuccess);
		Assert.Empty(result.Value!);
		Assert.Equal(0, dependencies.ConsentCalls);
		Assert.Equal(0, dependencies.ConnectionReads);
	}

	[Fact]
	public async Task NoLocalConnectionsDoesNotContactTink()
	{
		var dependencies = new Dependencies { Connections = [] };
		var result = await Service(dependencies).GetConsentsAsync(dependencies.UserId);
		Assert.True(result.IsSuccess);
		Assert.Empty(result.Value!);
		Assert.Equal(0, dependencies.ConsentCalls);
	}

	[Fact]
	public async Task OnlyMatchingOwnerAndCurrentTinkMappingAreReturnedWithoutChanges()
	{
		var dependencies = new Dependencies();
		var owned = dependencies.Connections[0];
		owned.ConsentExpiresAt = DateTimeOffset.UnixEpoch;
		var staleMapping = dependencies.Connection("stale");
		staleMapping.TinkUserId = "previous-tink-user";
		var foreign = dependencies.Connection("foreign");
		foreign.FourMoneyUserId = Guid.NewGuid();
		dependencies.Connections = [owned, staleMapping, foreign];
		var expiry = DateTimeOffset.UtcNow.AddDays(5);
		dependencies.Consents =
		[
			new("owned", "FUTURE_STATUS", expiry, true),
			new("stale", "UPDATED", null, null),
			new("foreign", "UPDATED", null, null),
			new("remote-only", "UPDATED", null, null)
		];
		var result = await Service(dependencies).GetConsentsAsync(dependencies.UserId);
		var consent = Assert.Single(result.Value!);
		Assert.Equal(new BankConsentDetails(owned.Id, "FUTURE_STATUS", expiry, true), consent);
		Assert.Equal("tink-owner", dependencies.TinkUserSeen);
		Assert.Equal(dependencies.UserId, dependencies.RepositoryUserSeen);
		Assert.Equal(DateTimeOffset.UnixEpoch, owned.ConsentExpiresAt);
		Assert.Equal(BankConnectionStatus.Connected, owned.ConnectionStatus);
	}

	[Fact]
	public async Task StaleMappingIsNotQueriedUsingCurrentUserToken()
	{
		var dependencies = new Dependencies();
		dependencies.Connections[0].TinkUserId = "previous-mapping";
		var result = await Service(dependencies).GetConsentsAsync(dependencies.UserId);
		Assert.Empty(result.Value!);
		Assert.Equal(0, dependencies.ConsentCalls);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task EmptyOrUnmatchedProviderResponseIsNotInferredAsRevocation(bool unmatched)
	{
		var dependencies = new Dependencies { Consents = unmatched ? [new("remote-only", "UPDATED", null, null)] : [] };
		var result = await Service(dependencies).GetConsentsAsync(dependencies.UserId);
		Assert.True(result.IsSuccess);
		Assert.Empty(result.Value!);
		Assert.Equal(BankConnectionStatus.Connected, dependencies.Connections[0].ConnectionStatus);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task IntegrationAndConfigurationErrorsBecomeUnavailable(bool invalidConfiguration)
	{
		var dependencies = new Dependencies
		{
			Failure = invalidConfiguration
			 ? new OptionsValidationException("Tink", typeof(TinkOptions), ["private-config-message"])
			 : new TinkApiException("provider_consents", "forbidden")
		};
		var result = await Service(dependencies).GetConsentsAsync(dependencies.UserId);
		Assert.False(result.IsSuccess);
		Assert.Equal(BankingErrorCode.TemporarilyUnavailable, result.Error);
		Assert.Null(result.Value);
	}

	[Fact]
	public async Task CallerCancellationIsPassedToEveryReadAndNotConvertedToUnavailable()
	{
		using var cancellation = new CancellationTokenSource();
		var dependencies = new Dependencies { ExpectedCancellation = cancellation.Token };
		dependencies.BeforeConsents = () => cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(dependencies).GetConsentsAsync(dependencies.UserId, cancellation.Token));
	}

	private static BankingConnectionService Service(Dependencies dependencies) => new(dependencies, dependencies,
	 dependencies, dependencies, TimeProvider.System, Options.Create(new BankingConnectionOptions()),
	 NullLogger<BankingConnectionService>.Instance);

	private sealed class Dependencies : IBankConnectionAttemptRepository, IBankConnectionRepository,
	 IUserBankingProfileService, ITinkConnectionGateway
	{
		public Guid UserId { get; } = Guid.NewGuid();
		public UserBankingProfile? Profile { get; set; }
		public IReadOnlyList<BankConnection> Connections { get; set; }
		public IReadOnlyList<TinkProviderConsent> Consents { get; set; } = [];
		public int ConsentCalls { get; private set; }
		public int ConnectionReads { get; private set; }
		public string? TinkUserSeen { get; private set; }
		public Guid RepositoryUserSeen { get; private set; }
		public CancellationToken ExpectedCancellation { get; set; }
		public Action? BeforeConsents { get; set; }
		public Exception? Failure { get; set; }
		public Dependencies()
		{
			Profile = new(UserId, true, "tink-owner", "Test User");
			Connections = [Connection("owned")];
		}
		public BankConnection Connection(string credentialsId) => new()
		{
			Id = Guid.NewGuid(),
			FourMoneyUserId = UserId,
			TinkUserId = "tink-owner",
			CredentialsId = credentialsId,
			ConnectionStatus = BankConnectionStatus.Connected
		};
		public Task<UserBankingProfile?> GetAsync(Guid userId, CancellationToken cancellationToken)
		{
			Assert.Equal(ExpectedCancellation, cancellationToken);
			Assert.Equal(UserId, userId);
			return Task.FromResult(Profile);
		}
		public Task<IReadOnlyList<BankConnection>> GetAllAsync(Guid userId, CancellationToken cancellationToken)
		{
			Assert.Equal(ExpectedCancellation, cancellationToken);
			ConnectionReads++;
			RepositoryUserSeen = userId;
			return Task.FromResult(Connections);
		}
		public Task<IReadOnlyList<TinkProviderConsent>> GetConsentsAsync(string tinkUserId, CancellationToken cancellationToken)
		{
			Assert.Equal(ExpectedCancellation, cancellationToken);
			ConsentCalls++;
			TinkUserSeen = tinkUserId;
			BeforeConsents?.Invoke();
			cancellationToken.ThrowIfCancellationRequested();
			return Failure is not null ? throw Failure : Task.FromResult(Consents);
		}
		public Task<UserBankingProfile?> EnsureTinkUserAsync(Guid userId, Func<CancellationToken, Task<string>> createOrRecover, CancellationToken cancellationToken) => throw new InvalidOperationException("Consent read must not provision users.");
		public Task<string> CreateOrRecoverUserAsync(Guid userId, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected provisioning.");
		public Task<string> CreateLinkAsync(string tinkUserId, string idHint, string state, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected link generation.");
		public Task<TinkVerifiedConnection?> VerifyConnectionAsync(string tinkUserId, string credentialsId, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected credential verification.");
		public Task<BankConnection?> GetAsync(Guid userId, Guid connectionId, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected connection read.");
		public Task AddAsync(BankConnectionAttempt attempt, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected persistence.");
		public Task<BankConnectionAttempt?> GetAsync(Guid userId, Guid attemptId, DateTimeOffset now, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected attempt read.");
		public Task<BankConnectionAttempt?> FindByStateAsync(string stateHash, DateTimeOffset now, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected state lookup.");
		public Task<bool> SetTinkUserAsync(Guid attemptId, string tinkUserId, DateTimeOffset now, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected persistence.");
		public Task<BankConnectionAttempt?> FinishAsync(Guid attemptId, BankConnectionAttemptStatus status, string? failureCode, BankConnection? connection, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected persistence.");
	}
}
