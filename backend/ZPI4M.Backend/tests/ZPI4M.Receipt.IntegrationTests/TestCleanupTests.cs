using Xunit;

namespace ZPI4M.Receipt.IntegrationTests;

public sealed class TestCleanupTests
{
	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public async Task Cleanup_always_runs_and_preserves_operation_and_cleanup_failures(bool operationFails, bool cleanupFails)
	{
		var operationError = new IOException("synthetic operation failure");
		var cleanupError = new IOException("synthetic cleanup failure");
		var calls = new List<string>();

		var actual = await Record.ExceptionAsync(() => TestCleanup.RunAsync(() =>
		{
			calls.Add("operation");
			return operationFails ? ThrowOperation(operationError) : Task.CompletedTask;
		}, () =>
		{
			calls.Add("cleanup");
			return cleanupFails ? ThrowCleanup(cleanupError) : Task.CompletedTask;
		}));

		Assert.Equal(["operation", "cleanup"], calls);
		if (operationFails && cleanupFails)
		{
			var combined = Assert.IsType<AggregateException>(actual);
			Assert.Equal(new Exception[] { operationError, cleanupError }, combined.InnerExceptions);
			Assert.Contains(nameof(ThrowOperation), combined.InnerExceptions[0].StackTrace);
			Assert.Contains(nameof(ThrowCleanup), combined.InnerExceptions[1].StackTrace);
		}
		else if (operationFails)
		{
			Assert.Same(operationError, actual);
			Assert.Contains(nameof(ThrowOperation), actual.StackTrace);
		}
		else if (cleanupFails)
		{
			Assert.Same(cleanupError, actual);
			Assert.Contains(nameof(ThrowCleanup), actual.StackTrace);
		}
		else
			Assert.Null(actual);
	}

	private static Task ThrowOperation(Exception error) => throw error;
	private static Task ThrowCleanup(Exception error) => throw error;
}
