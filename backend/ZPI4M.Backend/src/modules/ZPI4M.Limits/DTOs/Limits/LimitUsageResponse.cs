namespace ZPI4M.Limits.DTOs.Limits;

public sealed record LimitUsageResponse(string CurrencyCode, string SpentAmount, string RemainingAmount, decimal UsedPercentage, bool IsExceeded);
