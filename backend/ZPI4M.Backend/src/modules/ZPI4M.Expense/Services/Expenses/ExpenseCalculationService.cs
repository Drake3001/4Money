using ZPI4M.Expense.Services.Contracts;

namespace ZPI4M.Expense.Services.Expenses;

/// <summary>Calculates line totals and distributes whole cents deterministically.</summary>
public sealed class ExpenseCalculationService : IExpenseCalculationService
{
	public static decimal CalculateItemTotal(decimal unitPrice, decimal quantity, decimal discount) =>
		decimal.Round(unitPrice * quantity, 2, MidpointRounding.AwayFromZero) + discount;

	public IReadOnlyList<ExpenseShareDetails> CalculateShares(decimal amount, IEnumerable<(Guid UserId, decimal Percentage)> shares)
	{
		ArgumentNullException.ThrowIfNull(shares);
		if (amount <= 0 || amount > 9999999999999999.99m || decimal.Round(amount, 2) != amount)
			throw new ArgumentOutOfRangeException(nameof(amount));
		var participants = shares.OrderBy(share => share.UserId.ToString("D"), StringComparer.Ordinal).ToArray();
		if (participants.Length == 0 || participants.Any(share => share.UserId == Guid.Empty || share.Percentage <= 0 ||
			share.Percentage > 100 || decimal.Round(share.Percentage, 2) != share.Percentage) ||
			participants.Select(share => share.UserId).Distinct().Count() != participants.Length ||
			participants.Sum(share => share.Percentage) != 100m)
			throw new ArgumentException("Unique participants with positive percentages summing to 100 are required.", nameof(shares));

		var cents = amount * 100m;
		var exact = participants.Select(share => cents * share.Percentage / 100m).ToArray();
		var allocated = exact.Select(decimal.Floor).ToArray();
		var remaining = decimal.ToInt32(cents - allocated.Sum());
		var priority = Enumerable.Range(0, participants.Length)
			.OrderByDescending(index => exact[index] - allocated[index])
			.ThenBy(index => participants[index].UserId.ToString("D"), StringComparer.Ordinal);
		foreach (var index in priority.Take(remaining)) allocated[index]++;
		return participants.Select((share, index) => new ExpenseShareDetails(share.UserId, share.Percentage, allocated[index] / 100m)).ToArray();
	}
}
