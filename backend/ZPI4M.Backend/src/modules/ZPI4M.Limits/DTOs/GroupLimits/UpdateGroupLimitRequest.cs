using System.Text.Json.Serialization;

namespace ZPI4M.Limits.DTOs.GroupLimits;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateGroupLimitRequest
{
	[JsonRequired]
	public required string Amount { get; init; }

	[JsonRequired]
	public required string Version { get; init; }
}
