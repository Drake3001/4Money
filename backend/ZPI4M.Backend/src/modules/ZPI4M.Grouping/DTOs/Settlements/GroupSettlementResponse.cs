using ZPI4M.Grouping.Models;

namespace ZPI4M.Grouping.DTOs.Settlements;

public sealed record GroupSettlementResponse(
	Guid Id,
	Guid GroupId,
	Guid FromUserId,
	Guid ToUserId,
	decimal Amount,
	SettlementMethod Method,
	DateTimeOffset SettledAt,
	Guid? BankTransactionId);
