using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZPI4M.Database.Migrations
{
	/// <inheritdoc />
	public partial class MoveExpenseShareToExpense : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.RenameTable(
				name: "expense_share",
				schema: "grouping",
				newName: "expense_share",
				newSchema: "expense");
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.RenameTable(
				name: "expense_share",
				schema: "expense",
				newName: "expense_share",
				newSchema: "grouping");
		}
	}
}
