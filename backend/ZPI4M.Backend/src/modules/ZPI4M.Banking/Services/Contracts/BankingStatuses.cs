using System.Text.Json.Serialization;

namespace ZPI4M.Banking.Services.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<BankConnectionAttemptStatus>))]
public enum BankConnectionAttemptStatus
{
	[JsonStringEnumMemberName("PENDING")] Pending,
	[JsonStringEnumMemberName("COMPLETED")] Completed,
	[JsonStringEnumMemberName("CANCELLED")] Cancelled,
	[JsonStringEnumMemberName("FAILED")] Failed,
	[JsonStringEnumMemberName("EXPIRED")] Expired
}

[JsonConverter(typeof(JsonStringEnumConverter<BankConnectionStatus>))]
public enum BankConnectionStatus
{
	[JsonStringEnumMemberName("CONNECTED")] Connected,
	[JsonStringEnumMemberName("REAUTH_REQUIRED")] ReauthRequired,
	[JsonStringEnumMemberName("DISCONNECTED")] Disconnected
}

[JsonConverter(typeof(JsonStringEnumConverter<BankSynchronizationStatus>))]
public enum BankSynchronizationStatus
{
	[JsonStringEnumMemberName("NOT_STARTED")] NotStarted,
	[JsonStringEnumMemberName("QUEUED")] Queued,
	[JsonStringEnumMemberName("RUNNING")] Running,
	[JsonStringEnumMemberName("SUCCEEDED")] Succeeded,
	[JsonStringEnumMemberName("FAILED")] Failed
}
