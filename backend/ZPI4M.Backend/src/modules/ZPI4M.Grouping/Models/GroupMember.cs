using System;
using System.Collections.Generic;

namespace ZPI4M.Grouping.Models;

public partial class GroupMember
{
	public Guid FkUserId { get; set; }

	public Guid FkGroupId { get; set; }

	public bool IsMember { get; set; } = true;

	public DateTime JoinedAt { get; set; }

	public decimal TotalPaid { get; set; }

	public decimal TotalShare { get; set; }

	public decimal TotalSettlementsSent { get; set; }

	public decimal TotalSettlementsReceived { get; set; }

	public virtual Group FkGroup { get; set; } = null!;
}
