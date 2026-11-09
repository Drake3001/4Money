namespace ZPI4M.Banking.Models;

public enum BankTransactionConsumerType
{
	Expense = 1,
	GroupSettlement = 2
}

public sealed class BankTransactionAssignment
{
	public Guid BankTransactionId { get; set; }
	public BankTransactionConsumerType ConsumerType { get; set; }
	public Guid ConsumerId { get; set; }
}
