using System.Net;

namespace ZPI4M.Banking.Tink.Http;

// Only safe metadata is carried across the integration boundary.
public sealed class TinkApiException(string stage, string errorCode, HttpStatusCode? upstreamStatus = null, TimeSpan? retryAfter = null)
	: Exception("The banking integration request failed.")
{
	public TimeSpan? RetryAfter { get; } = retryAfter;
	public string Stage { get; } = stage;
	public string ErrorCode { get; } = errorCode;
	public HttpStatusCode? UpstreamStatus { get; } = upstreamStatus;
}
