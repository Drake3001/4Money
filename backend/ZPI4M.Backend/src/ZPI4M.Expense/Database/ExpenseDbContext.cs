using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Expense.Models;

namespace ZPI4M.Expense.Database;

public partial class ExpenseDbContext : DbContext
{
    public ExpenseDbContext(DbContextOptions<ExpenseDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<Models.Expense> Expenses { get; set; }

    public virtual DbSet<ExpenseItem> ExpenseItems { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("category_pkey");

            entity.ToTable("category", "expense");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("_id");
            entity.Property(e => e.Color).HasColumnName("color");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Icon).HasColumnName("icon");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<Models.Expense>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("expense_pkey");

            entity.ToTable("expense", "expense");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("_id");
            entity.Property(e => e.Amount)
                .HasPrecision(18, 2)
                .HasColumnName("amount");
            entity.Property(e => e.FkBankTransactionId).HasColumnName("fk_bank_transaction_id");
            entity.Property(e => e.FkGroupId).HasColumnName("fk_group_id");
            entity.Property(e => e.FkReceiptId).HasColumnName("fk_receipt_id");
            entity.Property(e => e.Merchant).HasColumnName("merchant");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.TransactionDate)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("transaction_date");
        });

        modelBuilder.Entity<ExpenseItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("expense_item_pkey");

            entity.ToTable("expense_item", "expense");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("_id");
            entity.Property(e => e.FkCategoryId).HasColumnName("fk_category_id");
            entity.Property(e => e.FkExpenseId).HasColumnName("fk_expense_id");
            entity.Property(e => e.FkUserId).HasColumnName("fk_user_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Quantity)
                .HasPrecision(18, 3)
                .HasColumnName("quantity");
            entity.Property(e => e.UnitPrice)
                .HasPrecision(18, 2)
                .HasColumnName("unit_price");

            entity.HasOne(d => d.FkCategory).WithMany(p => p.ExpenseItems)
                .HasForeignKey(d => d.FkCategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("c_expense_item_fk_category_id");

            entity.HasOne(d => d.FkExpense).WithMany(p => p.ExpenseItems)
                .HasForeignKey(d => d.FkExpenseId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("c_expense_item_fk_expense_id");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
