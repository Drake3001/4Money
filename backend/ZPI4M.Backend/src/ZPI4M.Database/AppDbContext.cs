using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Banking.Database;
using ZPI4M.Expense.Database;
using ZPI4M.Grouping.Database;
using ZPI4M.Limits.Database;
using ZPI4M.Notifications.Database;
using ZPI4M.Receipt.Database;
using ZPI4M.User.Database;

namespace ZPI4M.Database
{
	public class AppDbContext : DbContext
	{
		public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
		{
		}

		public DbSet<ZPI4M.Grouping.Models.GroupSettlement> GroupSettlements { get; set; }
		public DbSet<ZPI4M.Grouping.Models.GroupInvitation> GroupInvitations { get; set; }
		public DbSet<ZPI4M.Limits.Models.GroupLimit> GroupLimits { get; set; }
		public DbSet<ZPI4M.Limits.Models.UserLimit> UserLimits { get; set; }

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			base.OnModelCreating(modelBuilder);

			BankingConfiguration.Configure(modelBuilder);
			ExpenseConfiguration.Configure(modelBuilder);
			GroupingConfiguration.Configure(modelBuilder);
			LimitsConfiguration.Configure(modelBuilder);
			NotificationsConfiguration.Configure(modelBuilder);
			ReceiptConfiguration.Configure(modelBuilder);
			UserConfiguration.Configure(modelBuilder);
			modelBuilder.Entity<ZPI4M.Grouping.Models.Group>().HasOne<ZPI4M.User.Models.User>().WithMany()
				.HasForeignKey(e => e.AdminUserId).OnDelete(DeleteBehavior.NoAction);

			modelBuilder.Entity<ZPI4M.Grouping.Models.GroupMember>().HasOne<ZPI4M.User.Models.User>().WithMany()
				.HasForeignKey(e => e.FkUserId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("c_group_member_fk_user_id");

			modelBuilder.Entity<ZPI4M.Grouping.Models.GroupSettlement>().HasOne<ZPI4M.User.Models.User>().WithMany()
				.HasForeignKey(e => e.FromUserId).OnDelete(DeleteBehavior.NoAction);

			modelBuilder.Entity<ZPI4M.Grouping.Models.GroupSettlement>().HasOne<ZPI4M.User.Models.User>().WithMany()
				.HasForeignKey(e => e.ToUserId).OnDelete(DeleteBehavior.NoAction);

			modelBuilder.Entity<ZPI4M.Grouping.Models.GroupSettlement>().HasOne<ZPI4M.Banking.Models.BankTransaction>().WithMany()
				.HasForeignKey(e => e.BankTransactionId).OnDelete(DeleteBehavior.NoAction);

			modelBuilder.Entity<ZPI4M.Grouping.Models.GroupInvitation>().HasOne<ZPI4M.User.Models.User>().WithMany()
				.HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);

			modelBuilder.Entity<ZPI4M.Limits.Models.GroupLimit>().HasOne<ZPI4M.Grouping.Models.Group>().WithMany()
				.HasForeignKey(e => e.GroupId).OnDelete(DeleteBehavior.NoAction);

			modelBuilder.Entity<ZPI4M.Limits.Models.GroupLimit>().HasOne<ZPI4M.Expense.Models.Category>().WithMany()
				.HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.NoAction);

			modelBuilder.Entity<ZPI4M.Limits.Models.UserLimit>().HasOne<ZPI4M.User.Models.User>().WithMany()
				.HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.NoAction);

			modelBuilder.Entity<ZPI4M.Limits.Models.UserLimit>().HasOne<ZPI4M.Expense.Models.Category>().WithMany()
				.HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.NoAction);

			modelBuilder.Entity<ZPI4M.Expense.Models.Expense>()
				.HasOne<ZPI4M.User.Models.User>().WithMany()
				.HasForeignKey(e => e.FkPayerId)
				.OnDelete(DeleteBehavior.NoAction)
				.HasConstraintName("c_expense_fk_payer_id");
			modelBuilder.Entity<ZPI4M.Notifications.Models.Notification>()
				.HasOne<ZPI4M.Expense.Models.Expense>().WithMany()
				.HasForeignKey(e => e.FkExpenseId)
				.OnDelete(DeleteBehavior.SetNull)
				.HasConstraintName("c_notification_fk_expense_id");
		}
	}
}
