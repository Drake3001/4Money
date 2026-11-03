using System.ComponentModel.DataAnnotations;

namespace ZPI4M.Grouping.DTOs.Groups;

public sealed class CreateGroupRequest
{
	[Required]
	public string Name { get; init; } = null!;

	[Required, RegularExpression("^[A-Z]{3}$")]
	public string CurrencyCode { get; init; } = null!;

	public string? Icon { get; init; }
	public string? Description { get; init; }
	[System.Text.Json.Serialization.JsonIgnore]
	public Stream? IconContent { get; init; }
	[System.Text.Json.Serialization.JsonIgnore]
	public string? IconContentType { get; init; }
}
