namespace ZPI4M.Grouping.Services.Results;

public sealed record GroupingResult<T>(int Status, T? Value = default, IReadOnlyDictionary<string, string[]>? Errors = null);
