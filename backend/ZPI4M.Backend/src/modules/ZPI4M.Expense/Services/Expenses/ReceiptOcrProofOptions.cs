using Microsoft.Extensions.Options;

namespace ZPI4M.Expense.Services.Expenses;

public sealed class ReceiptOcrProofOptions
{
	public const string SectionName = "ReceiptOcrProof";

	public string SigningKey { get; set; } = string.Empty;
}

public sealed class ReceiptOcrProofOptionsValidator : IValidateOptions<ReceiptOcrProofOptions>
{
	public ValidateOptionsResult Validate(string? name, ReceiptOcrProofOptions options)
	{
		const string error = "RECEIPTOCRPROOF__SIGNINGKEY must be a Base64-encoded key containing at least 32 bytes.";
		if (string.IsNullOrWhiteSpace(options.SigningKey)) return ValidateOptionsResult.Fail(error);
		try
		{
			return Convert.FromBase64String(options.SigningKey).Length >= 32
				? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(error);
		}
		catch (FormatException) { return ValidateOptionsResult.Fail(error); }
	}
}
