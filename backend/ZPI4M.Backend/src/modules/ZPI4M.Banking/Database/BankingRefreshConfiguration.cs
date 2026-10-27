using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Banking.Models;

namespace ZPI4M.Banking.Database;

public static partial class BankingConfiguration
{
	private static void ConfigureRefreshes(ModelBuilder modelBuilder)
	{
		var job = modelBuilder.Entity<BankRefresh>();
		job.ToTable("bank_refresh", "banking");
		job.HasKey(x => x.Id);
		job.Property(x => x.Id).ValueGeneratedNever();
		job.Property(x => x.TinkUserId).HasMaxLength(128);
		job.Property(x => x.Status).HasMaxLength(32);
		job.Property(x => x.FailureCode).HasMaxLength(64);
		job.HasIndex(x => x.FourMoneyUserId).IsUnique().HasFilter("status IN ('QUEUED', 'RUNNING')");
		job.HasIndex(x => new { x.Status, x.NextAttemptAt });
		job.HasOne<ZPI4M.User.Models.User>().WithMany().HasForeignKey(x => x.FourMoneyUserId).OnDelete(DeleteBehavior.Restrict);
		job.HasMany(x => x.Accounts).WithOne().HasForeignKey(x => x.RefreshId).OnDelete(DeleteBehavior.Cascade);
		var account = modelBuilder.Entity<BankRefreshAccount>();
		account.ToTable("bank_refresh_account", "banking");
		account.HasKey(x => new { x.RefreshId, x.AccountId });
		account.Property(x => x.TinkAccountId).HasMaxLength(128);
		account.Property(x => x.Status).HasMaxLength(32);
		account.Property(x => x.FailureCode).HasMaxLength(64);
		account.HasOne<BankAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
		foreach (var entity in new[] { job.Metadata, account.Metadata })
			foreach (var property in entity.GetProperties())
				property.SetColumnName(property.Name == "Id" ? "_id" : Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
	}
}
