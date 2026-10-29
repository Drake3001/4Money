using System.Runtime.ExceptionServices;

namespace ZPI4M.Receipt.IntegrationTests;

internal static class TestCleanup
{
	public static async Task RunAsync(Func<Task> operation, Func<Task> cleanup)
	{
		ExceptionDispatchInfo? operationFailure = null;
		ExceptionDispatchInfo? cleanupFailure = null;
		try
		{
			await operation();
		}
		catch (Exception error)
		{
			operationFailure = ExceptionDispatchInfo.Capture(error);
		}
		try
		{
			await cleanup();
		}
		catch (Exception error)
		{
			cleanupFailure = ExceptionDispatchInfo.Capture(error);
		}
		if (operationFailure is not null && cleanupFailure is not null)
			throw new AggregateException(operationFailure.SourceException, cleanupFailure.SourceException);
		operationFailure?.Throw();
		cleanupFailure?.Throw();
	}
}
