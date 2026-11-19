using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZPI4M.Limits.Repositories.LimitSweepRepository;
using ZPI4M.Limits.Services.Calendar;

namespace ZPI4M.Limits.Services.Alerts;

public sealed class LimitAlertSweepService(IServiceScopeFactory scopes, LimitCalendar calendar, IOptions<LimitsAlertsSweepOptions> options, ILogger<LimitAlertSweepService> logger) : ILimitAlertSweepService
{
	private bool groupCursor;
	private Guid cursor;
	public async Task<AlertSweepResult> RunAsync(CancellationToken token = default)
	{
		token.ThrowIfCancellationRequested();
		var today = calendar.Today;
		LimitSweepCandidate[] page;
		await using (var scope = scopes.CreateAsyncScope())
		{
			var repository = scope.ServiceProvider.GetRequiredService<ILimitSweepRepository>();
			page = await repository.PageAsync(groupCursor, cursor, options.Value.MaxDefinitionsPerRun, today, token);
			if (page.Length == 0)
			{
				groupCursor = false;
				cursor = Guid.Empty;
				page = await repository.PageAsync(false, cursor, options.Value.MaxDefinitionsPerRun, today, token);
			}
		}

		int evaluated = 0, skipped = 0, failed = 0, notified = 0;
		foreach (var candidate in page)
		{
			token.ThrowIfCancellationRequested();
			// Advance even on a failing/timeout candidate; it will be retried after wrapping.
			groupCursor = candidate.IsGroup;
			cursor = candidate.Id;
			try
			{
				await using var scope = scopes.CreateAsyncScope();
				var status = await scope.ServiceProvider.GetRequiredService<LimitAlertSweepStep>().ExecuteAsync(candidate, today, token);
				if (status != AlertSweepStepOutcome.Skipped)
					evaluated++;
				else
					skipped++;
				if (status == AlertSweepStepOutcome.Notified)
					notified++;
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception error)
			{
				failed++;
				logger.LogError("Budget alert evaluation failed ({ErrorType}, {LimitId}).", error.GetType().Name, candidate.Id);
			}
		}

		return new(evaluated, skipped, failed, notified);
	}
}
