namespace ZPI4M.Grouping.Services.Results;

public sealed record GroupingResult(int Status, IReadOnlyDictionary<string, string[]>? Errors = null);
