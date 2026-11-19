using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using ZPI4M.Limits.Services;
using ZPI4M.Limits.Services.Alerts;

namespace ZPI4M.Limits.UnitTests;

public sealed class AlertSweepWorkerTests
{
	private sealed class Probe : ILimitAlertSweepService
	{
		public Channel<CancellationToken> Calls { get; } = Channel.CreateUnbounded<CancellationToken>();
		public Func<CancellationToken, Task<AlertSweepResult>> Action { get; set; } = _ => Task.FromResult(new AlertSweepResult(0, 0, 0));

		public Task<AlertSweepResult> RunAsync(CancellationToken token = default)
		{
			Calls.Writer.TryWrite(token);
			return Action(token);
		}
	}

	private static LimitAlertSweepWorker Worker(Probe service, FakeTimeProvider clock, LimitsAlertsSweepOptions settings) => new(service, Options.Create(settings), clock, NullLogger<LimitAlertSweepWorker>.Instance);
	[Fact]
	public async Task DisabledWorkerDoesNotCallService()
	{
		var probe = new Probe();
		using var worker = Worker(probe, new(), new() { Enabled = false });
		await worker.StartAsync(default);
		await worker.ExecuteTask!;
		Assert.False(probe.Calls.Reader.TryRead(out _));
	}

	[Fact]
	public async Task StartupAndShutdownCancelInflightWorkWithoutOverlap()
	{
		var probe = new Probe
		{
			Action = async token =>
			{
				await Task.Delay(Timeout.Infinite, token);
				return new(0, 0, 0);
			}
		};
		using var worker = Worker(probe, new(), new());
		await worker.StartAsync(default);
		var token = await probe.Calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
		await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
		Assert.True(token.IsCancellationRequested);
		Assert.False(probe.Calls.Reader.TryRead(out _));
	}

	[Fact]
	public async Task TimeoutDoesNotOverlapARunThatDelaysRespondingToCancellation()
	{
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var finish = new TaskCompletionSource<AlertSweepResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		var probe = new Probe
		{
			Action = _ =>
			{
				started.TrySetResult();
				// The operation deliberately keeps running after its token is cancelled.
				return finish.Task;
			}
		};
		var clock = new FakeTimeProvider();
		using var worker = Worker(probe, clock, new() { RunTimeoutSeconds = 10, IntervalSeconds = 5 });
		await worker.StartAsync(default);
		Task? stopping = null;
		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
			var token = await probe.Calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
			clock.Advance(TimeSpan.FromSeconds(30)); // Beyond the timeout and several run intervals.
			Assert.True(token.IsCancellationRequested);
			Assert.False(finish.Task.IsCompleted);
			Assert.False(probe.Calls.Reader.TryRead(out _));
			stopping = worker.StopAsync(default);
			Assert.False(stopping.IsCompleted);
			Assert.False(probe.Calls.Reader.TryRead(out _));
		}
		finally
		{
			finish.TrySetCanceled();
			await (stopping ?? worker.StopAsync(default)).WaitAsync(TimeSpan.FromSeconds(5));
		}
		Assert.False(probe.Calls.Reader.TryRead(out _));
	}

	[Fact]
	public async Task DeferredStartupWaitsAndUnexpectedFailureIsRetried()
	{
		var probe = new Probe();
		var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		probe.Action = _ =>
		{
			first.TrySetResult();
			throw new InvalidOperationException("Synthetic");
		};
		// A real TimeProvider with a one-second interval avoids racing FakeTimeProvider timer registration.
		using var worker = new LimitAlertSweepWorker(probe, Options.Create(new LimitsAlertsSweepOptions { RunOnStartup = false, IntervalSeconds = 1 }), TimeProvider.System, NullLogger<LimitAlertSweepWorker>.Instance);
		await worker.StartAsync(default);
		Assert.False(probe.Calls.Reader.TryRead(out _));
		await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await probe.Calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
		await probe.Calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
		await worker.StopAsync(default);
	}

	[Theory]
	[InlineData("IntervalSeconds", "0")]
	[InlineData("RunTimeoutSeconds", "86401")]
	[InlineData("MaxDefinitionsPerRun", "0")]
	public void OptionsAreValidatedEvenWhenDisabled(string key, string value)
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "LimitsAlertsSweep:Enabled", "false" }, { "LimitsAlertsSweep:" + key, value } }).Build());
		services.AddLimits();
		using var provider = services.BuildServiceProvider();
		Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<LimitsAlertsSweepOptions>>().Value);
	}
}
