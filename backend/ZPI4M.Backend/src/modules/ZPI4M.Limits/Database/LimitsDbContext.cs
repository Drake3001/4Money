using Microsoft.EntityFrameworkCore;
using ZPI4M.Limits.Models;

namespace ZPI4M.Limits.Database;

public partial class LimitsDbContext : DbContext
{
	public LimitsDbContext(DbContextOptions<LimitsDbContext> options) : base(options)
	{
	}

	public virtual DbSet<UserLimit> UserLimits { get; set; }
	public virtual DbSet<GroupLimit> GroupLimits { get; set; }
	public DbSet<UserLimitAlertState> UserLimitAlertStates { get; set; }
	public DbSet<GroupLimitAlertState> GroupLimitAlertStates { get; set; }

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);
		LimitsConfiguration.Configure(modelBuilder);
		OnModelCreatingPartial(modelBuilder);
	}

	partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
