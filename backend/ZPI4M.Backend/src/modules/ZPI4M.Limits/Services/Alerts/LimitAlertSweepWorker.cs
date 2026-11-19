using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ZPI4M.Limits.Services.Alerts;

public sealed class LimitAlertSweepWorker(ILimitAlertSweepService sweep, IOptions<LimitsAlertsSweepOptions> options, TimeProvider clock, ILogger<LimitAlertSweepWorker> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var settings = options.Value;
		if (!settings.Enabled)
			return;
		try
		{
			if (!settings.RunOnStartup)
				await Task.Delay(TimeSpan.FromSeconds(settings.IntervalSeconds), clock, stoppingToken);
			while (!stoppingToken.IsCancellationRequested)
			{
				var started = clock.GetTimestamp();
				using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.RunTimeoutSeconds), clock);
				using var run = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeout.Token);
				try
				{
					var result = await sweep.RunAsync(run.Token);
					logger.Log(result.Evaluated + result.Skipped + result.Failed == 0 ? LogLevel.Debug : LogLevel.Information, "Budget alert sweep completed in {ElapsedMs} ms: Evaluated={Evaluated}, Skipped={Skipped}, Failed={Failed}, Notified={Notified}.", clock.GetElapsedTime(started).TotalMilliseconds, result.Evaluated, result.Skipped, result.Failed, result.Notified);
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
					break;
				}
				catch (OperationCanceledException) when (timeout.IsCancellationRequested)
				{
					logger.LogWarning("Budget alert sweep timed out.");
				}
				catch (Exception error)
				{
					logger.LogError("Budget alert sweep failed ({ErrorType}).", error.GetType().Name);
				}

				await Task.Delay(TimeSpan.FromSeconds(settings.IntervalSeconds), clock, stoppingToken);
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}
	}
}
