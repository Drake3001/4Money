using ZPI4M.Banking.Services.Contracts;

namespace ZPI4M.Banking.Services.Connections;

public interface IBankingCallbackService
{
	Task<BankingResult<BankConnectionAttemptDetails>> CompleteAsync(BankingCallback callback,
	 CancellationToken cancellationToken = default);
}
