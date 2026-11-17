using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using ZPI4M.Banking.Database;
using ZPI4M.Banking.Repositories.BankAccountRepository;
using ZPI4M.Banking.Repositories.BankTransactionAssignmentRepository;
using ZPI4M.Banking.Repositories.BankTransactionRepository;
using ZPI4M.Banking.Services.Assignments;
using ZPI4M.Banking.Services.Reads;
using ZPI4M.Expense.ApiControllers;
using ZPI4M.Expense.Database;
using ZPI4M.Expense.Models;
using ZPI4M.Expense.Repositories.CategoryRepository;
using ZPI4M.Expense.Repositories.ExpenseItemRepository;
using ZPI4M.Expense.Repositories.ExpenseRepository;
using ZPI4M.Expense.Repositories.ExpenseShareRepository;
using ZPI4M.Expense.Services.Expenses;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Services.Aggregates;
using ZPI4M.Grouping.Services.ExpenseMetadata;
using ZPI4M.Grouping.Services.Expenses;
using ZPI4M.Grouping.Services.Members;
using ZPI4M.Grouping.Services.Transactions;
using ZPI4M.Notifications.Database;
using ZPI4M.Notifications.Repositories.NotificationRecipientRepository;
using ZPI4M.Notifications.Repositories.NotificationRepository;
using ZPI4M.Notifications.Services.Notifications;
using ZPI4M.Receipt.Database;
using ZPI4M.Receipt.Repositories.ReceiptRepository;
using ZPI4M.Receipt.Services;
using ZPI4M.Shared.CurrentUser;
using ZPI4M.Shared.Storage;
using ZPI4M.User.Database;
using ZPI4M.User.Services.BankingProfile;
using ZPI4M.User.Services.Lookup;

namespace ZPI4M.Expense.IntegrationTests;

public sealed class ExpenseWorkflowTests : PostgresFixture
{
	private static readonly Guid GroupId = Guid.Parse("20000000-0000-0000-0000-000000000001");
	private static readonly Guid CategoryId = Guid.Parse("30000000-0000-0000-0000-000000000001");
	private static readonly Guid OtherUserId = Guid.Parse("10000000-0000-0000-0000-000000000002");
	private static readonly Guid OutsiderId = Guid.Parse("10000000-0000-0000-0000-000000000003");

	[PostgresTheory]
	[InlineData(null, "70.00", 1)]
	[InlineData("100.00", "100.00", 2)]
	public async Task ManualWorkflowPreviewsWithoutWritesThenCreatesAndReadsFullDetails(string? amount, string expected, int itemCount)
	{
		await SeedAsync();
		await using var host = await StartAsync();
		var body = Body(amount: amount);
		body["items"] = new JsonArray(Item());
		var categories = await host.Client.GetFromJsonAsync<JsonElement>("/api/categories");
		Assert.Contains(categories.EnumerateArray(), category => category.GetProperty("isSystemGenerated").GetBoolean());
		using var preview = await host.Client.PostAsJsonAsync("/api/expenses/preview", body);
		Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
		var calculation = await preview.Content.ReadFromJsonAsync<JsonElement>();
		Assert.Equal(expected, calculation.GetProperty("amount").GetString());
		Assert.False(calculation.TryGetProperty("id", out _));
		Assert.Equal(4L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense"));
		using var create = await host.Client.PostAsJsonAsync("/api/expenses", body);
		Assert.Equal(HttpStatusCode.Created, create.StatusCode);
		var result = await create.Content.ReadFromJsonAsync<JsonElement>();
		Assert.Equal(expected, result.GetProperty("amount").GetString());
		Assert.Equal(itemCount, result.GetProperty("items").GetArrayLength());
		Assert.Equal("Dom", result.GetProperty("groupName").GetString());
		Assert.Equal("Anna Testowa", result.GetProperty("payerDisplayName").GetString());
		Assert.True(result.GetProperty("canEdit").GetBoolean());
		Assert.NotEqual(Guid.Empty, result.GetProperty("version").GetGuid());
		Assert.Equal($"/api/expenses/{result.GetProperty("id").GetGuid():D}", create.Headers.Location!.AbsolutePath);
		var read = await host.Client.GetFromJsonAsync<JsonElement>(create.Headers.Location);
		Assert.Equal(result.GetProperty("version").GetString(), read.GetProperty("version").GetString());
		Assert.Equal(result.GetRawText(), read.GetRawText());
	}

	[PostgresFact]
	public async Task CalendarDatesRoundTripAndHistoryIncludesLegacyTimesWithinTheDay()
	{
		await SeedAsync();
		await using var host = await StartAsync();
		var body = Body();
		body["name"] = "Calendar date test";
		body["transactionTime"] = "2030-03-31";
		using var create = await host.Client.PostAsJsonAsync("/api/expenses", body);
		Assert.Equal(HttpStatusCode.Created, create.StatusCode);
		var created = await create.Content.ReadFromJsonAsync<JsonElement>();
		Assert.Equal("2030-03-31", created.GetProperty("transactionTime").GetString());
		var id = created.GetProperty("id").GetGuid();
		var stored = await ScalarAsync<DateTime>($"SELECT transaction_time FROM expense.expense WHERE _id = '{id:D}'");
		Assert.Equal(new DateTime(2030, 3, 31, 0, 0, 0, DateTimeKind.Utc), stored);
		Assert.Equal(DateTimeKind.Utc, stored.Kind);

		// Older rows retain their stored hour but expose only their UTC calendar date.
		await ExecuteAsync($"UPDATE expense.expense SET transaction_time = '2030-03-31T23:59:59Z' WHERE _id = '{id:D}'");
		var read = await host.Client.GetFromJsonAsync<JsonElement>($"/api/expenses/{id:D}");
		Assert.Equal("2030-03-31", read.GetProperty("transactionTime").GetString());
		var history = await host.Client.GetFromJsonAsync<JsonElement>("/api/expenses?transactionTimeFrom=2030-03-31&transactionTimeTo=2030-04-01");
		Assert.Equal(1, history.GetProperty("totalCount").GetInt32());
		Assert.Equal("2030-03-31", history.GetProperty("items")[0].GetProperty("transactionTime").GetString());
		var excluded = await host.Client.GetFromJsonAsync<JsonElement>("/api/expenses?transactionTimeFrom=2030-03-30&transactionTimeTo=2030-03-31");
		Assert.Equal(0, excluded.GetProperty("totalCount").GetInt32());

		body.Remove("groupId");
		body["version"] = read.GetProperty("version").GetString();
		body["transactionTime"] = "2030-04-01";
		using var update = await host.Client.PutAsJsonAsync($"/api/expenses/{id:D}", body);
		Assert.Equal(HttpStatusCode.OK, update.StatusCode);
		var updated = await update.Content.ReadFromJsonAsync<JsonElement>();
		Assert.Equal("2030-04-01", updated.GetProperty("transactionTime").GetString());
		Assert.Equal(new DateTime(2030, 4, 1, 0, 0, 0, DateTimeKind.Utc),
			await ScalarAsync<DateTime>($"SELECT transaction_time FROM expense.expense WHERE _id = '{id:D}'"));
	}

	[PostgresFact]
	public async Task BankWorkflowListsThenAttachesAndConflictsWithoutChangingBankData()
	{
		await SeedAsync();
		var beforePaid = await ScalarAsync<decimal>($"SELECT total_paid FROM grouping.group_user WHERE group_id = '{GroupId}' AND user_id = '{UserId}'");
		var beforeShare = await ScalarAsync<decimal>($"SELECT total_share FROM grouping.group_user WHERE group_id = '{GroupId}' AND user_id = '{UserId}'");
		var bank = await AddBankAsync(currency: "pln");
		await using var host = await StartAsync();
		var available = await host.Client.GetFromJsonAsync<JsonElement>("/api/expenses/available-bank-transactions");
		Assert.Equal(1, available.GetProperty("totalCount").GetInt32());
		Assert.Equal(bank, available.GetProperty("items")[0].GetProperty("id").GetGuid());
		Assert.Equal("-100.00", available.GetProperty("items")[0].GetProperty("amount").GetString());
		Assert.Equal("PLN", available.GetProperty("items")[0].GetProperty("currencyCode").GetString());
		using var preview = await host.Client.PostAsJsonAsync("/api/expenses/preview", Body(bank));
		Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
		using var create = await host.Client.PostAsJsonAsync("/api/expenses", Body(bank));
		Assert.Equal(HttpStatusCode.Created, create.StatusCode);
		var result = await create.Content.ReadFromJsonAsync<JsonElement>();
		Assert.Equal(bank, result.GetProperty("bankTransactionId").GetGuid());
		Assert.Equal(-100m, await ScalarAsync<decimal>($"SELECT amount FROM banking.bank_transaction WHERE _id = '{bank:D}'"));
		var after = await host.Client.GetFromJsonAsync<JsonElement>("/api/expenses/available-bank-transactions");
		Assert.Equal(0, after.GetProperty("totalCount").GetInt32());
		using var repeat = await host.Client.PostAsJsonAsync("/api/expenses", Body(bank));
		Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
		Assert.Equal(5L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense"));
		Assert.Equal(beforePaid + 100, await ScalarAsync<decimal>($"SELECT total_paid FROM grouping.group_user WHERE group_id = '{GroupId}' AND user_id = '{UserId}'"));
		Assert.Equal(beforeShare + 100, await ScalarAsync<decimal>($"SELECT total_share FROM grouping.group_user WHERE group_id = '{GroupId}' AND user_id = '{UserId}'"));
	}

	[PostgresTheory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task AnalysisAndSavePreserveTheOriginalOcrWithoutRunningItAgain(bool withBank)
	{
		await SeedAsync();
		var storage = new ReceiptTestDependencies();
		await using var host = await StartAsync(receiptDependencies: storage);
		using var analysisResponse = await AnalyzeReceiptAsync(host.Client);
		Assert.Equal(HttpStatusCode.OK, analysisResponse.StatusCode);
		var analysis = await analysisResponse.Content.ReadFromJsonAsync<JsonElement>();
		Assert.Equal("Synthetic shop", analysis.GetProperty("proposal").GetProperty("name").GetString());
		var proposal = analysis.GetProperty("proposal");
		Assert.Equal("2026-11-02", proposal.GetProperty("transactionTime").GetString());
		Assert.False(proposal.TryGetProperty("transactionDate", out _));
		var item = proposal.GetProperty("items")[0];
		Assert.True(item.GetProperty("needsReview").GetBoolean());
		Assert.Equal("ambiguous_category", item.GetProperty("reviewReason").GetString());
		var warning = Assert.Single(analysis.GetProperty("warnings").EnumerateArray());
		Assert.Equal("review", warning.GetProperty("code").GetString());
		Assert.Equal("Check the item", warning.GetProperty("message").GetString());
		Assert.Equal("items[0]", warning.GetProperty("field").GetString());
		var proof = analysis.GetProperty("ocrProof").GetString()!;
		Assert.Empty(await storage.ListKeysAsync("receipts/"));
		Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM receipt.receipt"));

		var body = Body(withBank ? await AddBankAsync() : null);
		body["shares"] = new JsonArray(
			new JsonObject { ["userId"] = UserId.ToString(), ["percentage"] = "50.00" },
			new JsonObject { ["userId"] = OtherUserId.ToString(), ["percentage"] = "50.00" });
		body["name"] = "Corrected purchase";
		using var create = await SaveWithReceiptAsync(host.Client, HttpMethod.Post, "/api/expenses", body, proof);
		Assert.Equal(HttpStatusCode.Created, create.StatusCode);
		var expense = await create.Content.ReadFromJsonAsync<JsonElement>();
		Assert.Equal("Corrected purchase", expense.GetProperty("name").GetString());
		var receiptPath = create.Headers.Location!.OriginalString + "/receipt";
		var receipt = await host.Client.GetFromJsonAsync<JsonElement>(receiptPath);
		Assert.Equal(expense.GetProperty("receiptId").GetGuid(), receipt.GetProperty("id").GetGuid());
		var savedProposal = receipt.GetProperty("analysis").GetProperty("proposal");
		Assert.Equal("2026-11-02", savedProposal.GetProperty("transactionTime").GetString());
		Assert.False(savedProposal.TryGetProperty("transactionDate", out _));
		var savedItem = savedProposal.GetProperty("items")[0];
		Assert.True(savedItem.GetProperty("needsReview").GetBoolean());
		Assert.Equal("ambiguous_category", savedItem.GetProperty("reviewReason").GetString());
		var savedWarning = Assert.Single(receipt.GetProperty("analysis").GetProperty("warnings").EnumerateArray());
		Assert.Equal("review", savedWarning.GetProperty("code").GetString());
		Assert.Equal("Check the item", savedWarning.GetProperty("message").GetString());
		Assert.Equal("items[0]", savedWarning.GetProperty("field").GetString());
		Assert.Equal("Synthetic shop", receipt.GetProperty("analysis").GetProperty("rawResult").GetProperty("receipt").GetProperty("store").GetString());
		Assert.Equal(1, storage.AnalysisCalls);
		Assert.Single(await storage.ListKeysAsync("receipts/"));

		host.Authenticate(OtherUserId);
		using var member = await host.Client.GetAsync(receiptPath);
		Assert.Equal(HttpStatusCode.OK, member.StatusCode);
		await ExecuteAsync($"UPDATE grouping.group_user SET is_member = false WHERE group_id = '{GroupId:D}' AND user_id = '{OtherUserId:D}'");
		using var former = await host.Client.GetAsync(receiptPath);
		Assert.Equal(HttpStatusCode.OK, former.StatusCode);
		host.Authenticate(OutsiderId);
		using var outsider = await host.Client.GetAsync(receiptPath);
		Assert.Equal(HttpStatusCode.NotFound, outsider.StatusCode);
	}

	[PostgresFact]
	public async Task OcrDiscountsAndDepositAdjustmentsReconcileWhenSaved()
	{
		await SeedAsync();
		var storage = new ReceiptTestDependencies
		{
			AnalysisJson = """
				{
				  "receipt": {
				    "amount_due": "17.50",
				    "items": [
				      {"name":"Product","category":"żywność i napoje","unit_price":"10.00","quantity":"2","discount":"-0.50","total":"19.50"},
				      {"name":"Receipt-wide discount","category":"zniżka","total":"-3.20"},
				      {"name":"Opłata za plastik","category":"rachunki i opłaty","unit_price":"0.60","quantity":"1","total":"0.60"}
				    ],
				    "deposits": [
				      {"name":"Opłata kaucyjna","unit_price":"0.50","quantity":"2","total":"1.00"},
				      {"name":"Opakowanie","total":"0.40"}
				    ],
				    "deposit_returns": [{"name":"Zwrot butelek","total":"-0.80"}]
				  },
				  "warnings": [],
				  "needs_review": false
				}
				"""
		};
		await using var host = await StartAsync(receiptDependencies: storage);
		using var response = await AnalyzeReceiptAsync(host.Client);
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		var analysis = await response.Content.ReadFromJsonAsync<JsonElement>();
		var proposal = analysis.GetProperty("proposal");
		var items = proposal.GetProperty("items").EnumerateArray().ToArray();
		Assert.Equal(6, items.Length);
		Assert.Equal("-0.50", items[0].GetProperty("discount").GetString());
		Assert.Equal("-3.20", items[1].GetProperty("unitPrice").GetString());
		Assert.Equal("1", items[1].GetProperty("quantity").GetString());
		Assert.Equal(ExpenseDefaultCategories.BillsAndFees.Id, items[2].GetProperty("categoryId").GetGuid());
		Assert.Equal("2", items[3].GetProperty("quantity").GetString());
		Assert.Equal(ExpenseDefaultCategories.Deposit.Id, items[3].GetProperty("categoryId").GetGuid());
		Assert.Equal(ExpenseDefaultCategories.Deposit.Id, items[4].GetProperty("categoryId").GetGuid());
		Assert.False(items[4].GetProperty("needsReview").GetBoolean());
		Assert.False(analysis.GetProperty("needsReview").GetBoolean());
		Assert.Equal("-0.80", items[5].GetProperty("unitPrice").GetString());
		Assert.Equal(ExpenseDefaultCategories.Deposit.Id, items[5].GetProperty("categoryId").GetGuid());

		var body = Body(amount: proposal.GetProperty("amount").GetString());
		body["items"] = new JsonArray(items.Select(item => (JsonNode)new JsonObject
		{
			["name"] = item.GetProperty("name").GetString(),
			["categoryId"] = item.GetProperty("categoryId").GetString(),
			["unitPrice"] = item.GetProperty("unitPrice").GetString(),
			["quantity"] = item.GetProperty("quantity").GetString(),
			["discount"] = item.GetProperty("discount").GetString() ?? "0.00"
		}).ToArray());
		using var save = await SaveWithReceiptAsync(host.Client, HttpMethod.Post, "/api/expenses", body,
			analysis.GetProperty("ocrProof").GetString());
		Assert.Equal(HttpStatusCode.Created, save.StatusCode);
		var expense = await save.Content.ReadFromJsonAsync<JsonElement>();
		Assert.Equal("17.50", expense.GetProperty("amount").GetString());
		var savedItems = expense.GetProperty("items").EnumerateArray().ToArray();
		Assert.Equal(6, savedItems.Length);
		Assert.All(savedItems, item => Assert.False(item.GetProperty("isSystemGenerated").GetBoolean()));
	}

	[PostgresTheory]
	[InlineData("modified")]
	[InlineData("other-file")]
	[InlineData("other-user")]
	public async Task InvalidProofDoesNotCreateAReceipt(string invalid)
	{
		await SeedAsync();
		var storage = new ReceiptTestDependencies();
		await using var host = await StartAsync(receiptDependencies: storage);
		using var response = await AnalyzeReceiptAsync(host.Client);
		var proof = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ocrProof").GetString()!;
		if (invalid == "modified") proof = (proof[0] == 'a' ? "b" : "a") + proof[1..];
		var body = Body();
		if (invalid == "other-user")
		{
			host.Authenticate(OtherUserId);
			body["payerId"] = OtherUserId.ToString();
		}
		using var save = await SaveWithReceiptAsync(host.Client, HttpMethod.Post, "/api/expenses", body, proof, invalid == "other-file");
		Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
		Assert.Empty(await storage.ListKeysAsync("receipts/"));
		Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM receipt.receipt"));
	}

	[PostgresFact]
	public async Task UploadRejectsUnsupportedMimeAndMultipleFilesWithoutCreatingReceipts()
	{
		await SeedAsync();
		await using var host = await StartAsync();
		var cases = new (string ContentType, int FileCount, HttpStatusCode Status)[]
		{
			("text/plain", 1, HttpStatusCode.UnsupportedMediaType),
			("image/png", 2, HttpStatusCode.BadRequest)
		};
		foreach (var (contentType, fileCount, expected) in cases)
		{
			using var multipart = new MultipartFormDataContent();
			for (var index = 0; index < fileCount; index++)
			{
				var file = new ByteArrayContent([1, 2, 3]);
				file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
				multipart.Add(file, "file", $"receipt-{index}.png");
			}
			using var response = await host.Client.PostAsync("/api/expenses/receipts/analyze", multipart);
			Assert.Equal(expected, response.StatusCode);
		}
		Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM receipt.receipt"));
	}

	[PostgresTheory]
	[InlineData(null, null, 422)]
	[InlineData(ReceiptAnalysisErrorKind.HttpError, HttpStatusCode.UnprocessableEntity, 422)]
	[InlineData(ReceiptAnalysisErrorKind.InvalidResponse, null, 502)]
	[InlineData(ReceiptAnalysisErrorKind.Timeout, null, 504)]
	public async Task OcrFailuresDoNotCreateReceipts(ReceiptAnalysisErrorKind? error, HttpStatusCode? workerStatus, int expected)
	{
		await SeedAsync();
		var receiptDependencies = new ReceiptTestDependencies { AnalysisError = error, AnalysisStatus = workerStatus, EmptyAnalysis = error is null };
		await using var host = await StartAsync(receiptDependencies: receiptDependencies);
		using var analysis = await AnalyzeReceiptAsync(host.Client);
		Assert.Equal(expected, (int)analysis.StatusCode);
		Assert.Empty(await receiptDependencies.ListKeysAsync("receipts/"));
		Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM receipt.receipt"));
		Assert.Equal(4L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense"));
	}

	[PostgresTheory]
	[InlineData(100, "Booked", "PLN", false, 400)]
	[InlineData(0, "Booked", "PLN", false, 400)]
	[InlineData(-100, "Pending", "PLN", false, 400)]
	[InlineData(-100, "Booked", "EUR", false, 400)]
	[InlineData(-100, "Booked", "PLN", true, 404)]
	public async Task RejectsIneligibleBankSources(int amount, string status, string currency, bool foreign, int expected)
	{
		await SeedAsync();
		var bank = await AddBankAsync(amount, status, currency, foreign);
		await using var host = await StartAsync();
		using var response = await host.Client.PostAsJsonAsync("/api/expenses", Body(bank));
		Assert.Equal(expected, (int)response.StatusCode);
		Assert.Equal(4L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense"));
	}

	[PostgresTheory]
	[InlineData("amount")]
	[InlineData("payerId")]
	public async Task BankMismatchReturnsSpecificFieldError(string field)
	{
		await SeedAsync();
		var bank = await AddBankAsync();
		await using var host = await StartAsync();
		var body = Body(bank);
		if (field == "amount") body[field] = "99.00";
		else body[field] = OtherUserId.ToString();
		using var response = await host.Client.PostAsJsonAsync("/api/expenses", body);
		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
		Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
	}

	[PostgresFact]
	public async Task PaginationExcludesAssignedAndIneligibleRowsBeforeCountAndPage()
	{
		await SeedAsync();
		for (var index = 0; index < 5; index++) await AddBankAsync(-10 - index);
		var reservedForSettlement = await AddBankAsync(-30);
		await ExecuteAsync($"INSERT INTO banking.bank_transaction_assignment VALUES ('{reservedForSettlement:D}', 2, '{Guid.NewGuid():D}')");
		await AddBankAsync(100);
		await AddBankAsync(-100, "Pending");
		await AddBankAsync(-100, currency: "EUR");
		await AddBankAsync(-100, foreign: true);
		await using var host = await StartAsync();
		var seen = new HashSet<Guid>();
		for (var page = 1; page <= 3; page++)
		{
			var result = await host.Client.GetFromJsonAsync<JsonElement>($"/api/expenses/available-bank-transactions?pageSize=2&pageNumber={page}");
			Assert.Equal(5, result.GetProperty("totalCount").GetInt32());
			Assert.Equal(3, result.GetProperty("totalPages").GetInt32());
			Assert.Equal(page < 3, result.GetProperty("hasNextPage").GetBoolean());
			foreach (var item in result.GetProperty("items").EnumerateArray()) Assert.True(seen.Add(item.GetProperty("id").GetGuid()));
		}
		Assert.Equal(5, seen.Count);
		var empty = await host.Client.GetFromJsonAsync<JsonElement>("/api/expenses/available-bank-transactions?pageSize=2&pageNumber=4");
		Assert.Equal(0, empty.GetProperty("items").GetArrayLength());
		Assert.Equal(5, empty.GetProperty("totalCount").GetInt32());
	}

	[PostgresFact]
	public async Task ConcurrentBankAttachmentHasExactlyOneWinnerAndNoOrphans()
	{
		await SeedAsync();
		var beforePaid = await ScalarAsync<decimal>($"SELECT total_paid FROM grouping.group_user WHERE group_id = '{GroupId}' AND user_id = '{UserId}'");
		var bank = await AddBankAsync();
		var body = Body(bank);
		await using var host = await StartAsync(names: new PrepareBarrier());
		var responses = await Task.WhenAll(host.Client.PostAsJsonAsync("/api/expenses", body),
			host.Client.PostAsJsonAsync("/api/expenses", body));
		try
		{
			Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
			var bodies = await Task.WhenAll(responses.Select(response => response.Content.ReadAsStringAsync()));
			Assert.True(responses.Count(response => response.StatusCode == HttpStatusCode.Conflict) == 1, string.Join("\n", bodies));
			Assert.Equal(5L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense"));
			Assert.Equal(8L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense_item"));
			Assert.Equal(9L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense_share"));
			var consumer = await ScalarAsync<Guid>($"SELECT consumer_id FROM banking.bank_transaction_assignment WHERE bank_transaction_id = '{bank:D}' AND consumer_type = 1");
			Assert.Equal(consumer, await ScalarAsync<Guid>($"SELECT _id FROM expense.expense WHERE fk_bank_transaction_id = '{bank:D}'"));
			Assert.Equal(beforePaid + 100, await ScalarAsync<decimal>($"SELECT total_paid FROM grouping.group_user WHERE group_id = '{GroupId}' AND user_id = '{UserId}'"));
		}
		finally { foreach (var response in responses) response.Dispose(); }
	}

	[PostgresTheory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task DatabaseFailuresRollbackAndAreNotMisreportedAsSourceConflict(bool unrelatedUnique)
	{
		await SeedAsync();
		var beforePaid = await ScalarAsync<decimal>($"SELECT total_paid FROM grouping.group_user WHERE group_id = '{GroupId}' AND user_id = '{UserId}'");
		var beforeShare = await ScalarAsync<decimal>($"SELECT total_share FROM grouping.group_user WHERE group_id = '{GroupId}' AND user_id = '{UserId}'");
		var storage = new ReceiptTestDependencies();
		await using var host = await StartAsync(interceptor: unrelatedUnique ? new DuplicateCategory() : new FailAfterSave(), receiptDependencies: storage);
		var bank = await AddBankAsync();
		using var response = await SaveWithReceiptAsync(host.Client, HttpMethod.Post, "/api/expenses", Body(bank));
		Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
		Assert.Equal(0L, await AssignmentCountAsync(bank));
		Assert.Equal(4L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense"));
		Assert.Equal(7L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense_item"));
		Assert.Equal(8L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense_share"));
		Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM receipt.receipt"));
		Assert.Empty(await storage.ListKeysAsync("receipts/"));
		Assert.Equal(beforePaid, await ScalarAsync<decimal>($"SELECT total_paid FROM grouping.group_user WHERE group_id = '{GroupId}' AND user_id = '{UserId}'"));
		Assert.Equal(beforeShare, await ScalarAsync<decimal>($"SELECT total_share FROM grouping.group_user WHERE group_id = '{GroupId}' AND user_id = '{UserId}'"));
	}

	[PostgresTheory]
	[InlineData("EUR", false, 400)]
	[InlineData("PLN", true, 404)]
	public async Task GroupMetadataBlocksUnsupportedGroups(string currency, bool archived, int expected)
	{
		await SeedAsync();
		await using var host = await StartAsync(metadata: new FixedMetadata(currency, archived));
		using var response = await host.Client.PostAsJsonAsync("/api/expenses", Body());
		Assert.Equal(expected, (int)response.StatusCode);
		Assert.Equal(4L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense"));
	}

	[PostgresFact]
	public async Task RealGroupingMetadataIsUsedWithoutDevelopmentSwitch()
	{
		await SeedAsync();
		await ExecuteAsync("UPDATE grouping.\"group\" SET currency_code = 'EUR'");
		await using var host = await StartAsync();
		using var response = await host.Client.PostAsJsonAsync("/api/expenses", Body());
		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		Assert.Equal(4L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense"));
	}

	[PostgresFact]
	public async Task OutsidersCannotWriteOrReadAndFormerParticipantsKeepLabelsWithoutWritePermission()
	{
		await SeedAsync();
		await using var host = await StartAsync();
		host.Authenticate(OutsiderId);
		using var create = await host.Client.PostAsJsonAsync("/api/expenses", Body());
		Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
		using var read = await host.Client.GetAsync($"/api/expenses/{ExpenseId:D}");
		Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
		await ExecuteAsync($"DELETE FROM grouping.group_user WHERE group_id = '{GroupId:D}' AND user_id = '{OtherUserId:D}'");
		await ExecuteAsync($"UPDATE \"user\".\"user\" SET is_active = false WHERE _id = '{UserId:D}'");
		host.Authenticate(OtherUserId);
		var former = await host.Client.GetFromJsonAsync<JsonElement>($"/api/expenses/{ExpenseId:D}");
		Assert.False(former.GetProperty("canEdit").GetBoolean());
		Assert.False(former.GetProperty("canDelete").GetBoolean());
		Assert.Equal("Dom", former.GetProperty("groupName").GetString());
		Assert.Equal("Anna Testowa", former.GetProperty("payerDisplayName").GetString());
	}

	[PostgresFact]
	public async Task RetiredReceiptIdIsRejectedAndEditPreviewRequiresVersion()
	{
		await SeedAsync();
		await using var host = await StartAsync();
		var receipt = Body(); receipt["receiptId"] = Guid.NewGuid().ToString();
		using var create = await host.Client.PostAsJsonAsync("/api/expenses", receipt);
		Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
		using var missingReceiptPreview = await host.Client.PostAsJsonAsync("/api/expenses/preview", receipt);
		Assert.Equal(HttpStatusCode.BadRequest, missingReceiptPreview.StatusCode);
		var edit = Body(); edit["expenseId"] = ExpenseId.ToString();
		using var preview = await host.Client.PostAsJsonAsync("/api/expenses/preview", edit);
		Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
		var invalid = Body(); var item = Item(); item["id"] = Guid.NewGuid().ToString(); invalid["items"] = new JsonArray(item);
		using var itemResponse = await host.Client.PostAsJsonAsync("/api/expenses", invalid);
		Assert.Equal(HttpStatusCode.BadRequest, itemResponse.StatusCode);
		var problem = await itemResponse.Content.ReadFromJsonAsync<JsonElement>();
		Assert.True(problem.GetProperty("errors").TryGetProperty("items[0].id", out _));
		Assert.Equal(4L, await ScalarAsync<long>("SELECT count(*) FROM expense.expense"));
	}

	[PostgresFact]
	public async Task HistoryFiltersVisibleExpensesBeforePagination()
	{
		await SeedAsync();
		await using var host = await StartAsync();
		for (var index = 0; index < 2; index++)
		{
			var body = Body();
			body["name"] = "History 100% match";
			using var create = await host.Client.PostAsJsonAsync("/api/expenses", body);
			Assert.Equal(HttpStatusCode.Created, create.StatusCode);
		}

		var path = $"/api/expenses?groupId={GroupId:D}&search=100%25&pageSize=1";
		var first = await host.Client.GetFromJsonAsync<JsonElement>(path);
		var second = await host.Client.GetFromJsonAsync<JsonElement>(path + "&pageNumber=2");
		Assert.Equal(2, first.GetProperty("totalCount").GetInt32());
		Assert.Equal("100.00", first.GetProperty("items")[0].GetProperty("myShareAmount").GetString());
		Assert.NotEqual(first.GetProperty("items")[0].GetProperty("id").GetGuid(),
			second.GetProperty("items")[0].GetProperty("id").GetGuid());

		host.Authenticate(OutsiderId);
		var hidden = await host.Client.GetFromJsonAsync<JsonElement>($"/api/expenses?participantId={UserId:D}&search=100%25");
		Assert.Equal(0, hidden.GetProperty("totalCount").GetInt32());
		await ExecuteAsync($"UPDATE grouping.\"group\" SET is_archived = true WHERE id = '{GroupId:D}'");
		host.Authenticate(OtherUserId);
		var archived = await host.Client.GetFromJsonAsync<JsonElement>($"/api/expenses?participantId={UserId:D}&search=100%25");
		Assert.Equal(0, archived.GetProperty("totalCount").GetInt32());
	}

	[PostgresFact]
	public async Task ReplacingAndDeletingBankLinkUpdatesTheBankingRegistry()
	{
		await SeedAsync();
		var originalBank = await AddBankAsync();
		var replacementBank = await AddBankAsync();
		await using var host = await StartAsync();

		var body = Body(originalBank);
		using var create = await host.Client.PostAsJsonAsync("/api/expenses", body);
		Assert.Equal(HttpStatusCode.Created, create.StatusCode);
		var created = await create.Content.ReadFromJsonAsync<JsonElement>();
		var id = created.GetProperty("id").GetGuid();
		var url = $"/api/expenses/{id:D}";
		Assert.Equal(1L, await AssignmentCountAsync(originalBank));

		body.Remove("groupId");
		body["version"] = created.GetProperty("version").GetString();
		body["bankTransactionId"] = replacementBank.ToString();
		using var replace = await host.Client.PutAsJsonAsync(url, body);
		Assert.Equal(HttpStatusCode.OK, replace.StatusCode);
		var updated = await replace.Content.ReadFromJsonAsync<JsonElement>();
		Assert.Equal(replacementBank, updated.GetProperty("bankTransactionId").GetGuid());
		Assert.Equal(0L, await AssignmentCountAsync(originalBank));
		Assert.Equal(id, await ScalarAsync<Guid>($"SELECT consumer_id FROM banking.bank_transaction_assignment WHERE bank_transaction_id = '{replacementBank:D}' AND consumer_type = 1"));

		var version = updated.GetProperty("version").GetString();
		using var delete = await host.Client.DeleteAsync(url + "?version=" + version);
		Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
		Assert.Equal(0L, await AssignmentCountAsync(replacementBank));
	}

	[PostgresFact]
	public async Task EditingPreservesItemIdentityAndBankAndDeleteReversesTheContribution()
	{
		await SeedAsync();
		var bank = await AddBankAsync();
		var paidQuery = $"SELECT total_paid FROM grouping.group_user WHERE group_id = '{GroupId}' AND user_id = '{UserId}'";
		var baseline = await ScalarAsync<decimal>(paidQuery);
		await using var host = await StartAsync();
		var body = Body(bank);
		body["items"] = new JsonArray(Item());
		using var create = await host.Client.PostAsJsonAsync("/api/expenses", body);
		Assert.Equal(HttpStatusCode.Created, create.StatusCode);
		var original = await create.Content.ReadFromJsonAsync<JsonElement>();
		var itemId = original.GetProperty("items").EnumerateArray().Single(item => !item.GetProperty("isSystemGenerated").GetBoolean())
			.GetProperty("id").GetGuid();
		var expenseId = original.GetProperty("id").GetGuid();
		var notificationId = await ScalarAsync<Guid>($"SELECT _id FROM notifications.notification WHERE fk_expense_id = '{expenseId}'");
		var notificationCount = await ScalarAsync<long>("SELECT count(*) FROM notifications.notification");
		var version = original.GetProperty("version").GetString();
		var url = $"/api/expenses/{expenseId:D}";
		body["items"]![0]!["id"] = itemId.ToString();
		body["items"]![0]!["unitPrice"] = "80.00";
		body["expenseId"] = expenseId.ToString();
		body["version"] = version;

		await ExecuteAsync($"UPDATE grouping.group_user SET is_member = false WHERE group_id = '{GroupId:D}' AND user_id = '{UserId:D}'");
		using var former = await host.Client.PostAsJsonAsync("/api/expenses/preview", body);
		Assert.Equal(HttpStatusCode.Forbidden, former.StatusCode);

		host.Authenticate(OtherUserId);
		using var preview = await host.Client.PostAsJsonAsync("/api/expenses/preview", body);
		Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
		var previewValue = await preview.Content.ReadFromJsonAsync<JsonElement>();
		var unchanged = await host.Client.GetFromJsonAsync<JsonElement>(url);
		Assert.Equal(version, unchanged.GetProperty("version").GetString());

		body.Remove("expenseId");
		body.Remove("groupId");
		using var update = await host.Client.PutAsJsonAsync(url, body);
		Assert.Equal(HttpStatusCode.OK, update.StatusCode);
		var updated = await update.Content.ReadFromJsonAsync<JsonElement>();
		Assert.Equal(previewValue.GetProperty("amount").GetString(), updated.GetProperty("amount").GetString());
		Assert.Contains(updated.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == itemId);
		Assert.Equal(bank, updated.GetProperty("bankTransactionId").GetGuid());
		Assert.Equal(baseline + 100m, await ScalarAsync<decimal>(paidQuery));

		using var stale = await host.Client.DeleteAsync(url + "?version=" + version);
		Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
		body["version"] = updated.GetProperty("version").GetString();
		body["bankTransactionId"] = null;
		body["amount"] = "90.00";
		using var detach = await host.Client.PutAsJsonAsync(url, body);
		Assert.Equal(HttpStatusCode.OK, detach.StatusCode);
		var detached = await detach.Content.ReadFromJsonAsync<JsonElement>();
		Assert.Equal(0L, await AssignmentCountAsync(bank));
		Assert.Equal(baseline + 90m, await ScalarAsync<decimal>(paidQuery));

		using var delete = await host.Client.DeleteAsync(url + "?version=" + detached.GetProperty("version").GetString());
		Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
		Assert.Equal(baseline, await ScalarAsync<decimal>(paidQuery));
		Assert.Equal(-100m, await ScalarAsync<decimal>($"SELECT amount FROM banking.bank_transaction WHERE _id = '{bank:D}'"));
		Assert.Equal(notificationCount, await ScalarAsync<long>("SELECT count(*) FROM notifications.notification"));
		Assert.True(await ScalarAsync<bool>($"SELECT fk_expense_id IS NULL FROM notifications.notification WHERE _id = '{notificationId}'"));
	}

	[PostgresFact]
	public async Task NotificationFailureRollsBackReceiptExpenseAndBalancesAndCleansUpload()
	{
		await SeedAsync();
		var expenseCount = await ScalarAsync<long>("SELECT count(*) FROM expense.expense");
		var receiptCount = await ScalarAsync<long>("SELECT count(*) FROM receipt.receipt");
		var notificationCount = await ScalarAsync<long>("SELECT count(*) FROM notifications.notification");
		var recipientCount = await ScalarAsync<long>("SELECT count(*) FROM notifications.notification_recipient");
		var balanceQuery = $"SELECT total_paid FROM grouping.group_user WHERE group_id = '{GroupId}' AND user_id = '{UserId}'";
		var paid = await ScalarAsync<decimal>(balanceQuery);
		var storage = new ReceiptTestDependencies();
		await using var host = await StartAsync(receiptDependencies: storage, notificationInterceptor: new FailAfterSave());
		using var response = await SaveWithReceiptAsync(host.Client, HttpMethod.Post, "/api/expenses", Body());
		Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
		Assert.Equal(expenseCount, await ScalarAsync<long>("SELECT count(*) FROM expense.expense"));
		Assert.Equal(receiptCount, await ScalarAsync<long>("SELECT count(*) FROM receipt.receipt"));
		Assert.Equal(notificationCount, await ScalarAsync<long>("SELECT count(*) FROM notifications.notification"));
		Assert.Equal(recipientCount, await ScalarAsync<long>("SELECT count(*) FROM notifications.notification_recipient"));
		Assert.Equal(paid, await ScalarAsync<decimal>(balanceQuery));
		Assert.Empty(await storage.ListKeysAsync("receipts/"));
	}

	[PostgresFact]
	public async Task ReceiptReadDoesNotLoadProductsOrFullShares()
	{
		await SeedAsync();
		var storage = new ReceiptTestDependencies();
		await using var host = await StartAsync(receiptDependencies: storage);
		using var create = await SaveWithReceiptAsync(host.Client, HttpMethod.Post, "/api/expenses", Body());
		Assert.Equal(HttpStatusCode.Created, create.StatusCode);
		await using var reader = await StartAsync(interceptor: new RejectExpenseDetailReads(), receiptDependencies: storage);
		reader.Authenticate(OtherUserId);
		using var response = await reader.Client.GetAsync(create.Headers.Location!.OriginalString + "/receipt");
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}

	[PostgresTheory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ReceiptUploadDoesNotHoldTheGroupLock(bool update)
	{
		await SeedAsync();
		var uploaded = false;
		var storage = new ReceiptTestDependencies
		{
			BeforeUploadAsync = async () =>
			{
				await using var connection = new NpgsqlConnection(ConnectionString);
				await connection.OpenAsync();
				await using var transaction = await connection.BeginTransactionAsync();
				await using var command = new NpgsqlCommand(
					$"""SELECT id FROM grouping."group" WHERE id = '{GroupId:D}' FOR UPDATE NOWAIT""", connection, transaction);
				await command.ExecuteScalarAsync();
				uploaded = true;
			}
		};
		await using var host = await StartAsync(receiptDependencies: storage);
		var body = Body();
		var path = "/api/expenses";
		if (update)
		{
			using var create = await host.Client.PostAsJsonAsync(path, body);
			Assert.Equal(HttpStatusCode.Created, create.StatusCode);
			var existing = await create.Content.ReadFromJsonAsync<JsonElement>();
			path = create.Headers.Location!.OriginalString;
			body.Remove("groupId");
			body["version"] = existing.GetProperty("version").GetString();
		}
		using var response = await SaveWithReceiptAsync(host.Client, update ? HttpMethod.Put : HttpMethod.Post, path, body);
		Assert.Equal(update ? HttpStatusCode.OK : HttpStatusCode.Created, response.StatusCode);
		Assert.True(uploaded);
	}

	[PostgresFact]
	public async Task ReplacementAndRemovalDeleteTheOldReceiptOnlyAfterSuccessfulSave()
	{
		await SeedAsync();
		var storage = new ReceiptTestDependencies();
		await using var host = await StartAsync(receiptDependencies: storage);
		using var create = await SaveWithReceiptAsync(host.Client, HttpMethod.Post, "/api/expenses", Body());
		Assert.Equal(HttpStatusCode.Created, create.StatusCode);
		var created = await create.Content.ReadFromJsonAsync<JsonElement>();
		var path = create.Headers.Location!.OriginalString;
		var first = created.GetProperty("receiptId").GetGuid();
		var original = await host.Client.GetFromJsonAsync<JsonElement>(path + "/receipt");
		Assert.Equal(JsonValueKind.Null, original.GetProperty("analysis").ValueKind);

		var edit = Body();
		edit.Remove("groupId");
		edit["version"] = created.GetProperty("version").GetString();
		await using var failing = await StartAsync(interceptor: new FailAfterSave(), receiptDependencies: storage);
		using var failure = await SaveWithReceiptAsync(failing.Client, HttpMethod.Put, path, edit);
		Assert.Equal(HttpStatusCode.ServiceUnavailable, failure.StatusCode);
		Assert.Single(await storage.ListKeysAsync("receipts/"));
		Assert.Equal(first, (await host.Client.GetFromJsonAsync<JsonElement>(path + "/receipt")).GetProperty("id").GetGuid());

		edit["removeReceipt"] = true;
		using var contradictory = await SaveWithReceiptAsync(host.Client, HttpMethod.Put, path, edit);
		Assert.Equal(HttpStatusCode.BadRequest, contradictory.StatusCode);
		edit.Remove("removeReceipt");
		using var replace = await SaveWithReceiptAsync(host.Client, HttpMethod.Put, path, edit);
		Assert.Equal(HttpStatusCode.OK, replace.StatusCode);
		var replaced = await replace.Content.ReadFromJsonAsync<JsonElement>();
		Assert.NotEqual(first, replaced.GetProperty("receiptId").GetGuid());
		Assert.Single(await storage.ListKeysAsync("receipts/"));
		Assert.Equal(0L, await ScalarAsync<long>($"SELECT count(*) FROM receipt.receipt WHERE _id = '{first:D}'"));

		edit["version"] = replaced.GetProperty("version").GetString();
		edit["removeReceipt"] = true;
		using var remove = await host.Client.PutAsJsonAsync(path, edit);
		Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
		using var missing = await host.Client.GetAsync(path + "/receipt");
		Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
		Assert.Empty(await storage.ListKeysAsync("receipts/"));
		Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM receipt.receipt"));
	}

	[PostgresFact]
	public async Task ReceiptCleanupFollowsSuccessfulExpenseDelete()
	{
		await SeedAsync();
		var storage = new ReceiptTestDependencies();
		await using var host = await StartAsync(receiptDependencies: storage);
		using var create = await SaveWithReceiptAsync(host.Client, HttpMethod.Post, "/api/expenses", Body());
		Assert.Equal(HttpStatusCode.Created, create.StatusCode);
		var created = await create.Content.ReadFromJsonAsync<JsonElement>();
		var url = create.Headers.Location!.OriginalString;
		using var rejected = await host.Client.DeleteAsync(url + "?version=stale");
		Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
		Assert.Single(await storage.ListKeysAsync("receipts/"));
		await using var withoutLabels = await StartAsync(names: new MissingDisplayNames(), receiptDependencies: storage);
		using var deleted = await withoutLabels.Client.DeleteAsync(url + "?version=" + created.GetProperty("version").GetString());
		Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
		Assert.Empty(await storage.ListKeysAsync("receipts/"));
		using var missing = await host.Client.GetAsync(url + "/receipt");
		Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
	}

	[PostgresFact]
	public async Task FailedDeleteRollsBackExpenseAndBalancesAndPreservesReceipt()
	{
		await SeedAsync();
		var storage = new ReceiptTestDependencies();
		await using var host = await StartAsync(receiptDependencies: storage);
		using var create = await SaveWithReceiptAsync(host.Client, HttpMethod.Post, "/api/expenses", Body());
		Assert.Equal(HttpStatusCode.Created, create.StatusCode);
		var created = await create.Content.ReadFromJsonAsync<JsonElement>();
		var url = create.Headers.Location!.OriginalString;
		var receipt = created.GetProperty("receiptId").GetGuid();
		var paidQuery = $"SELECT total_paid FROM grouping.group_user WHERE group_id = '{GroupId}' AND user_id = '{UserId}'";
		var before = await ScalarAsync<decimal>(paidQuery);

		await using var failing = await StartAsync(interceptor: new FailAfterSave(), receiptDependencies: storage);
		using var delete = await failing.Client.DeleteAsync(url + "?version=" + created.GetProperty("version").GetString());
		Assert.Equal(HttpStatusCode.ServiceUnavailable, delete.StatusCode);
		Assert.Equal(before, await ScalarAsync<decimal>(paidQuery));
		var read = await host.Client.GetFromJsonAsync<JsonElement>(url);
		Assert.Equal(receipt, read.GetProperty("receiptId").GetGuid());
		Assert.Single(await storage.ListKeysAsync("receipts/"));
	}

	[PostgresFact]
	public async Task SeededExpensesHaveDistinctNonemptyVersionsAfterSquashedMigration()
	{
		await SeedAsync();
		Assert.Equal(4L, await ScalarAsync<long>("SELECT count(DISTINCT version) FROM expense.expense WHERE version <> '00000000-0000-0000-0000-000000000000'"));
		await using var context = OpenDatabase();
		Assert.False(context.Database.HasPendingModelChanges());
	}

	private static JsonObject Body(Guid? bank = null, string? amount = "100.00") => new()
	{
		["groupId"] = GroupId.ToString(),
		["payerId"] = UserId.ToString(),
		["name"] = " Synthetic purchase ",
		["amount"] = amount,
		["transactionTime"] = "2026-11-02",
		["items"] = new JsonArray(),
		["shares"] = new JsonArray(new JsonObject { ["userId"] = UserId.ToString(), ["percentage"] = "100.00" }),
		["bankTransactionId"] = bank?.ToString()
	};
	private static JsonObject Item() => new()
	{
		["categoryId"] = CategoryId.ToString(),
		["name"] = " Product ",
		["unitPrice"] = "70.00",
		["quantity"] = "1.000"
	};

	private Task<long> AssignmentCountAsync(Guid bankId) =>
		ScalarAsync<long>($"SELECT count(*) FROM banking.bank_transaction_assignment WHERE bank_transaction_id = '{bankId:D}'");

	private async Task<Guid> AddBankAsync(decimal amount = -100, string status = "Booked", string currency = "PLN", bool foreign = false)
	{
		var id = Guid.NewGuid();
		await ExecuteAsync("""
			INSERT INTO banking.bank_transaction (_id, fk_bank_account_id, amount, status, description, date, currency_code, tink_transaction_id)
			VALUES (@id, @account, @amount, @status, 'Expense stage1 synthetic', DATE '2026-11-02', @currency, @tink)
			""", new NpgsqlParameter("id", id), new NpgsqlParameter("account", Guid.Parse(foreign
				? "40000000-0000-0000-0000-000000000002" : "40000000-0000-0000-0000-000000000001")),
			new NpgsqlParameter("amount", amount), new NpgsqlParameter("status", status), new NpgsqlParameter("currency", currency),
			new NpgsqlParameter("tink", "stage1-" + id.ToString("N")));
		return id;
	}

	private static async Task<HttpResponseMessage> AnalyzeReceiptAsync(HttpClient client)
	{
		using var form = new MultipartFormDataContent();
		AddFile(form);
		return await client.PostAsync("/api/expenses/receipts/analyze", form);
	}

	private static async Task<HttpResponseMessage> SaveWithReceiptAsync(HttpClient client, HttpMethod method, string path,
		JsonObject body, string? proof = null, bool differentFile = false)
	{
		using var request = new HttpRequestMessage(method, path);
		var form = new MultipartFormDataContent();
		request.Content = form;
		form.Add(new StringContent(body.ToJsonString()), "expense");
		if (proof is not null) form.Add(new StringContent(proof), "ocrProof");
		AddFile(form, differentFile);
		return await client.SendAsync(request);
	}

	private static void AddFile(MultipartFormDataContent form, bool differentFile = false)
	{
		var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4//8/AAX+Av4N70a4AAAAAElFTkSuQmCC");
		if (differentFile) bytes[^1] ^= 1;
		var file = new ByteArrayContent(bytes);
		file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
		form.Add(file, "file", "receipt.png");
	}

	private async Task<Host> StartAsync(IInterceptor? interceptor = null, IGroupExpenseMetadataLookup? metadata = null, IUserDisplayNameLookup? names = null,
		ReceiptTestDependencies? receiptDependencies = null, IInterceptor? notificationInterceptor = null)
	{
		var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
		builder.WebHost.UseTestServer();
		var mvc = builder.Services.AddControllers(); mvc.PartManager.ApplicationParts.Clear();
		mvc.AddApplicationPart(typeof(ExpensesController).Assembly);
		builder.Services.AddProblemDetails();
		builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
		builder.Services.AddAuthorization(); builder.Services.AddHttpContextAccessor();
		builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
		builder.Services.AddScoped(_ => new NpgsqlConnection(ConnectionString));
		builder.Services.AddDbContext<ExpenseDbContext>((provider, options) =>
		{
			options.UseNpgsql(provider.GetRequiredService<NpgsqlConnection>(), contextOwnsConnection: false);
			if (interceptor is not null) options.AddInterceptors(interceptor);
		});
		builder.Services.AddDbContext<GroupingDbContext>((provider, options) => options.UseNpgsql(provider.GetRequiredService<NpgsqlConnection>(), contextOwnsConnection: false));
		builder.Services.AddDbContext<BankingDbContext>((provider, options) =>
			options.UseNpgsql(provider.GetRequiredService<NpgsqlConnection>(), contextOwnsConnection: false));
		builder.Services.AddScoped<IBankTransactionAssignmentRepository, BankTransactionAssignmentRepository>();
		builder.Services.AddScoped<IBankTransactionAssignmentService, BankTransactionAssignmentService>();
		builder.Services.AddDbContext<UserDbContext>(options => options.UseNpgsql(ConnectionString));
		builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
		builder.Services.AddScoped<IExpenseRepository, ExpenseRepository>();
		builder.Services.AddScoped<IExpenseItemRepository, ExpenseItemRepository>();
		builder.Services.AddScoped<IExpenseShareRepository, ExpenseShareRepository>();
		builder.Services.AddScoped<IGroupRepository, GroupRepository>();
		builder.Services.AddScoped<IGroupMemberRepository, GroupMemberRepository>();
		builder.Services.AddScoped<IBankAccountRepository, BankAccountRepository>();
		builder.Services.AddScoped<IBankTransactionRepository, BankTransactionRepository>();
		builder.Services.AddScoped<IUserBankingProfileService, UserBankingProfileService>();
		builder.Services.AddScoped<IBankingReadService, BankingReadService>();
		builder.Services.AddDbContext<NotificationsDbContext>((provider, options) =>
		{
			options.UseNpgsql(provider.GetRequiredService<NpgsqlConnection>(), contextOwnsConnection: false);
			if (notificationInterceptor is not null) options.AddInterceptors(notificationInterceptor);
		});
		builder.Services.AddScoped<IGroupingTransactionCoordinator, GroupingTransactionCoordinator>();
		builder.Services.AddScoped<IGroupMemberAggregateService, GroupMemberAggregateService>();
		builder.Services.AddDbContext<ReceiptDbContext>((provider, options) =>
			options.UseNpgsql(provider.GetRequiredService<NpgsqlConnection>(), contextOwnsConnection: false));
		builder.Configuration["ReceiptOcrProof:SigningKey"] = Convert.ToBase64String(new byte[32]);
		builder.Services.AddScoped<IReceiptRepository, ReceiptRepository>();
		builder.Services.AddScoped<IReceiptService, ReceiptService>();
		builder.Services.AddSingleton(TimeProvider.System);
		var receipts = receiptDependencies ?? new ReceiptTestDependencies();
		builder.Services.AddSingleton<IObjectStorage>(receipts);
		builder.Services.AddSingleton<IReceiptAnalysisService>(receipts);
		builder.Services.AddScoped<IGroupMemberService, GroupMemberService>();
		builder.Services.AddScoped<IGroupExpenseMetadataLookup, GroupExpenseMetadataLookup>();
		builder.Services.AddScoped<IGroupExpenseContributionService, GroupExpenseContributionService>();
		builder.Services.AddScoped<IGroupExpenseNotificationService, GroupExpenseNotificationService>();
		builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
		builder.Services.AddScoped<INotificationRecipientRepository, NotificationRecipientRepository>();
		builder.Services.AddScoped<INotificationService, NotificationService>();
		builder.Services.AddScoped<IUserLookupService, UserLookupService>();
		builder.Services.AddScoped<IUserDisplayNameLookup, UserDisplayNameLookup>();
		builder.Services.AddExpenseServices();
		if (metadata is not null) builder.Services.AddSingleton(metadata);
		if (names is not null) builder.Services.AddSingleton(names);
		var app = builder.Build();
		app.UseExceptionHandler(); app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization(); app.MapControllers();
		await app.StartAsync();
		return new Host(app, app.GetTestClient());
	}

	private sealed class Host : IAsyncDisposable
	{
		private readonly WebApplication _app;
		public HttpClient Client { get; }
		public Host(WebApplication app, HttpClient client) { _app = app; Client = client; Authenticate(UserId); }
		public void Authenticate(Guid actor) { Client.DefaultRequestHeaders.Remove("X-Test-User"); Client.DefaultRequestHeaders.Add("X-Test-User", actor.ToString()); }
		public async ValueTask DisposeAsync() { Client.Dispose(); await _app.DisposeAsync(); }
	}
	private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
		: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
	{
		protected override Task<AuthenticateResult> HandleAuthenticateAsync()
		{
			if (!Request.Headers.TryGetValue("X-Test-User", out var user)) return Task.FromResult(AuthenticateResult.NoResult());
			return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(
				new ClaimsIdentity([new Claim("sub", user.ToString())], Scheme.Name)), Scheme.Name)));
		}
	}
	private sealed class FixedMetadata(string currency, bool archived) : IGroupExpenseMetadataLookup
	{
		public Task<IReadOnlyList<Guid>> GetActiveGroupIdsAsync(Guid actor, CancellationToken cancellationToken = default) =>
			throw new NotSupportedException();

		public Task<GroupExpenseMetadataResult> GetAsync(Guid groupId, CancellationToken cancellationToken = default) =>
			Task.FromResult(new GroupExpenseMetadataResult(200, new(groupId, "Dom", currency, archived)));
	}
	private sealed class MissingDisplayNames : IUserDisplayNameLookup
	{
		public Task<IReadOnlyDictionary<Guid, string>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
	}

	private sealed class PrepareBarrier : IUserDisplayNameLookup
	{
		private int arrived;
		private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public async Task<IReadOnlyDictionary<Guid, string>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
		{
			if (Interlocked.Increment(ref arrived) == 2) ready.TrySetResult();
			await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
			return ids.ToDictionary(id => id, _ => "Test");
		}
	}

	private sealed class RejectExpenseDetailReads : DbCommandInterceptor
	{
		public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
			DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
			CancellationToken cancellationToken = default)
		{
			if (command.CommandText.Contains("expense_item", StringComparison.OrdinalIgnoreCase) ||
				command.CommandText.Contains("percentage", StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("Receipt access must not load products or full expense shares.");
			return ValueTask.FromResult(result);
		}
	}

	private sealed class FailAfterSave : SaveChangesInterceptor
	{
		public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
			throw new DbUpdateException("Synthetic failure after database save, before commit.");
	}
	private sealed class DuplicateCategory : SaveChangesInterceptor
	{
		public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
		{
			eventData.Context!.Add(new Category { Id = CategoryId, Name = "Duplicate synthetic", Description = "", Icon = "circle" });
			return ValueTask.FromResult(result);
		}
	}
}
