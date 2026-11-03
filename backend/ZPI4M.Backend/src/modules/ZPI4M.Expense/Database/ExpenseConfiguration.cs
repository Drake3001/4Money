using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ZPI4M.Expense.Models;

namespace ZPI4M.Expense.Database
{
	public static class ExpenseConfiguration
	{
		public static void Configure(ModelBuilder modelBuilder)
		{
			modelBuilder.Entity<Category>(entity =>
			{
				entity.HasKey(e => e.Id).HasName("category_pkey");

				entity.ToTable("category", "expense");

				entity.HasIndex(e => e.Name, "c_category_unique_name").IsUnique();

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

				entity.HasIndex(e => e.FkBankTransactionId, "c_expense_unique_fk_bank_transaction_id").IsUnique();
				entity.HasIndex(e => e.FkReceiptId, "c_expense_unique_fk_receipt_id").IsUnique();

				entity.Property(e => e.Id)
					.ValueGeneratedNever()
					.HasColumnName("_id");
				entity.Property(e => e.Amount)
					.HasPrecision(18, 2)
					.HasColumnName("amount");
				entity.Property(e => e.FkBankTransactionId).HasColumnName("fk_bank_transaction_id");
				entity.Property(e => e.FkGroupId).HasColumnName("fk_group_id");
				entity.Property(e => e.FkReceiptId).HasColumnName("fk_receipt_id");
				entity.Property(e => e.FkPayerId).HasColumnName("fk_payer_id");
				entity.HasIndex(e => e.FkPayerId).HasDatabaseName("ix_expense_fk_payer_id");
				entity.Property(e => e.Merchant).HasColumnName("merchant");
				entity.Property(e => e.Name).HasColumnName("name");
				entity.Property(e => e.TransactionTime)
					.HasColumnType("timestamp with time zone")
					.HasColumnName("transaction_time");
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

			modelBuilder.Entity<ExpenseShare>(entity =>
			{
				entity.HasKey(e => new { e.FkExpenseId, e.FkUserId }).HasName("expense_share_pkey");

				entity.ToTable("expense_share", "expense", t => t.HasCheckConstraint("c_expense_share_amount", "amount >= 0"));

				entity.Property(e => e.FkExpenseId).HasColumnName("fk_expense_id");
				entity.Property(e => e.FkUserId).HasColumnName("fk_user_id");
				entity.Property(e => e.Amount)
					.HasPrecision(18, 2)
					.HasColumnName("amount");
			});
			modelBuilder.Entity<ExpenseShare>().HasOne<Models.Expense>().WithMany()
				.HasForeignKey(e => e.FkExpenseId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("c_expense_share_fk_expense_id");
		}
	}
}
