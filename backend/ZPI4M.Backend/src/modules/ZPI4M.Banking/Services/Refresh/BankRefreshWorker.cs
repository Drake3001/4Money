using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZPI4M.Banking.Repositories.BankRefreshRepository;

namespace ZPI4M.Banking.Services.Refresh;

public sealed class BankRefreshWorker(IServiceScopeFactory scopes, IOptions<BankRefreshOptions> options,
	ILogger<BankRefreshWorker> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await using var scope = scopes.CreateAsyncScope();
				var repository = scope.ServiceProvider.GetRequiredService<BankRefreshRepository>();
				var job = await repository.ClaimAsync(stoppingToken);
				if (job is not null)
				{
					using var work = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
					work.CancelAfter(TimeSpan.FromMinutes(options.Value.AttemptTimeoutMinutes));
					using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
					var heartbeat = HeartbeatAsync(job.Id, job.LeaseId!.Value, work, heartbeatStop.Token);
					try
					{
						await scope.ServiceProvider.GetRequiredService<BankRefreshProcessor>().ProcessAsync(job, work.Token);
						logger.LogInformation("Banking refresh {RefreshId} attempt completed", job.Id);
					}
					finally
					{
						await heartbeatStop.CancelAsync();
						await heartbeat;
					}
				}
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
			catch (RefreshLeaseLostException) { logger.LogWarning("Banking refresh lease was lost"); }
			catch (Exception) { logger.LogError("Banking refresh worker operation failed; durable work will be recovered"); }
			try { await Task.Delay(TimeSpan.FromSeconds(options.Value.PollSeconds), stoppingToken); }
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
		}
	}

	private async Task HeartbeatAsync(Guid id, Guid lease, CancellationTokenSource work, CancellationToken ct)
	{
		try
		{
			using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.HeartbeatSeconds));
			while (await timer.WaitForNextTickAsync(ct))
			{
				await using var scope = scopes.CreateAsyncScope();
				if (!await scope.ServiceProvider.GetRequiredService<BankRefreshRepository>().RenewAsync(id, lease, ct))
				{
					await work.CancelAsync();
					return;
				}
			}
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
		catch (Exception)
		{
			logger.LogWarning("Banking refresh {RefreshId} heartbeat failed", id);
			await work.CancelAsync();
		}
	}
}
