using ZPI4M.Grouping.DTOs.Suggestions;
using ZPI4M.Grouping.Repositories.GroupRepository;
namespace ZPI4M.Grouping.Services.Suggestions;

public sealed class SettlementSuggestionCalculator
{
	private const decimal Max = 9999999999999999.99m;
	private static bool Money(decimal x) => x >= 0 && x <= Max && decimal.Round(x, 2) == x;
	public GroupSettlementSuggestionsResponse Calculate(GroupBalanceSnapshot snapshot, DateTimeOffset generatedAt, CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		var balances = new List<GroupMemberBalanceResponse>();
		var debtors = new PriorityQueue<(Guid User, decimal Remaining), (decimal NegativeAmount, Guid User)>();
		var creditors = new PriorityQueue<(Guid User, decimal Remaining), (decimal NegativeAmount, Guid User)>();
		var seen = new HashSet<Guid>();
		foreach (var m in snapshot.Members)
		{
			ct.ThrowIfCancellationRequested();
			if (m.UserId == Guid.Empty || !seen.Add(m.UserId) || !Money(m.TotalPaid) || !Money(m.TotalShare) || !Money(m.TotalSettlementsSent) || !Money(m.TotalSettlementsReceived)) throw new InconsistentGroupBalancesException();
			var balance = m.TotalPaid - m.TotalShare + m.TotalSettlementsSent - m.TotalSettlementsReceived;
			if (balance < 0 && m.TotalSettlementsSent - balance > Max || balance > 0 && m.TotalSettlementsReceived + balance > Max) throw new InconsistentGroupBalancesException();
			balances.Add(new(m.UserId, m.IsMember, balance));
			if (balance < 0) debtors.Enqueue((m.UserId, -balance), (balance, m.UserId));
			if (balance > 0) creditors.Enqueue((m.UserId, balance), (-balance, m.UserId));
		}
		if (balances.Sum(m => m.Balance) != 0) throw new InconsistentGroupBalancesException();
		var suggestions = new List<SuggestedSettlementResponse>();
		while (debtors.Count > 0 && creditors.Count > 0)
		{
			ct.ThrowIfCancellationRequested();
			var debtor = debtors.Dequeue(); var creditor = creditors.Dequeue();
			var amount = Math.Min(debtor.Remaining, creditor.Remaining);
			suggestions.Add(new(debtor.User, creditor.User, amount));
			debtor.Remaining -= amount; creditor.Remaining -= amount;
			if (debtor.Remaining > 0) debtors.Enqueue(debtor, (-debtor.Remaining, debtor.User));
			if (creditor.Remaining > 0) creditors.Enqueue(creditor, (-creditor.Remaining, creditor.User));
		}
		return new(snapshot.GroupId, snapshot.CurrencyCode, generatedAt, "largestBalanceFirst", false, balances.OrderBy(m => m.UserId).ToArray(), suggestions, suggestions.Sum(s => s.Amount));
	}
}
