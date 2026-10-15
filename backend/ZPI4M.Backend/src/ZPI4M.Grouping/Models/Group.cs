using System;
using System.Collections.Generic;

namespace ZPI4M.Grouping.Models;

public partial class Group
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Icon { get; set; }

    public decimal? WeeklyLimit { get; set; }

    public decimal? MonthlyLimit { get; set; }

    public decimal? YearLimit { get; set; }

    public virtual ICollection<GroupMember> GroupMembers { get; set; } = new List<GroupMember>();
}
