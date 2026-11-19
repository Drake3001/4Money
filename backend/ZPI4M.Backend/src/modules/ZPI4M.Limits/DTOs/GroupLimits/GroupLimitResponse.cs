using System.Text.Json.Serialization;
using ZPI4M.Limits.DTOs.Limits;
using ZPI4M.Limits.Models;

namespace ZPI4M.Limits.DTOs.GroupLimits;

public sealed record GroupLimitResponse(Guid Id, Guid GroupId, Guid? CategoryId, string Amount, string CurrencyCode, LimitPeriod Period, DateTimeOffset SetDate, bool IsEnabled, string Version, DateOnly PeriodStartDate, DateOnly PeriodEndDate, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LimitUsageResponse? Usage = null);
