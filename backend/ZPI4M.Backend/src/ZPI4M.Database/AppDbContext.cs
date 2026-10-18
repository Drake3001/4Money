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
		}
	}
}
