using System;
using System.Collections.Generic;

namespace ZPI4M.Receipt.Models;

public partial class Receipt
{
	public Guid Id { get; set; }

	public string S3Link { get; set; } = null!;

	public string? ExtractionResult { get; set; }
}
