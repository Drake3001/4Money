namespace ZPI4M.Limits.Services.Alerts;

public sealed record AlertSweepResult(int Evaluated, int Skipped, int Failed, int Notified = 0);
