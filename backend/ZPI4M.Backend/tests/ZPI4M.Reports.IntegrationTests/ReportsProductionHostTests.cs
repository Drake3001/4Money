using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
using ZPI4M.Expense.IntegrationTests;

namespace ZPI4M.Reports.IntegrationTests;

/// <summary>Real Kestrel host, production DI, real login/JWT/session checks, real PostgreSQL.</summary>
public sealed class ReportsProductionHostTests : PostgresFixture
{
	[PostgresFact]
	public async Task ProductionHostServesOpenApiAndAllPostmanRequestsWithRealJwt()
	{
		await SeedAsync();
		var apiDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/ZPI4M.Api"));
		var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
		var assembly = Path.Combine(apiDirectory, "bin", configuration, "net10.0", "ZPI4M.Api.dll");
		Assert.True(File.Exists(assembly), "API assembly must be built via the project reference.");
		var workingDirectory = Path.Combine(Path.GetTempPath(), "zpi4m-report-http-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(workingDirectory); // No .env: never load the developer's credentials.
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		var start = new ProcessStartInfo("dotnet")
		{
			WorkingDirectory = workingDirectory,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		start.ArgumentList.Add(assembly);
		start.ArgumentList.Add("--contentRoot");
		start.ArgumentList.Add(apiDirectory);
		var environment = new Dictionary<string, string>
		{
			["ASPNETCORE_ENVIRONMENT"] = "Development",
			["FRONTEND_URL"] = "http://localhost:5173",
			["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}",
			["DB_LOCAL_CONNECTION_STRING"] = ConnectionString,
			["UserAuthentication__SigningKey"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
			["UserAuthentication__Issuer"] = "ReportsVerification",
			["UserAuthentication__Audience"] = "ReportsVerification",
			["ReceiptOcrProof__SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
			["STORAGE_ENDPOINT"] = "http://127.0.0.1:1",
			["STORAGE_PUBLIC_ENDPOINT"] = "http://127.0.0.1:1",
			["STORAGE_ACCESS_KEY"] = "synthetic-test-access",
			["STORAGE_SECRET_KEY"] = "synthetic-test-secret",
			["STORAGE_BUCKET"] = "test-receipts",
			["STORAGE_REGION"] = "us-east-1",
			["OCR_BASE_URL"] = "http://127.0.0.1:1",
			["Smtp__Host"] = "127.0.0.1",
			["Smtp__Port"] = "1",
			["Smtp__SenderEmail"] = "reports@example.test",
			["Smtp__Username"] = "",
			["Smtp__Password"] = "",
			["Logging__LogLevel__Default"] = "Warning",
			["Logging__EventLog__LogLevel__Default"] = "None"
		};
		foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
		var diagnostics = new ConcurrentQueue<string>();
		using var process = new Process { StartInfo = start };
		process.OutputDataReceived += (_, e) => { if (e.Data is not null) diagnostics.Enqueue(e.Data); };
		process.ErrorDataReceived += (_, e) => { if (e.Data is not null) diagnostics.Enqueue(e.Data); };
		try
		{
			Assert.True(process.Start());
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
			using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(10) };
			var ready = false;
			for (var attempt = 0; attempt < 150 && !process.HasExited; attempt++)
			{
				try
				{
					using var response = await client.GetAsync("/openapi/v1.json");
					if (response.IsSuccessStatusCode) { ready = true; break; }
				}
				catch (HttpRequestException) { }
				await Task.Delay(100);
			}
			Assert.True(ready, "Production host failed: " + string.Join("\n", diagnostics));
			var openApi = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
			var reportPaths = openApi.GetProperty("paths").EnumerateObject().Where(path => path.Name.StartsWith("/api/reports/")).Select(path => path.Name).ToArray();
			Assert.Equal(4, reportPaths.Length);
			Assert.DoesNotContain("/api/reports/monthly-totals", reportPaths);
			Assert.DoesNotContain("/api/reports/spending-trend", reportPaths);
			foreach (var path in reportPaths)
			{
				var operation = openApi.GetProperty("paths").GetProperty(path).GetProperty("get");
				var parameters = operation.GetProperty("parameters").EnumerateArray()
					.ToDictionary(parameter => parameter.GetProperty("name").GetString()!);
				Assert.Equal(new[] { "categoryIds", "dateFrom", "dateTo", "groupId", "scope" }, parameters.Keys.Order());
				foreach (var field in new[] { "dateFrom", "dateTo" })
				{
					Assert.True(parameters[field].GetProperty("required").GetBoolean());
					Assert.Contains("1970-01-01", parameters[field].GetProperty("description").GetString());
				}
				var categories = parameters["categoryIds"];
				Assert.False(categories.TryGetProperty("required", out var required) && required.GetBoolean());
				Assert.Equal("array", categories.GetProperty("schema").GetProperty("type").GetString());
				Assert.Equal("uuid", categories.GetProperty("schema").GetProperty("items").GetProperty("format").GetString());
				Assert.Equal(new[] { "personal", "group" }, parameters["scope"].GetProperty("schema")
					.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
			}
			const string query = "dateFrom=2026-11-01&dateTo=2026-11-30&scope=personal";
			Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/reports/daily-totals?" + query)).StatusCode);
			using var login = await client.PostAsJsonAsync("/api/users/login", new { login = "seed_anna", password = "TestPassword1!" });
			Assert.Equal(HttpStatusCode.OK, login.StatusCode);
			var credentials = await login.Content.ReadFromJsonAsync<JsonElement>();
			client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credentials.GetProperty("accessToken").GetString());

			using var collection = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Baseline", "4Money.Reports.postman_collection.json")));
			var variables = collection.RootElement.GetProperty("variable").EnumerateArray().ToDictionary(v => v.GetProperty("key").GetString()!, v => v.GetProperty("value").GetString()!);
			var sent = 0;
			foreach (var folder in collection.RootElement.GetProperty("item").EnumerateArray())
				foreach (var item in folder.GetProperty("item").EnumerateArray())
				{
					var url = item.GetProperty("request").GetProperty("url").GetProperty("raw").GetString()!.Replace("{{baseUrl}}", "");
					foreach (var pair in variables) url = url.Replace("{{" + pair.Key + "}}", pair.Value);
					using var response = await client.GetAsync(url);
					Assert.True(response.StatusCode == HttpStatusCode.OK, $"{item.GetProperty("name").GetString()}: {(int)response.StatusCode}");
					Assert.True(response.Headers.CacheControl?.NoStore);
					var body = await response.Content.ReadFromJsonAsync<JsonElement>();
					Assert.True(body.TryGetProperty("items", out _) || body.TryGetProperty("series", out _));
					sent++;
				}
			Assert.Equal(17, sent);
			foreach (var route in new[] { "category-breakdown", "period-category-totals", "day-of-week-totals", "daily-totals" })
				foreach (var categories in new[] { "categoryIds=", "categoryIds=abc", "categoryIds=30000000-0000-0000-0000-000000000001&categoryIds=", "categoryIds=&categoryIds=30000000-0000-0000-0000-000000000001" })
					Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/reports/{route}?" + query + "&" + categories)).StatusCode);
			foreach (var route in new[] { "category-breakdown", "period-category-totals", "day-of-week-totals", "daily-totals" })
			{
				foreach (var dates in new[] { "dateFrom=1969-12-31&dateTo=1970-01-01", "dateFrom=1970-01-01&dateTo=1969-12-31" })
				{
					using var invalid = await client.GetAsync($"/api/reports/{route}?{dates}&scope=personal");
					Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
					Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType?.MediaType);
				}
				using var longRange = await client.GetAsync($"/api/reports/{route}?dateFrom=1970-01-01&dateTo=2060-12-31&scope=personal&categoryIds=30000000-0000-0000-0000-000000000001");
				Assert.Equal(HttpStatusCode.OK, longRange.StatusCode);
				var body = await longRange.Content.ReadFromJsonAsync<JsonElement>();
				if (route == "daily-totals")
				{
					var items = body.GetProperty("items");
					Assert.Equal(new DateOnly(2061, 1, 1).DayNumber - new DateOnly(1970, 1, 1).DayNumber, items.GetArrayLength());
					Assert.Equal("1970-01-01", items[0].GetProperty("date").GetString());
					Assert.Equal("2060-12-31", items[items.GetArrayLength() - 1].GetProperty("date").GetString());
				}
				if (route == "period-category-totals")
				{
					Assert.Equal("Year", body.GetProperty("granularity").GetString());
					Assert.Equal(91, body.GetProperty("series")[0].GetProperty("items").GetArrayLength());
				}
			}
			Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/reports/monthly-totals?" + query)).StatusCode);
			Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/users/logout", null)).StatusCode);
			Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/reports/daily-totals?" + query)).StatusCode);
		}
		finally
		{
			if (!process.HasExited)
			{
				process.Kill(entireProcessTree: true);
				await process.WaitForExitAsync();
			}
			Directory.Delete(workingDirectory, recursive: true);
		}
	}
}
