using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Grouping.Models;

namespace ZPI4M.Grouping.Database
{
	public static class GroupingConfiguration
	{
		public static void Configure(ModelBuilder modelBuilder)
		{
			modelBuilder.Entity<Group>(entity =>
			{
				entity.HasKey(e => e.Id).HasName("group_pkey");

				entity.ToTable("group", "grouping");

				entity.Property(e => e.Id)
					.ValueGeneratedNever()
					.HasColumnName("_id");
				entity.Property(e => e.Icon).HasColumnName("icon");
				entity.Property(e => e.MonthlyLimit)
					.HasPrecision(18, 2)
					.HasDefaultValueSql("NULL::numeric")
					.HasColumnName("monthly_limit");
				entity.Property(e => e.Name).HasColumnName("name");
				entity.Property(e => e.WeeklyLimit)
					.HasPrecision(18, 2)
					.HasDefaultValueSql("NULL::numeric")
					.HasColumnName("weekly_limit");
				entity.Property(e => e.YearLimit)
					.HasPrecision(18, 2)
					.HasDefaultValueSql("NULL::numeric")
					.HasColumnName("year_limit");
			});

			modelBuilder.Entity<GroupMember>(entity =>
			{
				entity.HasKey(e => new { e.FkUserId, e.FkGroupId }).HasName("group_member_pkey");

				entity.ToTable("group_member", "grouping");

				entity.Property(e => e.FkUserId).HasColumnName("fk_user_id");
				entity.Property(e => e.FkGroupId).HasColumnName("fk_group_id");
				entity.Property(e => e.IsAdmin).HasColumnName("is_admin");

				entity.HasOne(d => d.FkGroup).WithMany(p => p.GroupMembers)
					.HasForeignKey(d => d.FkGroupId)
					.OnDelete(DeleteBehavior.ClientSetNull)
					.HasConstraintName("c_group_member_fk_group_id");
			});
		}
	}
}
