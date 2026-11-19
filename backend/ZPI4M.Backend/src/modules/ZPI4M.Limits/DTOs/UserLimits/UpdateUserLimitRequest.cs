using System.Text.Json.Serialization;

namespace ZPI4M.Limits.DTOs.UserLimits;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateUserLimitRequest
{
	[JsonRequired]
	public required string Amount { get; init; }

	[JsonRequired]
	public required string Version { get; init; }
}
