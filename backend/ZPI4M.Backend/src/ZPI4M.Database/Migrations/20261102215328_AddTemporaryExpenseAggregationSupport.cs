using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZPI4M.Database.Migrations
{
	/// <inheritdoc />
	public partial class AddTemporaryExpenseAggregationSupport : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.Sql("""
LOCK TABLE expense.expense, expense.expense_share IN ACCESS EXCLUSIVE MODE;
DO $$ BEGIN IF EXISTS(SELECT 1 FROM expense.expense) OR EXISTS(SELECT 1 FROM expense.expense_share)
THEN RAISE EXCEPTION 'Temporary Expense migration requires empty expenses and shares'; END IF; END $$;
""");
			migrationBuilder.DropColumn(
				name: "percentage",
				schema: "expense",
				table: "expense_share");

			migrationBuilder.AddColumn<decimal>(
				name: "amount",
				schema: "expense",
				table: "expense_share",
				type: "numeric(18,2)",
				precision: 18,
				scale: 2,
				nullable: false);

			migrationBuilder.AddColumn<Guid>(
				name: "fk_payer_id",
				schema: "expense",
				table: "expense",
				type: "uuid",
				nullable: false);

			migrationBuilder.AddCheckConstraint(
				name: "c_expense_share_amount",
				schema: "expense",
				table: "expense_share",
				sql: "amount >= 0");

			migrationBuilder.CreateIndex(
				name: "ix_expense_fk_payer_id",
				schema: "expense",
				table: "expense",
				column: "fk_payer_id");

			migrationBuilder.AddForeignKey(
				name: "FK_expense_user_fk_payer_id",
				schema: "expense",
				table: "expense",
				column: "fk_payer_id",
				principalSchema: "user",
				principalTable: "user",
				principalColumn: "_id");

		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.Sql("""
LOCK TABLE expense.expense, expense.expense_share IN ACCESS EXCLUSIVE MODE;
DO $$ BEGIN IF EXISTS(SELECT 1 FROM expense.expense) OR EXISTS(SELECT 1 FROM expense.expense_share)
THEN RAISE EXCEPTION 'Temporary Expense migration requires empty expenses and shares'; END IF; END $$;
""");
			migrationBuilder.DropForeignKey(
				name: "FK_expense_user_fk_payer_id",
				schema: "expense",
				table: "expense");


			migrationBuilder.DropCheckConstraint(
				name: "c_expense_share_amount",
				schema: "expense",
				table: "expense_share");

			migrationBuilder.DropIndex(
				name: "ix_expense_fk_payer_id",
				schema: "expense",
				table: "expense");

			migrationBuilder.DropColumn(
				name: "amount",
				schema: "expense",
				table: "expense_share");

			migrationBuilder.DropColumn(
				name: "fk_payer_id",
				schema: "expense",
				table: "expense");

			migrationBuilder.AddColumn<decimal>(
				name: "percentage",
				schema: "expense",
				table: "expense_share",
				type: "numeric(5,2)",
				precision: 5,
				scale: 2,
				nullable: false);
			migrationBuilder.Sql("ALTER TABLE expense.expense_share ADD CONSTRAINT c_expense_share_percentage_between_zero_and_hundred CHECK (percentage >= 0 AND percentage <= 100);");
		}
	}
}
