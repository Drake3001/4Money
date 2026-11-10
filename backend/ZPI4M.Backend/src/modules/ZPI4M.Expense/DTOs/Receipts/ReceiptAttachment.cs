using Microsoft.AspNetCore.Http;

namespace ZPI4M.Expense.DTOs.Receipts;

internal sealed record ReceiptAttachment(IFormFile File, string? OcrProof);
