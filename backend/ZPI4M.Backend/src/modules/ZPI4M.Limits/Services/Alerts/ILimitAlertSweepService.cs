namespace ZPI4M.Limits.Services.Alerts;

/// <summary>
/// Stateful sweep with an in-memory cursor. Calls on the same instance must be sequential;
/// the worker awaits each run to completion, including cooperative cancellation.
/// </summary>
public interface ILimitAlertSweepService
{
	Task<AlertSweepResult> RunAsync(CancellationToken token = default);
}
