using System.ComponentModel.DataAnnotations;
using System.Text;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.DTOs.Groups;
using ZPI4M.Grouping.Models;
using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Services.Results;
using ZPI4M.Grouping.Services.Transactions;
using ZPI4M.Shared.Pagination;
using ZPI4M.User.Services.Lookup;
namespace ZPI4M.Grouping.Services.Groups;

public sealed class GroupService(GroupingDbContext db, IUserLookupService users, IGroupIconService icons, IGroupingTransactionCoordinator transactions, IGroupRepository groups, IGroupMemberRepository members) : IGroupService
{
	private static GroupResponse Map(Group g, bool member) => new(g.Id, g.Name, g.Icon, g.AdminUserId, g.CurrencyCode, member, g.Description, g.IconStorageKey, g.IsArchived);
	private static string? Description(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	private static bool Valid(object request) =>
	 Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(), true)
	 && (Description(request is CreateGroupRequest c ? c.Description : ((UpdateGroupRequest)request).Description)?.EnumerateRunes().Count() ?? 0) <= 255;
	public Task<GroupingResult<GroupResponse>> CreateAsync(Guid userId, CreateGroupRequest request, CancellationToken ct = default)
	{
		if (userId == Guid.Empty || !Valid(request) || string.IsNullOrWhiteSpace(request.Name)) return Task.FromResult(transactions.Reject(new GroupingResult<GroupResponse>(400)));
		return transactions.ExecuteAsync(token => CreateCoreAsync(userId, request, token), r => r.Status is >= 200 and < 300, ct);
	}
	private async Task<GroupingResult<GroupResponse>> CreateCoreAsync(Guid userId, CreateGroupRequest request, CancellationToken ct)
	{
		if (userId == Guid.Empty || !Valid(request) || string.IsNullOrWhiteSpace(request.Name)) return new(400);
		if (request.Icon is not null && request.IconContent is not null) return new(400);
		if (request.Icon is not null && !await icons.IsPresetAsync(request.Icon, ct)) return new(400);
		var group = new Group { Id = Guid.NewGuid(), Name = request.Name.Trim(), Icon = request.Icon, Description = Description(request.Description), CurrencyCode = request.CurrencyCode, AdminUserId = userId };
		group.GroupMembers.Add(new GroupMember { FkGroupId = group.Id, FkUserId = userId, IsMember = true, JoinedAt = DateTime.UtcNow });
		if (request.IconContent is not null)
		{
			group.IconStorageKey = await icons.UploadAsync(group.Id, request.IconContent, request.IconContentType ?? "", ct);
			var newKey = group.IconStorageKey;
			transactions.AfterRollback(() => icons.CleanupAsync(newKey));
		}
		groups.Add(group);
		await db.SaveChangesAsync(ct);
		return new(201, Map(group, true));
	}
	public async Task<GroupingResult<PagedResult<GroupResponse>>> GetPageAsync(Guid userId, GroupListRequest query, CancellationToken ct = default)
	{
		Validator.ValidateObject(query, new ValidationContext(query), true);
		var page = await groups.GetMembershipPageAsync(userId, new GroupMembershipQuery { IsMember = query.Membership switch { "active" => true, "former" => false, _ => null }, PageNumber = query.PageNumber, PageSize = query.PageSize, Descending = query.Descending }, ct);
		return new(200, new(page.Items.Select(item => Map(item.Group, item.IsMember)).ToArray(), page.TotalCount, page.PageNumber, page.PageSize));
	}
	public async Task<GroupingResult<GroupResponse>> GetAsync(Guid userId, Guid groupId, CancellationToken ct = default)
	{
		var group = await groups.GetForActiveMemberAsync(groupId, userId, ct);
		return group is null ? new(404) : new(200, Map(group, true));
	}
	public Task<GroupingResult<GroupResponse>> UpdateAsync(Guid userId, Guid groupId, UpdateGroupRequest request, CancellationToken ct = default)
	{
		if (!Valid(request) || string.IsNullOrWhiteSpace(request.Name)) return Task.FromResult(transactions.Reject(new GroupingResult<GroupResponse>(400)));
		return Change(userId, groupId, request, null, ct);
	}
	public Task<GroupingResult<GroupResponse>> TransferAdministratorAsync(Guid userId, Guid groupId, Guid targetId, CancellationToken ct = default) =>
	 targetId == Guid.Empty ? Task.FromResult(transactions.Reject(new GroupingResult<GroupResponse>(400))) : Change(userId, groupId, null, targetId, ct);
	public async Task<GroupingResult> ArchiveAsync(Guid userId, Guid groupId, CancellationToken ct = default) => new((await Change(userId, groupId, null, null, ct, true)).Status);
	private Task<GroupingResult<GroupResponse>> Change(Guid userId, Guid groupId, UpdateGroupRequest? request, Guid? target, CancellationToken ct, bool archive = false) =>
  transactions.ExecuteAsync(token => ChangeCoreAsync(userId, groupId, request, target, token, archive), r => r.Status is >= 200 and < 300, ct);
	private async Task<GroupingResult<GroupResponse>> ChangeCoreAsync(Guid userId, Guid groupId, UpdateGroupRequest? request, Guid? target, CancellationToken ct, bool archive)
	{
		if (request is not null && (request.RemoveIcon ? 1 : 0) + (request.Icon is not null ? 1 : 0) + (request.IconContent is not null ? 1 : 0) > 1) return new(400);
		if (request?.Icon is not null && !await icons.IsPresetAsync(request.Icon, ct)) return new(400);
		string? uploaded = null;
		if (request?.IconContent is not null)
		{
			var access = await groups.GetForActiveMemberAsync(groupId, userId, ct);
			if (access is null) return new(404);
			if (access.AdminUserId != userId) return new(403);
			uploaded = await icons.UploadAsync(groupId, request.IconContent, request.IconContentType ?? "", ct);
			transactions.AfterRollback(() => icons.CleanupAsync(uploaded));
		}

		var group = await groups.LockAsync(groupId, ct);
		if (group is null || (await members.GetAsync(groupId, userId, ct))?.IsMember != true) { return new(404); }
		if (!archive && group.IsArchived) { return new(404); }
		if (group.AdminUserId != userId) { return new(403); }
		if (archive)
		{
			group.IsArchived = true;
			await db.SaveChangesAsync(ct);

			return new(204);
		}
		if (group.IsArchived) { return new(404); }
		var oldKey = group.IconStorageKey;
		if (target is Guid id)
		{
			if (id == userId || !await members.IsMemberAsync(groupId, id, ct)) return new(409);
			if (!(await users.GetActiveUserIdsAsync(new[] { id }, ct)).Contains(id)) return new(409);
			group.AdminUserId = id;
		}
		else
		{
			if (request!.RemoveIcon && request.Icon is not null) return new(400);
			group.Name = request.Name.Trim(); group.Description = Description(request.Description);
			if (request.RemoveIcon || request.Icon is not null || uploaded is not null) { group.Icon = request.Icon; group.IconStorageKey = uploaded; }
		}
		await db.SaveChangesAsync(ct);

		if (oldKey is not null && oldKey != group.IconStorageKey) transactions.AfterCommit(() => icons.CleanupAsync(oldKey));
		return new(200, Map(group, true));
	}
}
