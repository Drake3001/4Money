namespace ZPI4M.Grouping.Services.Aggregates;

public sealed record ExpenseContribution(Guid GroupId, Guid PaidByUserId, decimal Amount, IReadOnlyList<ExpenseShareContribution> Shares);
