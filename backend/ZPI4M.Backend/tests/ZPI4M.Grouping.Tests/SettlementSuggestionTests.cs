using Xunit;
using ZPI4M.Grouping.DTOs.Suggestions;
using ZPI4M.Grouping.Repositories.GroupRepository;
using ZPI4M.Grouping.Services.Suggestions;
public sealed class SettlementSuggestionTests
{
	private static GroupBalanceSnapshot Snapshot(params decimal[] balances) => new(Guid.NewGuid(), "PLN", balances.Select((x, i) => new GroupBalanceMember(new Guid(i + 1, 0, 0, new byte[8]), i != 0, Math.Max(x, 0), Math.Max(-x, 0), 0, 0)).ToArray());
	private static GroupSettlementSuggestionsResponse Calculate(GroupBalanceSnapshot s, CancellationToken ct = default) => new SettlementSuggestionCalculator().Calculate(s, DateTimeOffset.UnixEpoch, ct);
	[Fact]
	public void Empty_and_zero_balances_need_no_payments()
	{
		Assert.Empty(Calculate(Snapshot()).Suggestions);
		var result = Calculate(Snapshot(0, 0)); Assert.Empty(result.Suggestions); Assert.Equal(0, result.TotalAmount); Assert.Equal(2, result.Balances.Count);
	}
	[Fact]
	public void Greedy_is_explicitly_not_guaranteed_optimal()
	{
		var result = Calculate(Snapshot(-8, -7, -6, 9, 8, 4));
		Assert.Equal(5, result.Suggestions.Count); Assert.False(result.OptimalityGuaranteed); Assert.Equal(21, result.TotalAmount);
	}
	[Fact]
	public void Ordering_is_deterministic_including_ties_and_former_members()
	{
		var s = Snapshot(-10, -10, 10, 10);
		var first = Calculate(s); var second = Calculate(s with { Members = s.Members.Reverse().ToArray() });
		Assert.Equal(first.Suggestions, second.Suggestions); Assert.Equal(first.Balances, second.Balances);
		Assert.Contains(first.Suggestions, p => p.FromUserId == s.Members[0].UserId);
	}
	[Fact]
	public void Random_balanced_inputs_are_fully_settled_without_self_payments()
	{
		var random = new Random(42);
		for (var test = 0; test < 100; test++)
		{
			var values = Enumerable.Range(0, 20).Select(_ => random.Next(-10000, 10000) / 100m).ToList(); values.Add(-values.Sum());
			var snapshot = Snapshot(values.ToArray()); var result = Calculate(snapshot);
			var outstanding = result.Balances.ToDictionary(x => x.UserId, x => x.Balance);
			foreach (var p in result.Suggestions)
			{
				Assert.True(p.Amount > 0); Assert.NotEqual(p.FromUserId, p.ToUserId);
				Assert.True(outstanding[p.FromUserId] < 0); Assert.True(outstanding[p.ToUserId] > 0);
				outstanding[p.FromUserId] += p.Amount; outstanding[p.ToUserId] -= p.Amount;
			}
			Assert.All(outstanding.Values, v => Assert.Equal(0, v));
			Assert.True(result.Suggestions.Count <= result.Balances.Count(b => b.Balance != 0) - 1);
			Assert.Equal(values.Where(v => v > 0).Sum(), result.TotalAmount);
		}
	}
	[Fact]
	public void Invalid_aggregates_and_nonzero_sum_are_rejected()
	{
		Assert.Throws<InconsistentGroupBalancesException>(() => Calculate(Snapshot(1, -2)));
		var s = Snapshot(1, -1);
		Assert.Throws<InconsistentGroupBalancesException>(() => Calculate(s with { Members = [s.Members[0] with { TotalPaid = 0.001m }, s.Members[1]] }));
		Assert.Throws<InconsistentGroupBalancesException>(() => Calculate(s with { Members = [s.Members[0] with { TotalSettlementsReceived = 9999999999999999.99m }, s.Members[1]] }));
	}
	[Fact]
	public void Large_amounts_stay_exact_and_cancellation_propagates()
	{
		var result = Calculate(Snapshot(-9999999999999999.99m, 9999999999999999.99m));
		Assert.Equal(9999999999999999.99m, Assert.Single(result.Suggestions).Amount);
		Assert.Throws<OperationCanceledException>(() => Calculate(Snapshot(-0.01m, 0.01m), new CancellationToken(true)));
	}
}
