using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Banking.Models;

namespace ZPI4M.Banking.Database;

public partial class BankingDbContext : DbContext
{
    public BankingDbContext(DbContextOptions<BankingDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<BankAccount> BankAccounts { get; set; }

    public virtual DbSet<BankTransaction> BankTransactions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BankAccount>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("bank_account_pkey");

            entity.ToTable("bank_account", "banking");

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

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
