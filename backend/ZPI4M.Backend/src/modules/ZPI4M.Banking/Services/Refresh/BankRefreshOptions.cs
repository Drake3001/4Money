using System.ComponentModel.DataAnnotations;

namespace ZPI4M.Banking.Services.Refresh;

public sealed class BankRefreshOptions
{
	[Range(1, 3650)] public int InitialHistoryDays { get; set; } = 30;
	[Range(1, 365)] public int OverlapDays { get; set; } = 7;
	[Range(1, 10)] public int MaxAttempts { get; set; } = 3;
	[Range(1, 3600)] public int FirstRetrySeconds { get; set; } = 30;
	[Range(1, 3600)] public int SubsequentRetrySeconds { get; set; } = 120;
	[Range(1, 60)] public int AttemptTimeoutMinutes { get; set; } = 10;
	[Range(1, 60)] public int PollSeconds { get; set; } = 2;
	[Range(30, 600)] public int LeaseSeconds { get; set; } = 120;
	[Range(5, 120)] public int HeartbeatSeconds { get; set; } = 30;
	[Range(1, 10000)] public int MaxPages { get; set; } = 10000;
}
