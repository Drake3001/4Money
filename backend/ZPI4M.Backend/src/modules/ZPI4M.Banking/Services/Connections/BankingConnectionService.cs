using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZPI4M.Banking.Models;
using ZPI4M.Banking.Repositories;
using ZPI4M.Banking.Services.Contracts;
using ZPI4M.Banking.Tink;
using ZPI4M.Banking.Tink.Http;
using ZPI4M.User.Services;

namespace ZPI4M.Banking.Services.Connections;

public sealed class BankingConnectionService(IBankConnectionAttemptRepository attempts,
 IBankConnectionRepository connections, IUserBankingProfileService users, ITinkConnectionGateway tink,
 TimeProvider time, IOptions<BankingConnectionOptions> options, ILogger<BankingConnectionService> logger)
 : IBankingConnectionService, IBankingCallbackService
{
	public async Task<BankingResult<StartBankConnectionResult>> StartConnectionAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		var profile = await users.GetAsync(userId, cancellationToken);
		if (profile is null)
			return BankingResult<StartBankConnectionResult>.Failure(BankingErrorCode.NotFound);
		var now = time.GetUtcNow();
		var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
		var attempt = new BankConnectionAttempt
		{
			Id = Guid.NewGuid(),
			FourMoneyUserId = userId,
			Status = BankConnectionAttemptStatus.Pending,
			CreatedAt = now,
			ExpiresAt = now.AddMinutes(options.Value.AttemptLifetimeMinutes),
			CorrelationStateHash = HashState(state)
		};
		await attempts.AddAsync(attempt, cancellationToken);
		if (!profile.IsActive)
		{
			await FailAsync(attempt.Id, "USER_INACTIVE");
			return BankingResult<StartBankConnectionResult>.Failure(BankingErrorCode.Forbidden, attempt.Id);
		}
		try
		{
			profile = await users.EnsureTinkUserAsync(userId, token => tink.CreateOrRecoverUserAsync(userId, token), cancellationToken);
			if (profile is null || !profile.IsActive)
			{
				await FailAsync(attempt.Id, "USER_INACTIVE");
				return BankingResult<StartBankConnectionResult>.Failure(BankingErrorCode.Forbidden, attempt.Id);
			}
			if (string.IsNullOrWhiteSpace(profile.TinkUserId))
				throw new TinkApiException("user_mapping", "invalid_response");
			if (!await attempts.SetTinkUserAsync(attempt.Id, profile.TinkUserId, time.GetUtcNow(), cancellationToken))
				return BankingResult<StartBankConnectionResult>.Failure(BankingErrorCode.Conflict, attempt.Id);
			var url = await tink.CreateLinkAsync(profile.TinkUserId, profile.IdHint, state, cancellationToken);
			// Never return a link for a concurrently expired/finished attempt.
			var current = await attempts.GetAsync(userId, attempt.Id, time.GetUtcNow(), cancellationToken);
			if (current?.Status != BankConnectionAttemptStatus.Pending)
				return BankingResult<StartBankConnectionResult>.Failure(BankingErrorCode.Conflict, attempt.Id);
			return BankingResult<StartBankConnectionResult>.Success(new(attempt.Id, url, attempt.ExpiresAt));
		}
		catch (Exception exception) when (exception is TinkApiException or OptionsValidationException)
		{
			LogIntegrationError(exception, attempt.Id);
			await FailAsync(attempt.Id, "TINK_UNAVAILABLE");
			return BankingResult<StartBankConnectionResult>.Failure(BankingErrorCode.TemporarilyUnavailable, attempt.Id);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			await FailAsync(attempt.Id, "REQUEST_CANCELLED");
			throw;
		}
	}

	public async Task<BankingResult<BankConnectionAttemptDetails>> GetConnectionAttemptAsync(Guid userId, Guid attemptId, CancellationToken cancellationToken = default)
	{
		var attempt = await attempts.GetAsync(userId, attemptId, time.GetUtcNow(), cancellationToken);
		return attempt is null ? BankingResult<BankConnectionAttemptDetails>.Failure(BankingErrorCode.NotFound)
		 : BankingResult<BankConnectionAttemptDetails>.Success(MapAttempt(attempt));
	}

	public async Task<BankingResult<IReadOnlyList<BankConnectionDetails>>> GetConnectionsAsync(Guid userId, CancellationToken cancellationToken = default)
	 => BankingResult<IReadOnlyList<BankConnectionDetails>>.Success((await connections.GetAllAsync(userId, cancellationToken)).Select(MapConnection).ToArray());

	public async Task<BankingResult<BankConnectionDetails>> GetConnectionAsync(Guid userId, Guid connectionId, CancellationToken cancellationToken = default)
	{
		var connection = await connections.GetAsync(userId, connectionId, cancellationToken);
		return connection is null ? BankingResult<BankConnectionDetails>.Failure(BankingErrorCode.NotFound)
		 : BankingResult<BankConnectionDetails>.Success(MapConnection(connection));
	}

	public async Task<BankingResult<IReadOnlyList<BankConsentDetails>>> GetConsentsAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		var profile = await users.GetAsync(userId, cancellationToken);
		if (profile is null)
			return BankingResult<IReadOnlyList<BankConsentDetails>>.Failure(BankingErrorCode.NotFound);
		if (!profile.IsActive)
			return BankingResult<IReadOnlyList<BankConsentDetails>>.Failure(BankingErrorCode.Forbidden);
		if (string.IsNullOrWhiteSpace(profile.TinkUserId))
			return BankingResult<IReadOnlyList<BankConsentDetails>>.Success([]);

		var localConnections = (await connections.GetAllAsync(userId, cancellationToken))
		 .Where(connection => connection.FourMoneyUserId == userId && connection.TinkUserId == profile.TinkUserId).ToArray();
		if (localConnections.Length == 0)
			return BankingResult<IReadOnlyList<BankConsentDetails>>.Success([]);

		try
		{
			var consents = await tink.GetConsentsAsync(profile.TinkUserId, cancellationToken);
			var result = from consent in consents
						 join connection in localConnections on consent.CredentialsId equals connection.CredentialsId
						 select new BankConsentDetails(connection.Id, consent.ProviderStatus,
							 consent.SessionExpiresAt, consent.SessionExtendable);
			return BankingResult<IReadOnlyList<BankConsentDetails>>.Success(result.ToArray());
		}
		catch (Exception exception) when (exception is TinkApiException or OptionsValidationException)
		{
			if (exception is TinkApiException tinkError)
				logger.LogWarning("Banking consents: {Stage}, {ErrorCode}, upstream {Status}", tinkError.Stage, tinkError.ErrorCode, tinkError.UpstreamStatus);
			else
				logger.LogWarning("Banking consents: invalid Tink configuration");
			return BankingResult<IReadOnlyList<BankConsentDetails>>.Failure(BankingErrorCode.TemporarilyUnavailable);
		}
	}

	public async Task<BankingResult<BankConnectionAttemptDetails>> CompleteAsync(BankingCallback callback, CancellationToken cancellationToken = default)
	{
		if (callback.State.Length != 43 || callback.State.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')
		 || callback.CredentialsId?.Length > 128 || callback.Error?.Length > 64 || callback.ErrorReason?.Length > 128
		 || (string.IsNullOrWhiteSpace(callback.CredentialsId) == string.IsNullOrWhiteSpace(callback.Error)))
			return BankingResult<BankConnectionAttemptDetails>.Failure(BankingErrorCode.InvalidCallback);
		var attempt = await attempts.FindByStateAsync(HashState(callback.State), time.GetUtcNow(), cancellationToken);
		if (attempt is null)
			return BankingResult<BankConnectionAttemptDetails>.Failure(BankingErrorCode.InvalidCallback);
		if (attempt.Status != BankConnectionAttemptStatus.Pending)
			return BankingResult<BankConnectionAttemptDetails>.Failure(BankingErrorCode.Conflict, attempt.Id);
		if (string.IsNullOrWhiteSpace(attempt.TinkUserId))
			return BankingResult<BankConnectionAttemptDetails>.Failure(BankingErrorCode.InvalidCallback);
		var profile = await users.GetAsync(attempt.FourMoneyUserId, cancellationToken);
		if (profile is null || !profile.IsActive || profile.TinkUserId != attempt.TinkUserId)
			return BankingResult<BankConnectionAttemptDetails>.Failure(BankingErrorCode.InvalidCallback);
		BankConnection? connection = null;
		var status = BankConnectionAttemptStatus.Completed;
		string? failureCode = null;
		if (!string.IsNullOrWhiteSpace(callback.Error))
		{
			status = callback.ErrorReason == "USER_CANCELLED" ? BankConnectionAttemptStatus.Cancelled : BankConnectionAttemptStatus.Failed;
			failureCode = callback.ErrorReason switch
			{
				"USER_CANCELLED" => "USER_CANCELLED",
				"USER_DECLINED_CONSENT" => "CONSENT_DECLINED",
				_ => "BANK_AUTHORIZATION_FAILED"
			};
		}
		else
		{
			try
			{
				var verified = await tink.VerifyConnectionAsync(attempt.TinkUserId, callback.CredentialsId!, cancellationToken);
				if (verified is null)
					return BankingResult<BankConnectionAttemptDetails>.Failure(BankingErrorCode.InvalidCallback);
				var now = time.GetUtcNow();
				connection = new BankConnection
				{
					Id = Guid.NewGuid(),
					FourMoneyUserId = attempt.FourMoneyUserId,
					TinkUserId = attempt.TinkUserId,
					CredentialsId = verified.CredentialsId,
					ProviderId = verified.ProviderId,
					ConnectionStatus = BankConnectionStatus.Connected,
					SyncStatus = BankSynchronizationStatus.NotStarted,
					CreatedAt = now,
					ConnectedAt = now
				};
			}
			catch (Exception exception) when (exception is TinkApiException or OptionsValidationException)
			{
				LogIntegrationError(exception, attempt.Id);
				// A retryable verification failure must not consume the callback.
				return BankingResult<BankConnectionAttemptDetails>.Failure(BankingErrorCode.TemporarilyUnavailable, attempt.Id);
			}
		}
		var finished = await attempts.FinishAsync(attempt.Id, status, failureCode, connection, cancellationToken);
		return finished is null ? BankingResult<BankConnectionAttemptDetails>.Failure(BankingErrorCode.Conflict, attempt.Id)
		 : BankingResult<BankConnectionAttemptDetails>.Success(MapAttempt(finished));
	}

	private async Task FailAsync(Guid attemptId, string code)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		try
		{
			await attempts.FinishAsync(attemptId, BankConnectionAttemptStatus.Failed, code, null, timeout.Token);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			// The durable Pending row still expires. Never log DB/HTTP exception payloads.
			logger.LogWarning("Unable to save failure for banking attempt {AttemptId}", attemptId);
		}
	}

	private void LogIntegrationError(Exception exception, Guid attemptId)
	{
		if (exception is TinkApiException tinkError)
			logger.LogWarning("Banking attempt {AttemptId}: {Stage}, {ErrorCode}, upstream {Status}", attemptId, tinkError.Stage, tinkError.ErrorCode, tinkError.UpstreamStatus);
		else
			logger.LogWarning("Banking attempt {AttemptId}: invalid Tink configuration", attemptId);
	}

	private static string HashState(string state) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
	private static BankConnectionAttemptDetails MapAttempt(BankConnectionAttempt value) => new(value.Id, value.Status,
	 value.BankConnectionId, value.CreatedAt, value.ExpiresAt, value.CompletedAt, value.FailureCode);
	private static BankConnectionDetails MapConnection(BankConnection value) => new(value.Id, value.ProviderName,
	 value.ConnectionStatus, value.SyncStatus, value.CreatedAt, value.ConnectedAt, value.ConsentExpiresAt,
	 value.LastSuccessfulSyncAt, value.LastSyncFailureCode);
}
