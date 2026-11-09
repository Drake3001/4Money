using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZPI4M.Database.Migrations
{
	/// <inheritdoc />
	public partial class AddBankTransactionAssignments : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.CreateTable(
				name: "bank_transaction_assignment",
				schema: "banking",
				columns: table => new
				{
					bank_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
					consumer_type = table.Column<int>(type: "integer", nullable: false),
					consumer_id = table.Column<Guid>(type: "uuid", nullable: false)
				},
				constraints: table =>
				{
					table.PrimaryKey("bank_transaction_assignment_pkey", x => x.bank_transaction_id);
					table.CheckConstraint("c_bank_transaction_assignment_consumer_type", "consumer_type IN (1, 2)");
					table.ForeignKey(
						name: "c_bank_transaction_assignment_bank_transaction_id",
						column: x => x.bank_transaction_id,
						principalSchema: "banking",
						principalTable: "bank_transaction",
						principalColumn: "_id");
				});
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropTable(
				name: "bank_transaction_assignment",
				schema: "banking");
		}
	}
}
