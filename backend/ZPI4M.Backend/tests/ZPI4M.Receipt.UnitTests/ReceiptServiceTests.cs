using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using ZPI4M.Receipt.Database;
using ZPI4M.Receipt.DTOs;
using ZPI4M.Receipt.Repositories.ReceiptRepository;
using ZPI4M.Receipt.Services;

namespace ZPI4M.Receipt.UnitTests;

public sealed class ReceiptServiceTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Create_with_http_analysis_preserves_response_and_only_uploads_after_success(bool fails)
	{
		string? sentContent = null;
		using var handler = new HttpReceiptAnalysisServiceTests.Handler(async (request, token) =>
		{
			var file = Assert.Single(Assert.IsType<MultipartFormDataContent>(request.Content));
			sentContent = await file.ReadAsStringAsync(token);
			return fails ? new HttpResponseMessage(System.Net.HttpStatusCode.UnprocessableEntity)
				: HttpReceiptAnalysisServiceTests.JsonResponse(HttpReceiptAnalysisServiceTests.ResponseJson);
		});
		using var client = HttpReceiptAnalysisServiceTests.Client(handler);
		using var fixture = new Fixture(HttpReceiptAnalysisServiceTests.Service(client));
		using var input = new MemoryStream(Encoding.UTF8.GetBytes("skip:synthetic image"));
		input.Position = 5;
		if (fails)
		{
			await Assert.ThrowsAsync<ReceiptAnalysisException>(() => fixture.Service.CreateAsync(input, "image/png", runOcr: true));
			Assert.Equal(0, fixture.Storage.UploadCalls);
			Assert.Empty(await fixture.Context.Receipts.ToListAsync());
		}
		else
		{
			var result = await fixture.Service.CreateAsync(input, "image/png", runOcr: true);
			fixture.Context.ChangeTracker.Clear();
			var receipt = await fixture.Service.GetByIdAsync(result.ReceiptId);
			Assert.NotNull(receipt);
			Assert.Equal(HttpReceiptAnalysisServiceTests.ResponseJson, receipt.ExtractionResult);
			Assert.Equal(receipt.ExtractionResult, result.ExtractionResult?.GetRawText());
			Assert.Equal(sentContent, fixture.Storage.Content);
			Assert.Equal(1, fixture.Storage.UploadCalls);
		}
		Assert.Equal("synthetic image", sentContent);
		Assert.True(input.CanRead);
		Assert.Equal(1, handler.Calls);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Create_persists_receipt_and_passes_remaining_content_to_requested_providers(bool runOcr)
	{
		using var fixture = new Fixture();
		using var input = new MemoryStream(Encoding.UTF8.GetBytes("skip:receipt image"));
		input.Position = 5;
		using var cancellation = new CancellationTokenSource();

		var result = await fixture.Service.CreateAsync(input, "image/custom", runOcr: runOcr, cancellationToken: cancellation.Token);

		fixture.Context.ChangeTracker.Clear();
		var receipt = await fixture.Service.GetByIdAsync(result.ReceiptId);
		Assert.NotEqual(Guid.Empty, result.ReceiptId);
		Assert.NotNull(receipt);
		Assert.Equal(fixture.Storage.Key, receipt.StorageKey);
		Assert.Equal(runOcr ? fixture.Analysis.Json : null, receipt.ExtractionResult);
		Assert.Equal(runOcr ? "receipt image" : null, fixture.Analysis.Content);
		Assert.Equal("receipt image", fixture.Storage.Content);
		Assert.Equal(runOcr ? "image/custom" : null, fixture.Analysis.ContentType);
		Assert.Equal("image/custom", fixture.Storage.ContentType);
		Assert.Equal(runOcr ? cancellation.Token : default, fixture.Analysis.Token);
		Assert.Equal(cancellation.Token, fixture.Storage.UploadToken);
		Assert.True(input.CanRead);
		AssertTemporaryFileDeleted(fixture.Storage.TemporaryPath);
		Assert.Equal(runOcr ? 1 : 0, fixture.Analysis.Calls);
		Assert.Equal(1, fixture.Storage.UploadCalls);
		Assert.Equal(receipt.ExtractionResult, result.ExtractionResult?.GetRawText());
	}

	[Fact]
	public async Task Create_without_ocr_does_not_send_http_request_and_returns_no_analysis()
	{
		using var handler = new HttpReceiptAnalysisServiceTests.Handler((_, _) => throw new InvalidOperationException("OCR must not be called."));
		using var client = HttpReceiptAnalysisServiceTests.Client(handler);
		using var fixture = new Fixture(HttpReceiptAnalysisServiceTests.Service(client));
		using var input = new MemoryStream(Encoding.UTF8.GetBytes("synthetic image"));

		var result = await fixture.Service.CreateAsync(input, "image/png", runOcr: false);

		fixture.Context.ChangeTracker.Clear();
		var receipt = await fixture.Service.GetByIdAsync(result.ReceiptId);
		Assert.NotEqual(Guid.Empty, result.ReceiptId);
		Assert.NotNull(receipt);
		Assert.Null(receipt.ExtractionResult);
		Assert.Null(result.ExtractionResult);
		Assert.Equal(fixture.Storage.Key, receipt.StorageKey);
		Assert.Equal("synthetic image", fixture.Storage.Content);
		Assert.Equal(1, fixture.Storage.UploadCalls);
		Assert.Equal(0, handler.Calls);
		Assert.True(input.CanRead);
		AssertTemporaryFileDeleted(fixture.Storage.TemporaryPath);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Create_accepts_a_non_seekable_stream_without_closing_it(bool runOcr)
	{
		using var fixture = new Fixture();
		using var input = new NonSeekableStream("image");

		await fixture.Service.CreateAsync(input, "image/png", runOcr: runOcr);

		Assert.Equal(runOcr ? "image" : null, fixture.Analysis.Content);
		Assert.Equal("image", fixture.Storage.Content);
		Assert.True(input.CanRead);
		AssertTemporaryFileDeleted(fixture.Storage.TemporaryPath);
		Assert.Equal(runOcr ? 1 : 0, fixture.Analysis.Calls);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Create_rejects_null_content(bool runOcr)
	{
		using var fixture = new Fixture();
		await Assert.ThrowsAsync<ArgumentNullException>(() => fixture.Service.CreateAsync(null!, "image/png", runOcr: runOcr));
		AssertNoProviderCalls(fixture);
	}

	[Theory]
	[InlineData(null, false)]
	[InlineData(null, true)]
	[InlineData("", false)]
	[InlineData("", true)]
	[InlineData(" \t", false)]
	[InlineData(" \t", true)]
	public async Task Create_rejects_missing_content_type(string? contentType, bool runOcr)
	{
		using var fixture = new Fixture();
		using var input = new MemoryStream([1]);
		await Assert.ThrowsAnyAsync<ArgumentException>(() => fixture.Service.CreateAsync(input, contentType!, runOcr: runOcr));
		AssertNoProviderCalls(fixture);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Create_rejects_unreadable_content(bool runOcr)
	{
		using var fixture = new Fixture();
		var input = new MemoryStream([1]);
		input.Dispose();
		await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateAsync(input, "image/png", runOcr: runOcr));
		AssertNoProviderCalls(fixture);
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public async Task Create_rejects_empty_remaining_content(bool atEnd, bool runOcr)
	{
		using var fixture = new Fixture();
		using var input = new MemoryStream(atEnd ? [1] : []);
		input.Position = input.Length;
		await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateAsync(input, "image/png", runOcr: runOcr));
		AssertNoProviderCalls(fixture);
		Assert.True(input.CanRead);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Create_honors_cancellation_before_reading(bool runOcr)
	{
		using var fixture = new Fixture();
		using var input = new MemoryStream([1]);
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.CreateAsync(input, "image/png", runOcr: runOcr, cancellationToken: cancellation.Token));
		Assert.Equal(0, input.Position);
		AssertNoProviderCalls(fixture);
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public async Task Create_cleans_temporary_file_when_copy_fails_or_is_cancelled(bool cancelled, bool runOcr)
	{
		using var fixture = new Fixture();
		var error = cancelled ? (Exception)new OperationCanceledException() : new IOException("read failure");
		using var input = new FailingCopyStream(error);
		var actual = await Record.ExceptionAsync(() => fixture.Service.CreateAsync(input, "image/png", runOcr: runOcr));
		Assert.Same(error, actual);
		AssertTemporaryFileDeleted(input.TemporaryPath);
		AssertNoProviderCalls(fixture);
		Assert.True(input.CanRead);
	}

	[Fact]
	public async Task Create_stops_before_upload_when_analysis_fails()
	{
		using var fixture = new Fixture();
		var error = new InvalidOperationException("analysis failure");
		fixture.Analysis.Error = error;
		using var input = new MemoryStream([1]);

		var actual = await Record.ExceptionAsync(() => fixture.Service.CreateAsync(input, "image/png", runOcr: true));

		Assert.Same(error, actual);
		Assert.Equal(0, fixture.Storage.UploadCalls);
		Assert.Empty(await fixture.Context.Receipts.ToListAsync());
		AssertTemporaryFileDeleted(fixture.Analysis.TemporaryPath);
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public async Task Create_does_not_persist_or_delete_an_unknown_file_when_upload_fails(bool runOcr, bool cancelled)
	{
		using var fixture = new Fixture();
		using var cancellation = new CancellationTokenSource();
		var error = cancelled ? (Exception)new OperationCanceledException(cancellation.Token) : new IOException("upload failure");
		fixture.Storage.UploadError = error;
		using var input = new MemoryStream([1]);

		var actual = await Record.ExceptionAsync(() => fixture.Service.CreateAsync(input, "image/png", runOcr: runOcr, cancellationToken: cancellation.Token));

		Assert.Same(error, actual);
		Assert.Empty(fixture.Storage.DeletedKeys);
		Assert.Empty(await fixture.Context.Receipts.ToListAsync());
		AssertTemporaryFileDeleted(fixture.Storage.TemporaryPath);
	}

	[Theory]
	[InlineData("", false)]
	[InlineData("", true)]
	[InlineData(" \t", false)]
	[InlineData(" \t", true)]
	public async Task Create_rejects_an_empty_storage_key(string key, bool runOcr)
	{
		using var fixture = new Fixture();
		fixture.Storage.Key = key;
		using var input = new MemoryStream([1]);
		await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CreateAsync(input, "image/png", runOcr: runOcr));
		Assert.Empty(await fixture.Context.Receipts.ToListAsync());
		Assert.Empty(fixture.Storage.DeletedKeys);
		AssertTemporaryFileDeleted(fixture.Storage.TemporaryPath);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Create_preserves_save_error_and_file_and_only_detaches_new_receipt(bool runOcr)
	{
		using var fixture = new Fixture();
		var existing = await fixture.SeedAsync();
		var error = new DbUpdateException("sensitive save failure");
		fixture.Interceptor.Error = error;
		using var input = new MemoryStream([1]);

		var actual = await Record.ExceptionAsync(() => fixture.Service.CreateAsync(input, "image/png", runOcr: runOcr));

		Assert.Same(error, actual);
		Assert.Empty(fixture.Storage.DeletedKeys);
		Assert.Same(existing, Assert.Single(fixture.Context.ChangeTracker.Entries()).Entity);
		Assert.Equal(EntityState.Unchanged, fixture.Context.Entry(existing).State);
		Assert.Equal(existing.Id, (await fixture.Context.Receipts.SingleAsync()).Id);
		AssertTemporaryFileDeleted(fixture.Storage.TemporaryPath);
		AssertRetainedFileWarning(fixture, error);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Create_preserves_file_and_original_cancellation_when_save_is_cancelled(bool runOcr)
	{
		using var fixture = new Fixture();
		using var cancellation = new CancellationTokenSource();
		fixture.Interceptor.OnSaving = cancellation.Cancel;
		fixture.Interceptor.Error = new OperationCanceledException(cancellation.Token);
		using var input = new MemoryStream([1]);

		var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.CreateAsync(input, "image/png", runOcr: runOcr, cancellationToken: cancellation.Token));

		Assert.Same(fixture.Interceptor.Error, error);
		Assert.Equal(cancellation.Token, error.CancellationToken);
		Assert.Empty(fixture.Storage.DeletedKeys);
		Assert.Empty(fixture.Context.ChangeTracker.Entries());
		Assert.Empty(await fixture.Context.Receipts.ToListAsync());
		AssertTemporaryFileDeleted(fixture.Storage.TemporaryPath);
		AssertRetainedFileWarning(fixture, error);
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public async Task Create_retains_persisted_receipt_and_file_when_acknowledgement_fails(bool cancelled, bool runOcr)
	{
		using var fixture = new Fixture();
		using var cancellation = new CancellationTokenSource();
		var error = cancelled ? (Exception)new OperationCanceledException("sensitive cancellation", cancellation.Token)
			: new IOException("sensitive acknowledgement failure");
		fixture.Interceptor.AfterSaveError = error;
		if (cancelled)
			fixture.Interceptor.OnSaved = cancellation.Cancel;
		using var input = new MemoryStream([1]);

		var actual = await Record.ExceptionAsync(() => fixture.Service.CreateAsync(input, "image/png", runOcr: runOcr, cancellationToken: cancellation.Token));

		Assert.Same(error, actual);
		Assert.Empty(fixture.Context.ChangeTracker.Entries());
		Assert.Empty(fixture.Storage.DeletedKeys);
		using var verification = new ReceiptDbContext(fixture.ContextOptions);
		var receipt = await verification.Receipts.SingleAsync();
		Assert.Equal(fixture.Storage.Key, receipt.StorageKey);
		Assert.Equal(runOcr ? fixture.Analysis.Json : null, receipt.ExtractionResult);
		AssertTemporaryFileDeleted(fixture.Storage.TemporaryPath);
		AssertRetainedFileWarning(fixture, error);
		Assert.Contains(receipt.Id.ToString(), fixture.Logger.Messages[0]);
	}

	[Fact]
	public async Task Get_returns_null_for_missing_receipt()
	{
		using var fixture = new Fixture();
		Assert.Null(await fixture.Service.GetByIdAsync(Guid.NewGuid()));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Create_preserves_uploaded_file_and_other_entries_when_repository_add_fails(bool runOcr)
	{
		using var fixture = new Fixture();
		var existing = await fixture.SeedAsync();
		var error = new InvalidOperationException("sensitive repository failure");
		var repository = new FailingAddRepository(fixture.Context, error);
		var service = new ReceiptService(repository, fixture.Context, fixture.Analysis, fixture.Storage, fixture.Logger, fixture.Clock);
		using var input = new MemoryStream([1]);

		var actual = await Record.ExceptionAsync(() => service.CreateAsync(input, "image/png", runOcr: runOcr));

		Assert.Same(error, actual);
		Assert.Equal(1, fixture.Storage.UploadCalls);
		Assert.Empty(fixture.Storage.DeletedKeys);
		Assert.Same(existing, Assert.Single(fixture.Context.ChangeTracker.Entries()).Entity);
		Assert.Equal(EntityState.Unchanged, fixture.Context.Entry(existing).State);
		Assert.Equal(existing.Id, (await fixture.Context.Receipts.SingleAsync()).Id);
		AssertTemporaryFileDeleted(fixture.Storage.TemporaryPath);
		AssertRetainedFileWarning(fixture, error);
	}

	[Fact]
	public async Task GetFileUrlById_preserves_storage_error_and_receipt()
	{
		using var fixture = new Fixture();
		var receipt = await fixture.SeedAsync();
		var error = new IOException("synthetic signing failure");
		fixture.Storage.UrlError = error;

		var actual = await Record.ExceptionAsync(() => fixture.Service.GetFileUrlByIdAsync(receipt.Id));

		Assert.Same(error, actual);
		Assert.Equal(receipt.StorageKey, fixture.Storage.UrlKey);
		Assert.Equal(EntityState.Unchanged, fixture.Context.Entry(receipt).State);
		using var verification = new ReceiptDbContext(fixture.ContextOptions);
		Assert.Equal(receipt.StorageKey, (await verification.Receipts.SingleAsync()).StorageKey);
		Assert.Empty(fixture.Storage.DeletedKeys);
	}

	[Fact]
	public async Task GetFileUrlById_passes_key_and_token_and_returns_storage_result()
	{
		using var fixture = new Fixture();
		var receipt = await fixture.SeedAsync();
		using var cancellation = new CancellationTokenSource();
		var result = await fixture.Service.GetFileUrlByIdAsync(receipt.Id, cancellation.Token);
		Assert.Same(fixture.Storage.UrlResult, result);
		Assert.Equal(receipt.StorageKey, fixture.Storage.UrlKey);
		Assert.Equal(cancellation.Token, fixture.Storage.UrlToken);
	}

	[Fact]
	public async Task GetFileUrlById_throws_for_missing_receipt_without_calling_storage()
	{
		using var fixture = new Fixture();
		await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.GetFileUrlByIdAsync(Guid.NewGuid()));
		Assert.Null(fixture.Storage.UrlKey);
	}

	[Fact]
	public async Task Delete_commits_database_before_deleting_file_and_is_idempotent()
	{
		using var fixture = new Fixture();
		var receipt = await fixture.SeedAsync();
		using var cancellation = new CancellationTokenSource();
		fixture.Storage.OnDelete = async () =>
		{
			Assert.False(await fixture.Context.Receipts.AnyAsync());
			cancellation.Cancel();
		};

		await fixture.Service.DeleteAsync(receipt.Id, cancellation.Token);
		await fixture.Service.DeleteAsync(receipt.Id);

		Assert.Equal([receipt.StorageKey], fixture.Storage.DeletedKeys);
		Assert.Null(await fixture.Service.GetByIdAsync(receipt.Id));
		Assert.NotEqual(cancellation.Token, fixture.Storage.DeleteToken);
		Assert.True(fixture.Storage.DeleteToken.CanBeCanceled);
		Assert.False(fixture.Storage.DeleteToken.IsCancellationRequested);
	}

	[Fact]
	public async Task Delete_missing_receipt_does_not_save_or_call_storage()
	{
		using var fixture = new Fixture();
		fixture.Interceptor.Error = new DbUpdateException("unexpected save");
		await fixture.Service.DeleteAsync(Guid.NewGuid());
		Assert.Empty(fixture.Storage.DeletedKeys);
	}

	[Fact]
	public async Task Delete_cancellation_during_save_preserves_record_file_and_tracking_state()
	{
		using var fixture = new Fixture();
		var receipt = await fixture.SeedAsync();
		using var cancellation = new CancellationTokenSource();
		var error = new OperationCanceledException(cancellation.Token);
		fixture.Interceptor.OnSaving = cancellation.Cancel;
		fixture.Interceptor.Error = error;

		var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => fixture.Service.DeleteAsync(receipt.Id, cancellation.Token));

		Assert.Same(error, actual);
		Assert.Equal(cancellation.Token, actual.CancellationToken);
		Assert.Empty(fixture.Storage.DeletedKeys);
		Assert.Equal(EntityState.Unchanged, fixture.Context.Entry(receipt).State);
		using var verification = new ReceiptDbContext(fixture.ContextOptions);
		Assert.Equal(receipt.Id, (await verification.Receipts.SingleAsync()).Id);
	}

	[Fact]
	public async Task Delete_cleanup_times_out_after_ten_seconds_and_logs_without_exposing_data()
	{
		using var fixture = new Fixture();
		var receipt = await fixture.SeedAsync();
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		fixture.Storage.OnDelete = async () =>
		{
			started.SetResult();
			await Task.Delay(Timeout.Infinite, fixture.Storage.DeleteToken);
		};

		var deletion = fixture.Service.DeleteAsync(receipt.Id);
		await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
		using var verification = new ReceiptDbContext(fixture.ContextOptions);
		Assert.Empty(await verification.Receipts.ToListAsync());
		fixture.Clock.Advance(TimeSpan.FromSeconds(9));
		Assert.False(fixture.Storage.DeleteToken.IsCancellationRequested);
		Assert.False(deletion.IsCompleted);
		fixture.Clock.Advance(TimeSpan.FromSeconds(1));
		await deletion.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.True(fixture.Storage.DeleteToken.IsCancellationRequested);
		var message = Assert.Single(fixture.Logger.Messages);
		Assert.Equal($"Receipt {receipt.Id}: file cleanup failed (TaskCanceledException).", message);
		Assert.Null(Assert.Single(fixture.Logger.Exceptions));
		Assert.Equal(LogLevel.Warning, Assert.Single(fixture.Logger.Levels));
		Assert.Equal([receipt.StorageKey], fixture.Storage.DeletedKeys);
	}

	[Fact]
	public async Task Delete_caller_cancellation_after_commit_does_not_interrupt_cleanup()
	{
		using var fixture = new Fixture();
		var receipt = await fixture.SeedAsync();
		using var cancellation = new CancellationTokenSource();
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		fixture.Storage.OnDelete = async () =>
		{
			started.SetResult();
			await release.Task.WaitAsync(fixture.Storage.DeleteToken);
		};

		var deletion = fixture.Service.DeleteAsync(receipt.Id, cancellation.Token);
		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
			using var verification = new ReceiptDbContext(fixture.ContextOptions);
			Assert.Empty(await verification.Receipts.ToListAsync());
			cancellation.Cancel();
			Assert.False(fixture.Storage.DeleteToken.IsCancellationRequested);
			Assert.False(deletion.IsCompleted);
		}
		finally
		{
			release.TrySetResult();
			await deletion.WaitAsync(TimeSpan.FromSeconds(5));
		}

		Assert.Empty(fixture.Logger.Messages);
		Assert.Equal([receipt.StorageKey], fixture.Storage.DeletedKeys);
	}

	[Fact]
	public async Task Delete_save_failure_preserves_file_record_and_original_tracking_state()
	{
		using var fixture = new Fixture();
		var receipt = await fixture.SeedAsync();
		var error = new DbUpdateException("save failure");
		fixture.Interceptor.Error = error;

		var actual = await Record.ExceptionAsync(() => fixture.Service.DeleteAsync(receipt.Id));

		Assert.Same(error, actual);
		Assert.Empty(fixture.Storage.DeletedKeys);
		Assert.Equal(EntityState.Unchanged, fixture.Context.Entry(receipt).State);
		fixture.Context.ChangeTracker.Clear();
		Assert.NotNull(await fixture.Service.GetByIdAsync(receipt.Id));
	}

	[Fact]
	public async Task Delete_storage_failure_logs_only_id_and_error_type_and_still_succeeds()
	{
		using var fixture = new Fixture();
		var receipt = await fixture.SeedAsync();
		fixture.Storage.DeleteError = new IOException("sensitive URL and OCR");

		await fixture.Service.DeleteAsync(receipt.Id);

		Assert.Null(await fixture.Service.GetByIdAsync(receipt.Id));
		var message = Assert.Single(fixture.Logger.Messages);
		Assert.Contains(receipt.Id.ToString(), message);
		Assert.Contains("IOException", message);
		Assert.DoesNotContain("sensitive", message);
		Assert.All(fixture.Logger.Exceptions, error => Assert.Null(error));
	}

	private static void AssertRetainedFileWarning(Fixture fixture, Exception error)
	{
		var message = Assert.Single(fixture.Logger.Messages);
		Assert.Matches($"^Receipt [a-f0-9-]{{36}}: persistence failed; uploaded file retained \\({error.GetType().Name}\\)\\.$", message);
		Assert.DoesNotContain("sensitive", message);
		Assert.DoesNotContain(fixture.Storage.Key, message);
		Assert.DoesNotContain(fixture.Storage.UrlResult.Url, message);
		Assert.DoesNotContain(fixture.Analysis.Json, message);
		Assert.Null(Assert.Single(fixture.Logger.Exceptions));
		Assert.Equal(LogLevel.Warning, Assert.Single(fixture.Logger.Levels));
	}

	private static void AssertNoProviderCalls(Fixture fixture)
	{
		Assert.Equal(0, fixture.Analysis.Calls);
		Assert.Equal(0, fixture.Storage.UploadCalls);
		Assert.Empty(fixture.Storage.DeletedKeys);
	}

	private static void AssertTemporaryFileDeleted(string? path)
	{
		Assert.NotNull(path);
		Assert.False(File.Exists(path));
	}

	private sealed class Fixture : IDisposable
	{
		public SaveInterceptor Interceptor { get; } = new();
		public DbContextOptions<ReceiptDbContext> ContextOptions { get; }
		public ReceiptDbContext Context { get; }
		public AnalysisStub Analysis { get; } = new();
		public StorageStub Storage { get; } = new();
		public TestLogger Logger { get; } = new();
		public ReceiptService Service { get; }
		public FakeTimeProvider Clock { get; } = new();

		public Fixture(IReceiptAnalysisService? analysis = null)
		{
			ContextOptions = new DbContextOptionsBuilder<ReceiptDbContext>()
				.UseInMemoryDatabase(Guid.NewGuid().ToString())
				.AddInterceptors(Interceptor)
				.Options;
			Context = new ReceiptDbContext(ContextOptions);
			Service = new ReceiptService(new ReceiptRepository(Context), Context, analysis ?? Analysis, Storage, Logger, Clock);
		}

		public async Task<Models.Receipt> SeedAsync()
		{
			var receipt = new Models.Receipt { Id = Guid.NewGuid(), StorageKey = Storage.Key };
			Context.Receipts.Add(receipt);
			await Context.SaveChangesAsync();
			return receipt;
		}

		public void Dispose() => Context.Dispose();
	}

	private sealed class FailingAddRepository(ReceiptDbContext context, Exception error) : IReceiptRepository
	{
		public void Add(Models.Receipt entity)
		{
			context.Receipts.Add(entity);
			throw error;
		}
		public void Remove(Models.Receipt entity) => throw new NotSupportedException();
		public Task<Models.Receipt?> GetByIdAsync(Guid receiptId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public Task<bool> ExistsAsync(Guid receiptId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
	}

	private sealed class SaveInterceptor : SaveChangesInterceptor
	{
		public Exception? Error { get; set; }
		public Action? OnSaving { get; set; }
		public Exception? AfterSaveError { get; set; }
		public Action? OnSaved { get; set; }

		public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
			DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
		{
			OnSaving?.Invoke();
			if (Error is not null)
				throw Error;
			return ValueTask.FromResult(result);
		}

		public override ValueTask<int> SavedChangesAsync(
			SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
		{
			OnSaved?.Invoke();
			if (AfterSaveError is not null)
				throw AfterSaveError;
			return ValueTask.FromResult(result);
		}
	}

	private sealed class AnalysisStub : IReceiptAnalysisService
	{
		public string Json { get; } = "{\"receipt\":{\"store\":\"Sklep\",\"total\":\"6.00\",\"items\":[]},\"warnings\":[\"synthetic warning\"],\"needs_review\":true}";
		public string? Content { get; private set; }
		public string? ContentType { get; private set; }
		public string? TemporaryPath { get; private set; }
		public CancellationToken Token { get; private set; }
		public int Calls { get; private set; }
		public Exception? Error { get; set; }

		public async Task<JsonElement> AnalyzeAsync(Stream content, string contentType, CancellationToken cancellationToken = default)
		{
			Calls++;
			TemporaryPath = Assert.IsType<FileStream>(content).Name;
			Token = cancellationToken;
			ContentType = contentType;
			using var reader = new StreamReader(content, leaveOpen: true);
			Content = await reader.ReadToEndAsync(cancellationToken);
			if (Error is not null)
				throw Error;
			using var document = JsonDocument.Parse(Json);
			return document.RootElement.Clone();
		}
	}

	private sealed class StorageStub : IFileStorageService
	{
		public string? TemporaryPath { get; private set; }
		public string Key { get; set; } = "synthetic/receipt-key";
		public string? Content { get; private set; }
		public string? ContentType { get; private set; }
		public string? UrlKey { get; private set; }
		public CancellationToken UploadToken { get; private set; }
		public CancellationToken UrlToken { get; private set; }
		public CancellationToken DeleteToken { get; private set; }
		public int UploadCalls { get; private set; }
		public List<string> DeletedKeys { get; } = [];
		public Exception? UploadError { get; set; }
		public Exception? DeleteError { get; set; }
		public Exception? UrlError { get; set; }
		public Func<Task>? OnDelete { get; set; }
		public FileUrlResult UrlResult { get; } = new("https://example.invalid/receipt", DateTimeOffset.UtcNow.AddMinutes(5));

		public async Task<string> UploadAsync(Stream content, string contentType, CancellationToken cancellationToken = default)
		{
			UploadCalls++;
			TemporaryPath = Assert.IsType<FileStream>(content).Name;
			UploadToken = cancellationToken;
			ContentType = contentType;
			using var reader = new StreamReader(content, leaveOpen: true);
			Content = await reader.ReadToEndAsync(cancellationToken);
			if (UploadError is not null)
				throw UploadError;
			return Key;
		}

		public Task<FileUrlResult> GetUrlAsync(string key, CancellationToken cancellationToken = default)
		{
			UrlKey = key;
			UrlToken = cancellationToken;
			if (UrlError is not null)
				throw UrlError;
			return Task.FromResult(UrlResult);
		}

		public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
		{
			DeletedKeys.Add(key);
			DeleteToken = cancellationToken;
			if (OnDelete is not null)
				await OnDelete();
			cancellationToken.ThrowIfCancellationRequested();
			if (DeleteError is not null)
				throw DeleteError;
		}
	}

	private sealed class TestLogger : ILogger<ReceiptService>
	{
		public List<string> Messages { get; } = [];
		public List<Exception?> Exceptions { get; } = [];
		public List<LogLevel> Levels { get; } = [];
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel logLevel) => true;
		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			Messages.Add(formatter(state, exception));
			Exceptions.Add(exception);
			Levels.Add(logLevel);
		}
	}

	private sealed class NonSeekableStream(string value) : Stream
	{
		private readonly MemoryStream _inner = new(Encoding.UTF8.GetBytes(value));
		public override bool CanRead => _inner.CanRead;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override void Flush() => throw new NotSupportedException();
		public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _inner.ReadAsync(buffer, cancellationToken);
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		protected override void Dispose(bool disposing)
		{
			if (disposing)
				_inner.Dispose();
			base.Dispose(disposing);
		}
	}

	private sealed class FailingCopyStream(Exception error) : MemoryStream
	{
		public string? TemporaryPath { get; private set; }
		public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
		{
			TemporaryPath = Assert.IsType<FileStream>(destination).Name;
			return Task.FromException(error);
		}
	}
}
