using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Limits.Models;

namespace ZPI4M.Limits.Database
{
	public static class LimitsConfiguration
	{
		public static void Configure(ModelBuilder modelBuilder)
		{
			modelBuilder.Entity<CategoryGroupLimit>(entity =>
			{
				entity.HasKey(e => new { e.FkCategoryId, e.FkGroupId }).HasName("category_group_limit_pkey");

				entity.ToTable("category_group_limit", "limits");

				entity.Property(e => e.FkCategoryId).HasColumnName("fk_category_id");
				entity.Property(e => e.FkGroupId).HasColumnName("fk_group_id");
				entity.Property(e => e.MonthlyLimit)
					.HasPrecision(18, 2)
					.HasDefaultValueSql("NULL::numeric")
					.HasColumnName("monthly_limit");
				entity.Property(e => e.WeeklyLimit)
					.HasPrecision(18, 2)
					.HasDefaultValueSql("NULL::numeric")
					.HasColumnName("weekly_limit");
				entity.Property(e => e.YearLimit)
					.HasPrecision(18, 2)
					.HasDefaultValueSql("NULL::numeric")
					.HasColumnName("year_limit");
			});
		}
	}
}
