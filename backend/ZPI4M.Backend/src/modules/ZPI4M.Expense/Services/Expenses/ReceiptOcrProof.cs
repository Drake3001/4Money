using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace ZPI4M.Expense.Services.Expenses;

public sealed class ReceiptOcrProof(IOptions<ReceiptOcrProofOptions> options, TimeProvider timeProvider)
{
	public const int MaximumLength = 4 * 1024 * 1024;

	private readonly byte[] key = Convert.FromBase64String(options.Value.SigningKey);

	private sealed record Payload(Guid Actor, string FileHash, DateTimeOffset ExpiresAt, JsonElement Result);

	public string? Create(Guid actor, string fileHash, JsonElement result)
	{
		var data = JsonSerializer.SerializeToUtf8Bytes(new Payload(actor, fileHash, timeProvider.GetUtcNow().AddHours(24), result));
		var proof = WebEncoders.Base64UrlEncode(data) + "." + WebEncoders.Base64UrlEncode(HMACSHA256.HashData(key, data));
		return proof.Length <= MaximumLength ? proof : null;
	}

	public string? Verify(string proof, Guid actor, string fileHash)
	{
		if (proof.Length > MaximumLength) return null;
		var separator = proof.IndexOf('.');
		if (separator <= 0 || separator != proof.LastIndexOf('.')) return null;
		try
		{
			var data = WebEncoders.Base64UrlDecode(proof[..separator]);
			var signature = WebEncoders.Base64UrlDecode(proof[(separator + 1)..]);
			if (!CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(key, data))) return null;
			var payload = JsonSerializer.Deserialize<Payload>(data);
			return payload is not null && payload.Actor == actor && payload.FileHash == fileHash &&
				payload.ExpiresAt > timeProvider.GetUtcNow() && payload.Result.ValueKind == JsonValueKind.Object
				? payload.Result.GetRawText() : null;
		}
		catch (FormatException) { return null; }
		catch (JsonException) { return null; }
	}
}
