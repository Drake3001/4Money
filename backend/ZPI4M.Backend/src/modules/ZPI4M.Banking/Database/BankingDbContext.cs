using Microsoft.EntityFrameworkCore;
using ZPI4M.Banking.Models;

namespace ZPI4M.Banking.Database;

public partial class BankingDbContext : DbContext
{
	public BankingDbContext(DbContextOptions<BankingDbContext> options)
		: base(options)
	{
	}

	public DbSet<BankTransactionAssignment> BankTransactionAssignments => Set<BankTransactionAssignment>();

	public DbSet<BankRefresh> Refreshes => Set<BankRefresh>();

	public DbSet<BankRefreshAccount> RefreshAccounts => Set<BankRefreshAccount>();

	public DbSet<BankConnectionAttempt> ConnectionAttempts => Set<BankConnectionAttempt>();

	public DbSet<BankConnection> Connections => Set<BankConnection>();

	public virtual DbSet<BankAccount> BankAccounts { get; set; }

	public virtual DbSet<BankTransaction> BankTransactions { get; set; }

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);
		BankingConfiguration.Configure(modelBuilder);
		OnModelCreatingPartial(modelBuilder);
	}

	partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
