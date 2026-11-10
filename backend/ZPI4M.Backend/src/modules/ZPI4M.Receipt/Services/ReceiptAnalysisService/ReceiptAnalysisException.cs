using System.Net;

namespace ZPI4M.Receipt.Services;

public enum ReceiptAnalysisErrorKind
{
	HttpError,
	TransportError,
	Timeout,
	InvalidResponse,
	NoUsableData
}

public sealed class ReceiptAnalysisException : Exception
{
	public ReceiptAnalysisErrorKind Kind { get; }
	public HttpStatusCode? StatusCode { get; }

	public ReceiptAnalysisException(ReceiptAnalysisErrorKind kind, HttpStatusCode? statusCode = null)
		: base(kind switch
		{
			ReceiptAnalysisErrorKind.HttpError => "Receipt analysis returned an unsuccessful HTTP status.",
			ReceiptAnalysisErrorKind.TransportError => "Receipt analysis transport failed.",
			ReceiptAnalysisErrorKind.Timeout => "Receipt analysis timed out.",
			ReceiptAnalysisErrorKind.NoUsableData => "Receipt analysis did not find usable data.",
			_ => "Receipt analysis returned an invalid response."
		})
	{
		Kind = kind;
		StatusCode = statusCode;
	}
}
