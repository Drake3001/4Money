using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZPI4M.Limits.Models;

namespace ZPI4M.Limits.Database;

public static class LimitsConfiguration
{
	public static void Configure(ModelBuilder modelBuilder)
	{
		ConfigureDefinition(modelBuilder.Entity<UserLimit>(), "user_limit", nameof(UserLimit.UserId), "user_id");
		ConfigureDefinition(modelBuilder.Entity<GroupLimit>(), "group_limit", nameof(GroupLimit.GroupId), "group_id");
		ConfigureState<UserLimitAlertState, UserLimit>(modelBuilder.Entity<UserLimitAlertState>(), "user_limit_alert_state");
		ConfigureState<GroupLimitAlertState, GroupLimit>(modelBuilder.Entity<GroupLimitAlertState>(), "group_limit_alert_state");
	}

	private static void ConfigureDefinition<T>(EntityTypeBuilder<T> e, string table, string owner, string ownerColumn)
		where T : class, ILimitDefinition
	{
		e.ToTable(table, "limits", t =>
		{
			t.HasCheckConstraint($"c_{table}_dates", "(period IN (1,2,3) AND start_date IS NULL AND end_date IS NULL) OR (period = 4 AND start_date IS NOT NULL AND end_date IS NOT NULL AND isfinite(start_date) AND isfinite(end_date) AND start_date <= end_date)");
			t.HasCheckConstraint($"c_{table}_amount", "amount > 0");
			t.HasCheckConstraint($"c_{table}_period", "period IN (1,2,3,4)");
			t.HasCheckConstraint($"c_{table}_currency", "currency_code = 'PLN'");
		});
		e.HasKey(x => x.Id);
		e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
		e.Property<Guid>(owner).HasColumnName(ownerColumn);
		e.Property(x => x.CategoryId).HasColumnName("category_id");
		e.Property(x => x.Amount).HasColumnName("amount").HasPrecision(18, 2);
		e.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3).IsRequired();
		e.Property(x => x.Period).HasColumnName("period");
		e.Property(x => x.StartDate).HasColumnName("start_date");
		e.Property(x => x.EndDate).HasColumnName("end_date");
		e.Property(x => x.SetDate).HasColumnName("set_date").HasDefaultValueSql("CURRENT_TIMESTAMP");
		e.Property(x => x.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(true);
		e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().HasDefaultValueSql("gen_random_uuid()");
		e.HasIndex(nameof(ILimitDefinition.IsEnabled), nameof(ILimitDefinition.Id));
	}

	private static void ConfigureState<T, TLimit>(EntityTypeBuilder<T> e, string table)
		where T : class where TLimit : class
	{
		e.ToTable(table, "limits");
		e.HasKey("LimitId", "PeriodStartDate");
		e.Property<Guid>("LimitId").HasColumnName("limit_id");
		e.Property<DateOnly>("PeriodStartDate").HasColumnName("period_start_date");
		e.Property<DateTime?>("ApproachingHandledAt").HasColumnName("approaching_handled_at");
		e.Property<DateTime?>("ExceededNotifiedAt").HasColumnName("exceeded_notified_at");
		e.HasOne<TLimit>().WithMany().HasForeignKey("LimitId").OnDelete(DeleteBehavior.NoAction);
	}
}
