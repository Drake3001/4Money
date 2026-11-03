using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.DTOs.Invitations;
using ZPI4M.Grouping.DTOs.Members;
using ZPI4M.Grouping.Models;
using ZPI4M.Grouping.Repositories.GroupInvitationRepository;
using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Services.Groups;
using ZPI4M.Grouping.Services.Results;
using ZPI4M.Grouping.Services.Transactions;
using ZPI4M.Notifications.DTOs;
using ZPI4M.Notifications.Models;
using ZPI4M.Notifications.Services.Notifications;
using ZPI4M.Shared.Pagination;
using ZPI4M.Shared.Storage;
using ZPI4M.User.Services.Lookup;
using Group = ZPI4M.Grouping.Models.Group;
namespace ZPI4M.Grouping.Services.Invitations;

public sealed class GroupInvitationService(GroupingDbContext db, IGroupInvitationRepository repository, INotificationService notifications, IUserLookupService users, IGroupIconService icons, TimeProvider clock, IGroupingTransactionCoordinator transactions, IGroupRepository groups, IGroupMemberRepository members) : IGroupInvitationService
{
	public static string GenerateToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
	public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
	private static bool Valid(string? token) => token is not null && Regex.IsMatch(token, @"\A[A-Za-z0-9_-]{43}\z");
	private static GroupInvitationResponse Map(GroupInvitation i) => new(i.Id, i.GroupId, i.CreatedByUserId, i.MaxUses, i.UseCount, new DateTimeOffset(i.ExpiresAt), i.IsActive);
	private static GroupMemberResponse Map(GroupMember m) => new(m.FkUserId, m.FkGroupId, m.IsMember, m.JoinedAt, m.TotalPaid, m.TotalShare, m.TotalSettlementsSent, m.TotalSettlementsReceived);
	private async Task<int> Access(Guid user, Group? group, CancellationToken ct)
	{
		if (group is null || group.IsArchived || !await members.IsMemberAsync(group.Id, user, ct)) return 404;
		return group.AdminUserId == user ? 200 : 403;
	}
	private bool Available(GroupInvitation i) => i.IsActive && i.ExpiresAt > clock.GetUtcNow().UtcDateTime && i.UseCount < i.MaxUses;
	public Task<GroupingResult<CreateGroupInvitationResponse>> CreateAsync(Guid user, Guid group, CreateGroupInvitationRequest request, CancellationToken ct = default)
	{

		return transactions.ExecuteAsync(tokenCt => CreateCoreAsync(user, group, request, tokenCt), r => r.Status is >= 200 and < 300, ct);
	}
	private async Task<GroupingResult<CreateGroupInvitationResponse>> CreateCoreAsync(Guid user, Guid group, CreateGroupInvitationRequest request, CancellationToken ct)
	{

		var access = await Access(user, await groups.LockAsync(group, ct), ct);
		if (access != 200) return new(access);
		if (request.MaxUses <= 0 || request.ExpiresAt is null || request.ExpiresAt <= clock.GetUtcNow()) return new(400);
		var token = GenerateToken();
		var invitation = new GroupInvitation { Id = Guid.NewGuid(), GroupId = group, CreatedByUserId = user, TokenHash = Hash(token), MaxUses = request.MaxUses, ExpiresAt = request.ExpiresAt.Value.UtcDateTime };
		repository.Add(invitation);
		await db.SaveChangesAsync(ct);
		return new(201, new CreateGroupInvitationResponse(Map(invitation), token));
	}
	public async Task<GroupingResult<PagedResult<GroupInvitationResponse>>> ListAsync(Guid user, Guid group, GroupInvitationListRequest query, CancellationToken ct = default)
	{
		var access = await Access(user, await groups.GetReadAsync(group, ct), ct);
		if (access != 200) return new(access);
		var page = await repository.GetPageAsync(group, new GroupInvitationQuery
		{
			IsActive = query.IsActive,
			PageNumber = query.PageNumber,
			PageSize = query.PageSize,
			Descending = query.Descending
		}, ct);
		return new(200, new PagedResult<GroupInvitationResponse>(page.Items.Select(Map).ToArray(), page.TotalCount, page.PageNumber, page.PageSize));
	}
	public Task<GroupingResult> RevokeAsync(Guid user, Guid group, Guid invitation, CancellationToken ct = default)
	{

		return transactions.ExecuteAsync(tokenCt => RevokeCoreAsync(user, group, invitation, tokenCt), r => r.Status is >= 200 and < 300, ct);
	}
	private async Task<GroupingResult> RevokeCoreAsync(Guid user, Guid group, Guid invitation, CancellationToken ct)
	{

		var access = await Access(user, await groups.LockAsync(group, ct), ct);
		if (access != 200) return new(access);
		var row = await repository.LockAsync(invitation, ct);
		if (row is null || row.GroupId != group) return new(404);
		row.IsActive = false;
		await db.SaveChangesAsync(ct); return new(204);
	}
	public async Task<GroupingResult<InvitationPreview>> PreviewAsync(string token, CancellationToken ct = default)
	{
		if (!Valid(token)) return new(400);
		var hash = Hash(token);
		var row = await repository.GetByTokenHashAsync(hash, ct);
		if (row is null || !Available(row)) return new(404);
		var group = await groups.GetReadAsync(row.GroupId, ct);
		if (group is null || group.IsArchived) return new(404);
		FileUrlResult? image = null;
		var key = group.IconStorageKey;
		if (key is null && group.Icon is not null && await icons.IsPresetAsync(group.Icon, ct)) key = group.Icon;
		if (key is not null) image = await icons.GetUrlAsync(key, ct);
		return new(200, new InvitationPreview(group.Id, group.Name, group.Description, group.CurrencyCode, group.Icon, image, new DateTimeOffset(row.ExpiresAt), row.MaxUses - row.UseCount));
	}
	public Task<GroupingResult<GroupMemberResponse>> AcceptAsync(Guid user, string token, CancellationToken ct = default)
	{
		if (!Valid(token)) return Task.FromResult(transactions.Reject(new GroupingResult<GroupMemberResponse>(400)));
		return transactions.ExecuteAsync(tokenCt => AcceptCoreAsync(user, token, tokenCt), r => r.Status is >= 200 and < 300, ct);
	}
	private async Task<GroupingResult<GroupMemberResponse>> AcceptCoreAsync(Guid user, string token, CancellationToken ct)
	{
		if (!Valid(token)) return new(400);
		var hash = Hash(token);
		var found = await repository.GetByTokenHashAsync(hash, ct);
		if (found is null) return new(404);
		var group = await groups.LockAsync(found.GroupId, ct);
		var invitation = await repository.LockAsync(found.Id, ct);
		if (group is null || group.IsArchived || invitation is null || invitation.TokenHash != hash || !invitation.IsActive) return new(404);
		if (!(await users.GetActiveUserIdsAsync(new[] { user }, ct)).Contains(user)) return new(401);
		var member = await members.GetAsync(group.Id, user, ct);
		if (member?.IsMember == true) return new(200, Map(member));
		if (!Available(invitation)) return new(404);
		if (member is null)
		{
			member = new GroupMember { FkUserId = user, FkGroupId = group.Id, IsMember = true, JoinedAt = clock.GetUtcNow().UtcDateTime };
			members.Add(member);
		}
		else member.IsMember = true;
		invitation.UseCount++;
		var recipients = await members.GetActiveUserIdsAsync(group.Id, user, ct);
		await transactions.EnlistNotificationsAsync(ct);
		await db.SaveChangesAsync(ct);
		await notifications.NotifyAsync(new CreateNotificationRequest(NotificationType.GroupMemberJoined, "Nowy członek grupy", "Do grupy dołączył nowy członek.", recipients, group.Id), ct);

		return new(200, Map(member));
	}
}
