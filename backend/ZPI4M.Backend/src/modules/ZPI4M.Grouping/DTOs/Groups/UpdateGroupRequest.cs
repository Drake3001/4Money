using System.ComponentModel.DataAnnotations;

namespace ZPI4M.Grouping.DTOs.Groups;

public sealed class UpdateGroupRequest
{
	[Required]
	public string Name { get; init; } = null!;

	public string? Icon { get; init; }
	public string? Description { get; init; }
	[System.Text.Json.Serialization.JsonIgnore]
	public Stream? IconContent { get; init; }
	[System.Text.Json.Serialization.JsonIgnore]
	public string? IconContentType { get; init; }
	public bool RemoveIcon { get; init; }
}
