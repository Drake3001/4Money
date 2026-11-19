namespace ZPI4M.Limits.Services.Results;

public sealed record LimitsResult<T>(int Status, T? Value = default, IReadOnlyDictionary<string, string[]>? Errors = null, string? Code = null);
