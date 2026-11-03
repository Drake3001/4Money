using System;
using System.Collections.Generic;

namespace ZPI4M.Grouping.Models;

public partial class Group
{
	public Guid Id { get; set; }

	public string Name { get; set; } = null!;

	public string? Icon { get; set; }

	public string? Description { get; set; }
	public string? IconStorageKey { get; set; }
	public bool IsArchived { get; set; }

	public Guid AdminUserId { get; set; }

	public string CurrencyCode { get; set; } = null!;

	public virtual ICollection<GroupMember> GroupMembers { get; set; } = new List<GroupMember>();
}
