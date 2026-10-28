namespace ZPI4M.Banking.Services.Contracts;

// ProviderStatus is the reported state, not a local connection status or a validity guarantee.
public sealed record BankConsentDetails(Guid ConnectionId, string ProviderStatus,
 DateTimeOffset? SessionExpiresAt, bool? SessionExtendable);
