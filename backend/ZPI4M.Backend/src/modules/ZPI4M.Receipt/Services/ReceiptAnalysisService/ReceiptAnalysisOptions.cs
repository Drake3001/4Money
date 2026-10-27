using Microsoft.Extensions.Options;

namespace ZPI4M.Receipt.Services;

public sealed class ReceiptAnalysisOptions
{
	public string BaseUrl { get; set; } = string.Empty;
	public int TimeoutSeconds { get; set; } = 120;
}

public sealed class ReceiptAnalysisOptionsValidator : IValidateOptions<ReceiptAnalysisOptions>
{
	public ValidateOptionsResult Validate(string? name, ReceiptAnalysisOptions options)
	{
		var errors = new List<string>();
		if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var endpoint)
			|| (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)
			|| string.IsNullOrEmpty(endpoint.Host) || !string.IsNullOrEmpty(endpoint.UserInfo)
			|| endpoint.AbsolutePath != "/" || !string.IsNullOrEmpty(endpoint.Query)
			|| !string.IsNullOrEmpty(endpoint.Fragment))
			errors.Add("OCR_BASE_URL must be an absolute HTTP(S) origin without credentials, path, query or fragment.");
		// CancelAfter uses a timer limited to uint.MaxValue - 1 milliseconds.
		if (options.TimeoutSeconds is < 1 or > 4294967)
			errors.Add("OCR_TIMEOUT_SECONDS must be an integer between 1 and 4294967.");
		return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
	}
}
