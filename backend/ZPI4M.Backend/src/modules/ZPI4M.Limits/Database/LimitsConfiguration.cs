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
			modelBuilder.Entity<GroupLimit>(entity =>
			{
				entity.ToTable("group_limit", "limits", t =>
				{
					t.HasCheckConstraint("c_group_limit_amount", "amount > 0");
					t.HasCheckConstraint("c_group_limit_period", "period IN (1, 2, 3)");
				});
				entity.HasKey(e => e.Id);
				entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
				entity.Property(e => e.GroupId).HasColumnName("group_id");
				entity.Property(e => e.Period).HasColumnName("period");
				entity.Property(e => e.Amount).HasColumnName("amount").HasPrecision(18, 2);
				entity.HasIndex(e => new { e.GroupId, e.Period }).IsUnique();
			});

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
