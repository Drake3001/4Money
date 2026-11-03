namespace ZPI4M.Grouping.Models;

public class GroupSettlement
{
	public Guid Id { get; set; }
	public Guid GroupId { get; set; }
	public Guid FromUserId { get; set; }
	public Guid ToUserId { get; set; }
	public decimal Amount { get; set; }
	public SettlementMethod Method { get; set; }
	public DateTime SettledAt { get; set; }
	public Guid? BankTransactionId { get; set; }
}
