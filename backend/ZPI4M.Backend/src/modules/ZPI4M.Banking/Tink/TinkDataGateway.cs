using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Numerics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ZPI4M.Banking.Services.Refresh;
using ZPI4M.Banking.Tink.Authentication;
using ZPI4M.Banking.Tink.Configuration;
using ZPI4M.Banking.Tink.Http;

namespace ZPI4M.Banking.Tink;

public sealed class TinkDataGateway(TinkAuthorizationService authorization, IHttpClientFactory clients,
	IOptions<TinkOptions> options, IOptions<BankRefreshOptions> refreshOptions) : ITinkDataGateway
{
	public async Task<ITinkDataSession> OpenAsync(string tinkUserId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tinkUserId);
		if (!options.Value.ContinuousAccessVerified)
			throw new TinkApiException("configuration", "continuous_access_unverified");
		var token = await authorization.CreateUserTokenAsync(tinkUserId, false, "accounts:read,transactions:read", cancellationToken);
		return new Session(clients.CreateClient(TinkHttp.ClientName), authorization, tinkUserId, token, refreshOptions.Value.MaxPages);
	}

	private sealed class Session(HttpClient client, TinkAuthorizationService authorization, string userId,
		string accessToken, int maxPages) : ITinkDataSession
	{
		private string _token = accessToken;
		public void Dispose() => client.Dispose();

		public async Task<IReadOnlyList<TinkBankAccount>> GetAccountsAsync(CancellationToken cancellationToken)
		{
			var items = await PagesAsync("data/v2/accounts?pageSize=100", "accounts", cancellationToken);
			return items.Select(item =>
			{
				var booked = Text(item, "balances", "booked", "amount", "currencyCode");
				var available = Text(item, "balances", "available", "amount", "currencyCode");
				var currency = booked ?? available;
				if (!Currency(currency) || (booked is not null && available is not null && booked != available))
					throw Invalid();
				return new TinkBankAccount(Required(item, "id"), Required(item, "name"),
					Text(item, "identifiers", "financialInstitution", "accountNumber"), Text(item, "identifiers", "iban", "iban"),
					Text(item, "financialInstitutionId"), Text(item, "type"), currency!);
			}).ToArray();
		}

		public async Task<IReadOnlyList<TinkBankTransaction>> GetTransactionsAsync(string accountId,
			DateTimeOffset from, DateTimeOffset through, CancellationToken cancellationToken)
		{
			var path = "data/v2/transactions?pageSize=100&statusIn=BOOKED&accountIdIn=" + Uri.EscapeDataString(accountId)
				+ "&bookedDateGte=" + from.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
				+ "&bookedDateLte=" + through.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
			var items = await PagesAsync(path, "transactions", cancellationToken);
			return items.Select(item =>
			{
				var id = Required(item, "id");
				if (Required(item, "accountId") != accountId || Required(item, "status") != "BOOKED") throw Invalid();
				var currency = Text(item, "amount", "currencyCode");
				if (!Currency(currency)) throw Invalid();
				DateOnly date;
				var booked = Text(item, "dates", "booked");
				if (booked is not null)
				{
					if (!DateOnly.TryParseExact(booked, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) throw Invalid();
				}
				else
				{
					if (!DateTimeOffset.TryParse(Text(item, "bookedDateTime"), CultureInfo.InvariantCulture,
						DateTimeStyles.AssumeUniversal, out var timestamp)) throw Invalid();
					date = DateOnly.FromDateTime(timestamp.UtcDateTime);
				}
				if (date < DateOnly.FromDateTime(from.UtcDateTime) || date > DateOnly.FromDateTime(through.UtcDateTime)) throw Invalid();
				return new TinkBankTransaction(id, accountId,
					ParseAmount(Text(item, "amount", "value", "unscaledValue"), Text(item, "amount", "value", "scale")),
					currency!, "BOOKED", Text(item, "descriptions", "original") ?? Text(item, "descriptions", "display"), date);
			}).ToArray();
		}

		private async Task<List<JsonElement>> PagesAsync(string path, string field, CancellationToken cancellationToken)
		{
			var result = new List<JsonElement>();
			var seen = new HashSet<string>(StringComparer.Ordinal);
			var ids = new HashSet<string>(StringComparer.Ordinal);
			string? page = null;
			for (var index = 0; index < maxPages; index++)
			{
				using var document = await GetAsync(path + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page)), cancellationToken);
				var root = document.RootElement;
				if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(field, out var array) || array.ValueKind != JsonValueKind.Array) throw Invalid();
				foreach (var item in array.EnumerateArray())
				{
					var id = Required(item, "id");
					if (!ids.Add(id)) throw Invalid();
					result.Add(item.Clone());
				}
				page = Text(root, "nextPageToken");
				if (page is null) return result;
				if (!seen.Add(page)) throw Invalid();
			}
			throw new TinkApiException("data", "pagination_limit");
		}

		private async Task<JsonDocument> GetAsync(string path, CancellationToken cancellationToken)
		{
			for (var attempt = 0; ; attempt++)
			{
				using var request = new HttpRequestMessage(HttpMethod.Get, path);
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
				try { return await TinkHttp.SendAsync<JsonDocument>(client, request, "data", cancellationToken); }
				catch (TinkApiException error) when (attempt == 0 && error.UpstreamStatus == HttpStatusCode.Unauthorized)
				{
					_token = await authorization.CreateUserTokenAsync(userId, false, "accounts:read,transactions:read", cancellationToken);
				}
			}
		}
	}

	public static decimal ParseAmount(string? unscaledValue, string? scaleValue)
	{
		if (unscaledValue is null || unscaledValue.Length > 100 || scaleValue is null || scaleValue.Length > 4
			|| !BigInteger.TryParse(unscaledValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
			|| !int.TryParse(scaleValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var scale)
			|| scale is < -100 or > 100) throw Invalid();
		// Work in integer minor units: reject fractions of a cent rather than rounding them.
		if (scale > 2)
		{
			value = BigInteger.DivRem(value, BigInteger.Pow(10, scale - 2), out var remainder);
			if (!remainder.IsZero) throw Invalid();
		}
		else value *= BigInteger.Pow(10, 2 - scale);
		if (BigInteger.Abs(value) > BigInteger.Parse("999999999999999999", CultureInfo.InvariantCulture)) throw Invalid();
		return (decimal)value / 100m;
	}

	private static bool Currency(string? value) => value is { Length: 3 } && value.All(c => c is >= 'A' and <= 'Z');
	private static string Required(JsonElement item, params string[] path)
	{
		var value = Text(item, path);
		return value is { Length: > 0 and <= 128 } ? value : throw Invalid();
	}
	private static string? Text(JsonElement item, params string[] path)
	{
		foreach (var key in path)
		{
			if (item.ValueKind == JsonValueKind.Null) return null;
			if (item.ValueKind != JsonValueKind.Object) throw Invalid();
			if (!item.TryGetProperty(key, out item)) return null;
		}
		if (item.ValueKind == JsonValueKind.Null) return null;
		if (item.ValueKind != JsonValueKind.String) throw Invalid();
		return string.IsNullOrWhiteSpace(item.GetString()) ? null : item.GetString();
	}
	private static TinkApiException Invalid() => new("data", "invalid_response");
}
