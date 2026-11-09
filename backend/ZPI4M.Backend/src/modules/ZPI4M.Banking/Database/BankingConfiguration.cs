using Microsoft.EntityFrameworkCore;
using ZPI4M.Banking.Models;

namespace ZPI4M.Banking.Database;

public static partial class BankingConfiguration
{
	public static void Configure(ModelBuilder modelBuilder)
	{
		ConfigureConnections(modelBuilder);
		ConfigureRefreshes(modelBuilder);
		ConfigureAssignments(modelBuilder);
		modelBuilder.Entity<BankAccount>(entity =>
		{
			entity.HasKey(e => e.Id).HasName("bank_account_pkey");

			entity.ToTable("bank_account", "banking");

			entity.HasIndex(e => new { e.TinkUserId, e.TinkAccountId }).IsUnique();
			entity.Property(e => e.TinkAccountId).HasMaxLength(128).HasColumnName("tink_account_id");
			entity.Property(e => e.TinkUserId).HasMaxLength(128).HasColumnName("tink_user_id");
			entity.Property(e => e.LastSuccessfulSyncAt).HasColumnName("last_successful_sync_at");
			entity.Property(e => e.TransactionsSyncedThrough).HasColumnName("transactions_synced_through");

			entity.Property(e => e.Id)
				.ValueGeneratedNever()
				.HasColumnName("_id");
			entity.Property(e => e.CredentialsId).HasColumnName("credentials_id");
			entity.Property(e => e.CurrencyCode).HasColumnName("currency_code");
			entity.Property(e => e.FinancialInstitutionId).HasColumnName("financial_institution_id");
			entity.Property(e => e.FkUserId).HasColumnName("fk_user_id");
			entity.Property(e => e.Iban).HasColumnName("iban");
			entity.Property(e => e.IsClosed).HasColumnName("is_closed");
			entity.Property(e => e.Name).HasColumnName("name");
			entity.Property(e => e.Number).HasColumnName("number");
			entity.Property(e => e.Type).HasColumnName("type");
		});

		modelBuilder.Entity<BankTransaction>(entity =>
		{
			entity.HasKey(e => e.Id).HasName("bank_transaction_pkey");

			entity.ToTable("bank_transaction", "banking");

			entity.HasIndex(e => e.TinkTransactionId, "c_bank_transaction_unique_tink_transaction_id").IsUnique();

			entity.Property(e => e.Id)
				.ValueGeneratedNever()
				.HasColumnName("_id");
			entity.Property(e => e.Amount)
				.HasPrecision(18, 2)
				.HasColumnName("amount");
			entity.Property(e => e.CurrencyCode).HasColumnName("currency_code");
			entity.Property(e => e.Date).HasColumnName("date");
			entity.Property(e => e.Description).HasColumnName("description");
			entity.Property(e => e.FkBankAccountId).HasColumnName("fk_bank_account_id");
			entity.Property(e => e.Status).HasColumnName("status");
			entity.Property(e => e.TinkTransactionId).HasColumnName("tink_transaction_id");

			entity.HasOne(d => d.FkBankAccount).WithMany(p => p.BankTransactions)
				.HasForeignKey(d => d.FkBankAccountId)
				.OnDelete(DeleteBehavior.ClientSetNull)
				.HasConstraintName("c_bank_transaction_fk_bank_account_id");
		});
	}
}
