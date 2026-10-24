using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZPI4M.Database.Migrations
{
	/// <inheritdoc />
	public partial class AddUniqueExpenseSourceReferences : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.CreateIndex(
				name: "c_expense_unique_fk_bank_transaction_id",
				schema: "expense",
				table: "expense",
				column: "fk_bank_transaction_id",
				unique: true);

			migrationBuilder.CreateIndex(
				name: "c_expense_unique_fk_receipt_id",
				schema: "expense",
				table: "expense",
				column: "fk_receipt_id",
				unique: true);
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropIndex(
				name: "c_expense_unique_fk_bank_transaction_id",
				schema: "expense",
				table: "expense");

			migrationBuilder.DropIndex(
				name: "c_expense_unique_fk_receipt_id",
				schema: "expense",
				table: "expense");
		}
	}
}
