using ZPI4M.Banking.Models;

namespace ZPI4M.Banking.Services.Contracts;

public sealed record BankTransactionAssignmentDetails(
	Guid BankTransactionId, BankTransactionConsumerType ConsumerType, Guid ConsumerId);
