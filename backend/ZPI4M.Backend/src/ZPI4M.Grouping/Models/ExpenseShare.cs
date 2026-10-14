using System;
using System.Collections.Generic;

namespace ZPI4M.Grouping.Models;

public partial class ExpenseShare
{
    public Guid Id { get; set; }

    public Guid FkUserId { get; set; }

    public Guid FkExpenseId { get; set; }

    public decimal Percentage { get; set; }
}
