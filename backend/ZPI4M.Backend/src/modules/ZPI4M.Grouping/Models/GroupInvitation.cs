namespace ZPI4M.Grouping.Models;

public class GroupInvitation
{
	public Guid Id { get; set; }
	public Guid GroupId { get; set; }
	public Guid CreatedByUserId { get; set; }
	public string TokenHash { get; set; } = null!;
	public int MaxUses { get; set; }
	public int UseCount { get; set; }
	public DateTime ExpiresAt { get; set; }
	public bool IsActive { get; set; } = true;
}
