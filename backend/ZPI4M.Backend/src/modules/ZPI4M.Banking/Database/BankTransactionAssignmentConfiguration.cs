using Microsoft.EntityFrameworkCore;
using ZPI4M.Banking.Models;

namespace ZPI4M.Banking.Database;

public static partial class BankingConfiguration
{
	private static void ConfigureAssignments(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<BankTransactionAssignment>(entity =>
		{
			entity.ToTable("bank_transaction_assignment", "banking", table =>
				table.HasCheckConstraint("c_bank_transaction_assignment_consumer_type", "consumer_type IN (1, 2)"));
			entity.HasKey(e => e.BankTransactionId).HasName("bank_transaction_assignment_pkey");
			entity.Property(e => e.BankTransactionId).ValueGeneratedNever().HasColumnName("bank_transaction_id");
			entity.Property(e => e.ConsumerType).HasConversion<int>().HasColumnName("consumer_type");
			entity.Property(e => e.ConsumerId).HasColumnName("consumer_id");
			entity.HasOne<BankTransaction>().WithOne().HasForeignKey<BankTransactionAssignment>(e => e.BankTransactionId)
				.OnDelete(DeleteBehavior.NoAction).HasConstraintName("c_bank_transaction_assignment_bank_transaction_id");
		});
	}
}
