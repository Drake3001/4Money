using System.Text.Json;

namespace ZPI4M.Receipt.Services;

public interface IReceiptAnalysisService
{
	Task<JsonElement> AnalyzeAsync(
		Stream content,
		string contentType,
		CancellationToken cancellationToken = default);
}
