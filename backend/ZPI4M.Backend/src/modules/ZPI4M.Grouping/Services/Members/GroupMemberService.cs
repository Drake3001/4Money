using System.ComponentModel.DataAnnotations;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.DTOs.Members;
using ZPI4M.Grouping.Models;
using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Services.Results;
using ZPI4M.Grouping.Services.Transactions;
using ZPI4M.Shared.Pagination;
using ZPI4M.User.Services.Lookup;
namespace ZPI4M.Grouping.Services.Members;

public sealed class GroupMemberService(IGroupRepository groups, IGroupMemberRepository members, GroupingDbContext db, IGroupingTransactionCoordinator transactions, IUserDisplayNameLookup users) : IGroupMemberService
{
	private static GroupMemberResponse Map(GroupMember m, string? displayName) => new(m.FkUserId, m.FkGroupId, m.IsMember, m.JoinedAt, m.TotalPaid, m.TotalShare, m.TotalSettlementsSent, m.TotalSettlementsReceived, displayName);
	public async Task<GroupingResult<PagedResult<GroupMemberResponse>>> ListAsync(Guid actor, Guid group, GroupMemberListRequest query, CancellationToken ct = default)
	{
		Validator.ValidateObject(query, new ValidationContext(query), true);
		if (!await members.IsMemberAsync(group, actor, ct)) return new(404);
		var page = await members.GetPageAsync(group, new GroupMemberQuery { IsMember = query.Membership switch { "active" => true, "former" => false, _ => null }, PageNumber = query.PageNumber, PageSize = query.PageSize, Descending = query.Descending }, ct);
		var names = await users.GetAsync(page.Items.Select(m => m.FkUserId).ToArray(), ct);
		return new(200, new(page.Items.Select(m => Map(m, names.GetValueOrDefault(m.FkUserId))).ToArray(), page.TotalCount, page.PageNumber, page.PageSize));
	}
	public async Task<GroupingResult<GroupMemberResponse>> GetAsync(Guid actor, Guid group, Guid user, CancellationToken ct = default)
	{
		if (!await members.IsMemberAsync(group, actor, ct)) return new(404);
		var member = await members.GetReadAsync(group, user, ct);
		if (member is null) return new(404);
		var names = await users.GetAsync(new[] { user }, ct);
		return new(200, Map(member, names.GetValueOrDefault(user)));
	}
	public async Task<GroupingResult<IReadOnlySet<Guid>>> GetActiveMemberIdsAsync(Guid actor, Guid group,
		IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
	{
		if (!await members.IsMemberAsync(group, actor, ct)) return new(404);
		var active = await members.GetActiveUsersAmongAsync(group, userIds, ct);
		return new(200, active);
	}

	public Task<GroupingResult> LeaveAsync(Guid actor, Guid group, CancellationToken ct = default) => transactions.ExecuteAsync(token => EndAsync(actor, group, actor, false, token), r => r.Status == 204, ct);
	public Task<GroupingResult> RemoveAsync(Guid actor, Guid group, Guid user, CancellationToken ct = default) => transactions.ExecuteAsync(token => EndAsync(actor, group, user, true, token), r => r.Status == 204, ct);
	private async Task<GroupingResult> EndAsync(Guid actor, Guid groupId, Guid user, bool removal, CancellationToken ct)
	{
		var group = await groups.LockAsync(groupId, ct);
		if (group is null || group.IsArchived) return new(404);
		var caller = await members.GetAsync(groupId, actor, ct);
		if (caller is null || removal && !caller.IsMember) return new(404);
		if (removal && group.AdminUserId != actor) return new(403);
		if (group.AdminUserId == user) return new(409);
		var target = removal ? await members.GetAsync(groupId, user, ct) : caller;
		if (target is null) return new(404);
		if (target.IsMember) { target.IsMember = false; await db.SaveChangesAsync(ct); }
		return new(204);
	}
}
