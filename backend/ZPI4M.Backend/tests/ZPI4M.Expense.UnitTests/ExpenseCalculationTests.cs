using Xunit;
using ZPI4M.Expense.Services.Expenses;

namespace ZPI4M.Expense.UnitTests;

public sealed class ExpenseCalculationTests
{
	private static readonly Guid First = Guid.Parse("10000000-0000-0000-0000-000000000001");
	private static readonly Guid Second = Guid.Parse("10000000-0000-0000-0000-000000000002");
	private static readonly Guid Third = Guid.Parse("10000000-0000-0000-0000-000000000003");
	private readonly ExpenseCalculationService _service = new();

	[Fact]
	public void ItemRoundsAwayFromZeroBeforeApplyingDiscount()
	{
		Assert.Equal(0.01m, ExpenseCalculationService.CalculateItemTotal(0.01m, 0.5m, 0m));
		Assert.Equal(-0.01m, ExpenseCalculationService.CalculateItemTotal(-0.01m, 0.5m, 0m));
		Assert.Equal(17m, ExpenseCalculationService.CalculateItemTotal(10m, 2m, -3m));
	}

	[Fact]
	public void RemaindersCoverTheTotalAndRetainZeroRoundedShares()
	{
		var shares = _service.CalculateShares(0.01m, [(First, 33.33m), (Second, 33.33m), (Third, 33.34m)]);
		Assert.Equal(3, shares.Count);
		Assert.Equal(0.01m, shares.Sum(share => share.Amount));
		Assert.Equal(0.01m, Assert.Single(shares, share => share.UserId == Third).Amount);
		Assert.Equal(2, shares.Count(share => share.Amount == 0m));
		Assert.Equal(100m, shares.Sum(share => share.Percentage));
	}

	[Fact]
	public void EqualRemaindersUseOrdinalGuidTextRegardlessOfInputOrder()
	{
		var forward = _service.CalculateShares(0.01m, [(First, 50m), (Second, 50m)]);
		var reverse = _service.CalculateShares(0.01m, [(Second, 50m), (First, 50m)]);
		Assert.Equal(forward, reverse);
		Assert.Equal(0.01m, forward[0].Amount);
		Assert.Equal(0m, forward[1].Amount);
	}

	[Fact]
	public void LargestMoneyAmountRetainsAllCents()
	{
		const decimal amount = 9999999999999999.99m;
		var shares = _service.CalculateShares(amount, [(First, 33.33m), (Second, 33.33m), (Third, 33.34m)]);
		Assert.Equal(amount, shares.Sum(share => share.Amount));
		Assert.All(shares, share => Assert.Equal(share.Amount, decimal.Round(share.Amount, 2)));
	}

	[Fact]
	public void CalculatorRejectsInvalidTotalsAndParticipantSets()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => _service.CalculateShares(0m, [(First, 100m)]));
		Assert.Throws<ArgumentOutOfRangeException>(() => _service.CalculateShares(0.001m, [(First, 100m)]));
		Assert.Throws<ArgumentException>(() => _service.CalculateShares(1m, [(First, 50m), (First, 50m)]));
		Assert.Throws<ArgumentException>(() => _service.CalculateShares(1m, [(First, 99m)]));
		Assert.Throws<ArgumentException>(() => _service.CalculateShares(1m, [(First, 0m), (Second, 100m)]));
	}
}
