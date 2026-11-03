namespace ZPI4M.Grouping.DTOs.Groups;

public sealed record GroupResponse(
	Guid Id,
	string Name,
	string? Icon,
	Guid AdminUserId,
	string CurrencyCode,
	bool IsMember,
 string? Description = null,
 string? IconStorageKey = null,
 bool IsArchived = false);
