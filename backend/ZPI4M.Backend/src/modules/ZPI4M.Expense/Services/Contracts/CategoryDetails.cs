namespace ZPI4M.Expense.Services.Contracts;

public sealed record CategoryDetails(Guid Id, string Name, string Description, string Icon, bool IsSystemGenerated);
