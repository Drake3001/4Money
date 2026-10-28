using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZPI4M.Database.Migrations
{
	/// <inheritdoc />
	public partial class AddAccountDeactivationTokens : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropCheckConstraint(
				name: "c_user_token_target_email",
				schema: "user",
				table: "user_token");

			migrationBuilder.DropCheckConstraint(
				name: "c_user_token_valid_purpose",
				schema: "user",
				table: "user_token");

			migrationBuilder.AddCheckConstraint(
				name: "c_user_token_target_email",
				schema: "user",
				table: "user_token",
				sql: "(purpose IN (1, 2, 3, 5) AND target_email IS NOT NULL) OR (purpose = 4 AND target_email IS NULL)");

			migrationBuilder.AddCheckConstraint(
				name: "c_user_token_valid_purpose",
				schema: "user",
				table: "user_token",
				sql: "purpose IN (1, 2, 3, 4, 5)");
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.Sql("DELETE FROM \"user\".\"user_token\" WHERE \"purpose\" = 5;");

			migrationBuilder.DropCheckConstraint(
				name: "c_user_token_target_email",
				schema: "user",
				table: "user_token");

			migrationBuilder.DropCheckConstraint(
				name: "c_user_token_valid_purpose",
				schema: "user",
				table: "user_token");

			migrationBuilder.AddCheckConstraint(
				name: "c_user_token_target_email",
				schema: "user",
				table: "user_token",
				sql: "(purpose IN (1, 2, 3) AND target_email IS NOT NULL) OR (purpose = 4 AND target_email IS NULL)");

			migrationBuilder.AddCheckConstraint(
				name: "c_user_token_valid_purpose",
				schema: "user",
				table: "user_token",
				sql: "purpose IN (1, 2, 3, 4)");
		}
	}
}
