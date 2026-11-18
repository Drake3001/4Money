namespace ZPI4M.Reports.DTOs.DailyTotals;

/// <summary>Every requested date chronologically, with zero for missing dates.</summary>
public sealed record DailyTotalsResponse(IReadOnlyList<DailyTotalsItem> Items);
