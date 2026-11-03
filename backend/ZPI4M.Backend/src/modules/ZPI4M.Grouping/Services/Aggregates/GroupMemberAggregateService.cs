using ZPI4M.Grouping.Models;
using ZPI4M.Grouping.Repositories.GroupMemberRepository;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Services.Transactions;
namespace ZPI4M.Grouping.Services.Aggregates;

public sealed class GroupMemberAggregateService(IGroupRepository groups, IGroupMemberRepository members, IGroupingTransactionCoordinator transactions) : IGroupMemberAggregateService
{
	private const decimal Max = 9999999999999999.99m;
	private sealed record Delta(Guid User, decimal Paid, decimal Share, decimal Sent, decimal Received);
	private static void Check([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool valid) { if (!valid) throw new AggregateInvariantException("Invalid aggregate contribution or resulting amount."); }
	private static bool Money(decimal value) => value >= 0 && value <= Max && decimal.Round(value, 2) == value;
	public Task ApplySettlementChangeAsync(SettlementContribution? previous, SettlementContribution? current, CancellationToken ct = default)
	{
		transactions.EnsureActive();
		return transactions.ExecuteAsync(async token =>
		{
			Check(previous is not null || current is not null);
			var deltas = new List<Delta>();
			foreach (var pair in new[] { (previous, -1m), (current, 1m) })
			{
				if (pair.Item1 is { } c)
				{
					Check(c.GroupId != Guid.Empty && c.FromUserId != Guid.Empty && c.ToUserId != Guid.Empty && c.FromUserId != c.ToUserId && Money(c.Amount) && c.Amount > 0);
					deltas.Add(new(c.FromUserId, 0, 0, pair.Item2 * c.Amount, 0)); deltas.Add(new(c.ToUserId, 0, 0, 0, pair.Item2 * c.Amount));
				}
			}
			Check(previous is null || current is null || previous.GroupId == current.GroupId);
			await Apply((current ?? previous)!.GroupId, deltas, token); return true;
		}, v => v, ct);
	}
	public Task ApplyExpenseChangeAsync(ExpenseContribution? previous, ExpenseContribution? current, CancellationToken ct = default)
	{
		transactions.EnsureActive();
		return transactions.ExecuteAsync(async token =>
		{
			Check(previous is not null || current is not null);
			var deltas = new List<Delta>();
			foreach (var pair in new[] { (previous, -1m), (current, 1m) })
			{
				if (pair.Item1 is { } c)
				{
					Check(c.GroupId != Guid.Empty && c.PaidByUserId != Guid.Empty && Money(c.Amount) && c.Amount > 0 && c.Shares is not null);
					Check(c.Shares!.All(s => s.UserId != Guid.Empty && Money(s.Amount)) && c.Shares.Select(s => s.UserId).Distinct().Count() == c.Shares.Count && c.Shares.Sum(s => s.Amount) == c.Amount);
					deltas.Add(new(c.PaidByUserId, pair.Item2 * c.Amount, 0, 0, 0));
					deltas.AddRange(c.Shares.Select(s => new Delta(s.UserId, 0, pair.Item2 * s.Amount, 0, 0)));
				}
			}
			Check(previous is null || current is null || previous.GroupId == current.GroupId);
			await Apply((current ?? previous)!.GroupId, deltas, token); return true;
		}, v => v, ct);
	}
	private async Task Apply(Guid groupId, List<Delta> deltas, CancellationToken ct)
	{
		var group = await groups.LockAsync(groupId, ct); Check(group is not null && !group.IsArchived);
		var rows = await members.GetManyAsync(groupId, deltas.Select(d => d.User).Distinct().ToArray(), ct);
		var changes = new List<(GroupMember Member, Delta Value)>();
		foreach (var ds in deltas.GroupBy(d => d.User))
		{
			Check(rows.TryGetValue(ds.Key, out var member));
			var next = new Delta(ds.Key, member!.TotalPaid + ds.Sum(d => d.Paid), member.TotalShare + ds.Sum(d => d.Share), member.TotalSettlementsSent + ds.Sum(d => d.Sent), member.TotalSettlementsReceived + ds.Sum(d => d.Received));
			Check(Money(next.Paid) && Money(next.Share) && Money(next.Sent) && Money(next.Received)); changes.Add((member, next));
		}
		foreach (var (member, next) in changes) { member.TotalPaid = next.Paid; member.TotalShare = next.Share; member.TotalSettlementsSent = next.Sent; member.TotalSettlementsReceived = next.Received; }
	}
}
