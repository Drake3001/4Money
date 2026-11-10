using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace ZPI4M.Expense.DTOs.Receipts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ReceiptUploadRequest
{
	[Description("Upload exactly one non-empty file up to 20 MiB. Supported formats include JPEG, PNG, WebP, HEIC/HEIF, TIFF, BMP, and PDF. The OCR worker checks file content and the page limit.")]
	[JsonRequired]
	[Required]
	public IFormFile File { get; init; } = null!;
}
