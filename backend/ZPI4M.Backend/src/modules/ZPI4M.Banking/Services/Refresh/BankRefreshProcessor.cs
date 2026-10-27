using System.Net;
using Microsoft.Extensions.Options;
using ZPI4M.Banking.Models;
using ZPI4M.Banking.Repositories;
using ZPI4M.Banking.Tink;
using ZPI4M.Banking.Tink.Http;
using ZPI4M.User.Services;

namespace ZPI4M.Banking.Services.Refresh;

public sealed class BankRefreshProcessor(BankRefreshRepository repository, ITinkDataGateway tink,
	IUserBankingProfileService users, TimeProvider clock, IOptions<BankRefreshOptions> options)
{
	public async Task ProcessAsync(BankRefresh job, CancellationToken ct)
	{
		RefreshFailure? failure = null;
		try
		{
			await CheckIdentityAsync(job, ct);
			if (job.DiscoveryCompleted)
			{
				var saved = (await repository.GetAsync(job.FourMoneyUserId, job.Id, ct))!;
				if (saved.Accounts.All(x => x.Status == "SUCCEEDED" || (x.Status == "FAILED" && !x.Retryable)))
				{
					await repository.FinishAsync(job, null, ct);
					return;
				}
			}
			if (job.Attempts > options.Value.MaxAttempts)
			{
				await repository.FinishAsync(job, new("BANKING_ATTEMPTS_EXHAUSTED", false), ct);
				return;
			}
			using var session = await tink.OpenAsync(job.TinkUserId, ct);
			if (!job.DiscoveryCompleted)
				await repository.DiscoverAsync(job, await session.GetAccountsAsync(ct), ct);
			var current = (await repository.GetAsync(job.FourMoneyUserId, job.Id, ct))!;
			foreach (var account in current.Accounts.Where(x => x.Status == "QUEUED" || (x.Status == "FAILED" && x.Retryable)))
			{
				try
				{
					await CheckIdentityAsync(job, ct);
					var transactions = await session.GetTransactionsAsync(account.TinkAccountId, account.From, account.Through, ct);
					await repository.SaveAccountAsync(job, account.AccountId, transactions, ct);
				}
				catch (TinkApiException error)
				{
					await repository.FailAccountAsync(job, account.AccountId, Failure(error), ct);
				}
			}
		}
		catch (TinkApiException error) { failure = Failure(error); }
		catch (RefreshIdentityException) { failure = new("BANKING_IDENTITY_CHANGED", false); }
		catch (OptionsValidationException) { failure = new("BANKING_CONFIGURATION_INVALID", false); }
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			// A separate token lets a timeout/shutdown persist retry state. Lease fencing still applies.
			using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
			await repository.FinishAsync(job, new("BANKING_REFRESH_INTERRUPTED", true), cleanup.Token);
			return;
		}
		await repository.FinishAsync(job, failure, ct);
	}

	private async Task CheckIdentityAsync(BankRefresh job, CancellationToken ct)
	{
		var profile = await users.GetAsync(job.FourMoneyUserId, ct);
		if (profile is null || !profile.IsActive || profile.TinkUserId != job.TinkUserId) throw new RefreshIdentityException();
	}

	private RefreshFailure Failure(TinkApiException error)
	{
		var retryable = error.ErrorCode is "network_error" or "timeout"
			|| error.UpstreamStatus == HttpStatusCode.TooManyRequests || (int?)error.UpstreamStatus >= 500;
		return new(error.UpstreamStatus is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
			? "BANKING_TINK_ACCESS_DENIED" : error.ErrorCode == "invalid_response" ? "BANKING_TINK_INVALID_DATA" : "BANKING_TINK_UNAVAILABLE",
			retryable, error.RetryAfter is { } delay && delay > TimeSpan.Zero ? clock.GetUtcNow().Add(delay) : null);
	}
}
