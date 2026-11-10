namespace ZPI4M.Expense.Services.Contracts;

public sealed record ExpenseLabels(string GroupName, IReadOnlyDictionary<Guid, string> Users, IReadOnlyDictionary<Guid, CategoryDetails> Categories);
