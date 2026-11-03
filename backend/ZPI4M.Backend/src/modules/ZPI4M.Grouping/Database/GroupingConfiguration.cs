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
					.HasColumnName("id");
				entity.Property(e => e.Icon).HasColumnName("icon");
				entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(255);
				entity.Property(e => e.IconStorageKey).HasColumnName("icon_storage_key");
				entity.Property(e => e.IsArchived).HasColumnName("is_archived").HasDefaultValue(false);
				entity.ToTable(t => t.HasCheckConstraint("c_group_icon_source", "icon IS NULL OR icon_storage_key IS NULL"));
				entity.Property(e => e.Name).HasColumnName("name");
				entity.Property(e => e.AdminUserId).HasColumnName("admin_user_id");
				entity.Property(e => e.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3).IsRequired();
				entity.ToTable(t => t.HasCheckConstraint("c_group_currency", "currency_code ~ '^[A-Z]{3}$'"));
			});

			modelBuilder.Entity<GroupMember>(entity =>
			{
				entity.HasKey(e => new { e.FkUserId, e.FkGroupId }).HasName("group_member_pkey");

				entity.ToTable("group_user", "grouping");

				entity.Property(e => e.FkUserId).HasColumnName("user_id");
				entity.Property(e => e.FkGroupId).HasColumnName("group_id");
				entity.Property(e => e.IsMember).HasColumnName("is_member").HasDefaultValue(true);
				entity.Property(e => e.JoinedAt).HasColumnName("joined_at").HasColumnType("timestamp with time zone");
				entity.Property(e => e.TotalPaid).HasColumnName("total_paid").HasPrecision(18, 2).HasDefaultValue(0m);
				entity.ToTable(t => t.HasCheckConstraint("c_group_user_total_paid", "total_paid >= 0"));
				entity.Property(e => e.TotalShare).HasColumnName("total_share").HasPrecision(18, 2).HasDefaultValue(0m);
				entity.ToTable(t => t.HasCheckConstraint("c_group_user_total_share", "total_share >= 0"));
				entity.Property(e => e.TotalSettlementsSent).HasColumnName("total_settlements_sent").HasPrecision(18, 2).HasDefaultValue(0m);
				entity.ToTable(t => t.HasCheckConstraint("c_group_user_total_settlements_sent", "total_settlements_sent >= 0"));
				entity.Property(e => e.TotalSettlementsReceived).HasColumnName("total_settlements_received").HasPrecision(18, 2).HasDefaultValue(0m);
				entity.ToTable(t => t.HasCheckConstraint("c_group_user_total_settlements_received", "total_settlements_received >= 0"));

				entity.HasOne(d => d.FkGroup).WithMany(p => p.GroupMembers)
					.HasForeignKey(d => d.FkGroupId)
					.OnDelete(DeleteBehavior.NoAction)
					.HasConstraintName("c_group_member_fk_group_id");
			});
			modelBuilder.Entity<GroupSettlement>(entity =>
			{
				entity.ToTable("group_settlements", "grouping");
				entity.HasKey(e => e.Id);
				entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
				entity.Property(e => e.GroupId).HasColumnName("group_id");
				entity.Property(e => e.FromUserId).HasColumnName("from_user_id");
				entity.Property(e => e.ToUserId).HasColumnName("to_user_id");
				entity.Property(e => e.Amount).HasColumnName("amount").HasPrecision(18, 2);
				entity.Property(e => e.Method).HasColumnName("method");
				entity.Property(e => e.SettledAt).HasColumnName("settled_at").HasColumnType("timestamp with time zone");
				entity.Property(e => e.BankTransactionId).HasColumnName("bank_transaction_id");
				entity.ToTable(t => t.HasCheckConstraint("c_group_settlements_amount", "amount > 0"));
				entity.ToTable(t => t.HasCheckConstraint("c_group_settlements_users", "from_user_id <> to_user_id"));
				entity.ToTable(t => t.HasCheckConstraint("c_group_settlements_method", "method IN (1, 2, 3)"));
				entity.HasIndex(e => e.BankTransactionId).IsUnique();
				entity.HasOne<Group>().WithMany().HasForeignKey(e => e.GroupId).OnDelete(DeleteBehavior.NoAction);
			});

			modelBuilder.Entity<GroupInvitation>(entity =>
			{
				entity.ToTable("group_invitation", "grouping");
				entity.HasKey(e => e.Id);
				entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
				entity.Property(e => e.GroupId).HasColumnName("group_id");
				entity.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
				entity.Property(e => e.TokenHash).HasColumnName("token_hash");
				entity.Property(e => e.MaxUses).HasColumnName("max_uses");
				entity.Property(e => e.UseCount).HasColumnName("use_count").HasDefaultValue(0);
				entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone");
				entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
				entity.ToTable(t => t.HasCheckConstraint("c_group_invitation_uses", "max_uses > 0 AND use_count >= 0 AND use_count <= max_uses"));
				entity.HasIndex(e => e.TokenHash).IsUnique();
				entity.HasOne<Group>().WithMany().HasForeignKey(e => e.GroupId).OnDelete(DeleteBehavior.NoAction);
			});

		}
	}
}
