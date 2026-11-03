namespace ZPI4M.Grouping.Services.Suggestions;

public sealed class InconsistentGroupBalancesException() : Exception("Group aggregates cannot produce a valid settlement plan.");
