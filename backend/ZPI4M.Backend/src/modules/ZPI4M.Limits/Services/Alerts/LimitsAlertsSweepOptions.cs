namespace ZPI4M.Limits.Services.Alerts;

public sealed class LimitsAlertsSweepOptions
{
	public bool Enabled { get; set; } = true;
	public bool RunOnStartup { get; set; } = true;
	public int IntervalSeconds { get; set; } = 60;
	public int RunTimeoutSeconds { get; set; } = 300;
	public int MaxDefinitionsPerRun { get; set; } = 100;
}
