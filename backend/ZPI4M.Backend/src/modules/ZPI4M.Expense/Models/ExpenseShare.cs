using System;
using System.Collections.Generic;

namespace ZPI4M.Expense.Models;

public partial class ExpenseShare
{
	public Guid FkExpenseId { get; set; }

	public Guid FkUserId { get; set; }

	public decimal Percentage { get; set; }
}
