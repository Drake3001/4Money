using System.Text.Json;

namespace ZPI4M.Receipt.DTOs;

public sealed record ReceiptCreationResult(Guid ReceiptId, JsonElement? ExtractionResult);
