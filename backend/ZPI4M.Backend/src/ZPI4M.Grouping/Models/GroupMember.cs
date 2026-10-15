using System;
using System.Collections.Generic;

namespace ZPI4M.Grouping.Models;

public partial class GroupMember
{
    public Guid FkUserId { get; set; }

    public Guid FkGroupId { get; set; }

    public bool IsAdmin { get; set; }

    public virtual Group FkGroup { get; set; } = null!;
}
