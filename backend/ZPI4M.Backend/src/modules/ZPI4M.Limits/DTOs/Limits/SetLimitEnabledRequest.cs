using System.Text.Json.Serialization;

namespace ZPI4M.Limits.DTOs.Limits;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SetLimitEnabledRequest
{
	[JsonRequired]
	public required bool IsEnabled { get; init; }

	[JsonRequired]
	public required string Version { get; init; }
}
