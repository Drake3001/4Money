using System.Net;
using System.Text.Json;

namespace ZPI4M.Banking.Tink.Http;

internal static class TinkHttp
{
	public const string ClientName = "Tink";

	public static async Task<T> SendAsync<T>(HttpClient client, HttpRequestMessage request,
		string stage, CancellationToken cancellationToken)
	{
		try
		{
			using var response = await client.SendAsync(request, cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				var error = response.StatusCode switch
				{
					HttpStatusCode.BadRequest => "invalid_request",
					HttpStatusCode.Unauthorized => "unauthorized",
					HttpStatusCode.Forbidden => "forbidden",
					_ => "upstream_error"
				};
				var retryAfter = response.Headers.RetryAfter?.Delta
					?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null);
				throw new TinkApiException(stage, error, response.StatusCode, retryAfter);
			}

			await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
			return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken)
				?? throw new TinkApiException(stage, "invalid_response");
		}
		catch (JsonException)
		{
			throw new TinkApiException(stage, "invalid_response");
		}
		catch (HttpRequestException)
		{
			throw new TinkApiException(stage, "network_error");
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new TinkApiException(stage, "timeout");
		}
	}
}
