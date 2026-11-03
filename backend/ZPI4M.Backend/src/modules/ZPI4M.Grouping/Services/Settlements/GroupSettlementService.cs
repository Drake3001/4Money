using System.ComponentModel.DataAnnotations;
using ZPI4M.Banking.Services.Reads;
using ZPI4M.Grouping.Database;
using ZPI4M.Grouping.DTOs.Settlements;
using ZPI4M.Grouping.Models;
using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Repositories.GroupSettlementRepository;
using ZPI4M.Grouping.Services.Aggregates;
using ZPI4M.Grouping.Services.Results;
using ZPI4M.Grouping.Services.Transactions;
using ZPI4M.Notifications.DTOs;
using ZPI4M.Notifications.Models;
using ZPI4M.Notifications.Services.Notifications;
using ZPI4M.Shared.Pagination;
namespace ZPI4M.Grouping.Services.Settlements;

public sealed class GroupSettlementService(GroupingDbContext db, IGroupRepository groups, IGroupMemberRepository members, IGroupSettlementRepository settlements, IGroupMemberAggregateService aggregates, IGroupingTransactionCoordinator tx, TimeProvider clock, IBankingReadService banking, INotificationService notifications) : IGroupSettlementService
{
	private async Task<GroupSettlementResponse> MapAsync(GroupSettlement s, Guid actor, CancellationToken ct, Dictionary<Guid, Guid?>? visible = null)
	{
		Guid? bankId = null;
		if (s.BankTransactionId is Guid id)
		{
			if (visible is not null && visible.TryGetValue(id, out var cached)) bankId = cached;
			else
			{
				var result = await banking.GetTransactionAsync(actor, id, ct);
				if (result.IsSuccess && result.Value?.Id == id) bankId = id;
				visible?.Add(id, bankId);
			}
		}
		return new(s.Id, s.GroupId, s.FromUserId, s.ToUserId, s.Amount, s.Method, new DateTimeOffset(s.SettledAt), bankId);
	}
	private GroupingResult<GroupSettlementResponse> Invalid(string field, string message) =>
	 tx.Reject(new GroupingResult<GroupSettlementResponse>(400, Errors: new Dictionary<string, string[]> { [field] = [message] }));
	private static SettlementContribution Contribution(GroupSettlement s) => new(s.GroupId, s.FromUserId, s.ToUserId, s.Amount);
	private static bool Valid(object request) => Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(), true);
	public Task<GroupingResult<GroupSettlementResponse>> CreateAsync(Guid actor, Guid group, CreateGroupSettlementRequest request, CancellationToken ct = default) => Write(actor, group, null, request, null, ct);
	public Task<GroupingResult<GroupSettlementResponse>> UpdateAsync(Guid actor, Guid group, Guid id, UpdateGroupSettlementRequest request, CancellationToken ct = default) => Write(actor, group, id, null, request, ct);
	private Task<GroupingResult<GroupSettlementResponse>> Write(Guid actor, Guid group, Guid? id, CreateGroupSettlementRequest? create, UpdateGroupSettlementRequest? update, CancellationToken ct)
	{
		var validation = new List<ValidationResult>();
		var request = (object?)create ?? update!;
		if (!Validator.TryValidateObject(request, new ValidationContext(request), validation, true))
		{
			var errors = validation.SelectMany(v => v.MemberNames.DefaultIfEmpty("request").Select(m => (Field: char.ToLowerInvariant(m[0]) + m[1..], Message: v.ErrorMessage ?? "Invalid value."))).GroupBy(e => e.Field).ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray());
			return Task.FromResult(tx.Reject(new GroupingResult<GroupSettlementResponse>(400, Errors: errors)));
		}
		var bank = create is not null ? create.BankTransactionId : update!.BankTransactionId;
		var date = create?.SettledAt ?? update!.SettledAt;
		if (bank.HasValue) return Task.FromResult(Invalid("bankTransactionId", "Bank transaction assignment is not supported yet."));
		if (date > clock.GetUtcNow()) return Task.FromResult(Invalid("settledAt", "Settlement date must not be in the future."));
		return tx.ExecuteAsync(async token =>
		{
			var g = await groups.LockAsync(group, token);
			if (g is null || g.IsArchived || !await members.IsMemberAsync(group, actor, token)) return new GroupingResult<GroupSettlementResponse>(404);
			var existing = id.HasValue ? await settlements.GetAsync(group, id.Value, true, token) : null;
			if (id.HasValue && existing is null) return new GroupingResult<GroupSettlementResponse>(404);
			var from = (create?.FromUserId ?? update!.FromUserId)!.Value; var to = (create?.ToUserId ?? update!.ToUserId)!.Value;
			if (g.AdminUserId != actor && (from != actor || existing is not null && existing.FromUserId != actor)) return new GroupingResult<GroupSettlementResponse>(403);
			var parties = await members.GetManyAsync(group, new[] { from, to }, token);
			if (!parties.ContainsKey(from) || !parties.ContainsKey(to)) return new GroupingResult<GroupSettlementResponse>(409);
			var previous = existing is null ? null : Contribution(existing);
			var value = existing ?? new GroupSettlement { Id = Guid.NewGuid(), GroupId = group };
			value.FromUserId = from; value.ToUserId = to; value.Amount = (create?.Amount ?? update!.Amount)!.Value; value.Method = (create?.Method ?? update!.Method)!.Value; value.SettledAt = date!.Value.UtcDateTime; value.BankTransactionId = null;
			if (existing is null) settlements.Add(value);
			try { await aggregates.ApplySettlementChangeAsync(previous, Contribution(value), token); }
			catch (AggregateInvariantException) { return new GroupingResult<GroupSettlementResponse>(409); }
			await db.SaveChangesAsync(token);
			if (existing is null && value.ToUserId != actor)
			{
				await tx.EnlistNotificationsAsync(token);
				await notifications.NotifyAsync(new CreateNotificationRequest(
					NotificationType.SettlementReceived, "Otrzymane rozliczenie",
					"Zarejestrowano zwrot pieniędzy dla Ciebie.", [value.ToUserId], group), token);
			}
			return new GroupingResult<GroupSettlementResponse>(id.HasValue ? 200 : 201, await MapAsync(value, actor, token));
		}, r => r.Status is >= 200 and < 300, ct);
	}
	public Task<GroupingResult> DeleteAsync(Guid actor, Guid group, Guid id, CancellationToken ct = default) => tx.ExecuteAsync(async token =>
	{
		var g = await groups.LockAsync(group, token);
		if (g is null || g.IsArchived || !await members.IsMemberAsync(group, actor, token)) return new GroupingResult(404);
		var value = await settlements.GetAsync(group, id, true, token); if (value is null) return new GroupingResult(404);
		if (g.AdminUserId != actor && value.FromUserId != actor) return new GroupingResult(403);
		try { await aggregates.ApplySettlementChangeAsync(Contribution(value), null, token); }
		catch (AggregateInvariantException) { return new GroupingResult(409); }
		settlements.Remove(value); await db.SaveChangesAsync(token); return new GroupingResult(204);
	}, r => r.Status == 204, ct);
	public async Task<GroupingResult<GroupSettlementResponse>> GetAsync(Guid actor, Guid group, Guid id, CancellationToken ct = default)
	{
		if (!await members.IsMemberAsync(group, actor, ct)) return new(404);
		var value = await settlements.GetAsync(group, id, false, ct); return value is null ? new(404) : new(200, await MapAsync(value, actor, ct));
	}
	public async Task<GroupingResult<PagedResult<GroupSettlementResponse>>> ListAsync(Guid actor, Guid group, GroupSettlementListRequest query, CancellationToken ct = default)
	{
		if (!Valid(query)) return new(400);
		if (!await members.IsMemberAsync(group, actor, ct)) return new(404);
		var page = await settlements.GetPageAsync(group, new GroupSettlementQuery { FromUserId = query.FromUserId, ToUserId = query.ToUserId, DateFrom = query.DateFrom, DateTo = query.DateTo, Method = query.Method, PageNumber = query.PageNumber, PageSize = query.PageSize, Descending = query.Descending }, ct);
		var items = new List<GroupSettlementResponse>();
		var visible = new Dictionary<Guid, Guid?>();
		foreach (var item in page.Items) items.Add(await MapAsync(item, actor, ct, visible));
		return new(200, new(items, page.TotalCount, page.PageNumber, page.PageSize));
	}
}
