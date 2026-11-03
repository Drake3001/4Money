using ZPI4M.Shared.Storage;

namespace ZPI4M.Grouping.Services.Invitations;

public sealed record InvitationPreview(Guid GroupId, string Name, string? Description, string CurrencyCode, string? Icon, FileUrlResult? Image, DateTimeOffset ExpiresAt, int RemainingUses);
