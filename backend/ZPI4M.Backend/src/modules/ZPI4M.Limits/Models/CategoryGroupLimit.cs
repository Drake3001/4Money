using System;
using System.Collections.Generic;

namespace ZPI4M.Limits.Models;

public partial class CategoryGroupLimit
{
	public Guid FkCategoryId { get; set; }

	public Guid FkGroupId { get; set; }

	public decimal? WeeklyLimit { get; set; }

	public decimal? MonthlyLimit { get; set; }

	public decimal? YearLimit { get; set; }
}
