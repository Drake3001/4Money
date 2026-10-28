using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZPI4M.Database.Migrations
{
	/// <inheritdoc />
	public partial class AddUserTokenTable : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropColumn(
				name: "refresh_token",
				schema: "user",
				table: "user");

			migrationBuilder.DropColumn(
				name: "refresh_token_expiry_time",
				schema: "user",
				table: "user");

			migrationBuilder.AddColumn<bool>(
				name: "is_email_confirmed",
				schema: "user",
				table: "user",
				type: "boolean",
				nullable: false,
				defaultValue: false);

			migrationBuilder.CreateTable(
				name: "user_token",
				schema: "user",
				columns: table => new
				{
					_id = table.Column<Guid>(type: "uuid", nullable: false),
					fk_user_id = table.Column<Guid>(type: "uuid", nullable: false),
					token_hash = table.Column<string>(type: "text", nullable: false),
					purpose = table.Column<int>(type: "integer", nullable: false),
					target_email = table.Column<string>(type: "text", nullable: true),
					created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
					expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
				},
				constraints: table =>
				{
					table.PrimaryKey("user_token_pkey", x => x._id);
					table.CheckConstraint("c_user_token_expiry_after_creation", "expires_at > created_at");
					table.CheckConstraint("c_user_token_target_email", "(purpose IN (1, 2, 3) AND target_email IS NOT NULL) OR (purpose = 4 AND target_email IS NULL)");
					table.CheckConstraint("c_user_token_valid_purpose", "purpose IN (1, 2, 3, 4)");
					table.ForeignKey(
						name: "c_user_token_fk_user_id",
						column: x => x.fk_user_id,
						principalSchema: "user",
						principalTable: "user",
						principalColumn: "_id",
						onDelete: ReferentialAction.Cascade);
				});

			migrationBuilder.CreateIndex(
				name: "c_user_token_unique_token_hash",
				schema: "user",
				table: "user_token",
				column: "token_hash",
				unique: true);

			migrationBuilder.CreateIndex(
				name: "c_user_token_unique_user_purpose",
				schema: "user",
				table: "user_token",
				columns: new[] { "fk_user_id", "purpose" },
				unique: true);
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropTable(
				name: "user_token",
				schema: "user");

			migrationBuilder.DropColumn(
				name: "is_email_confirmed",
				schema: "user",
				table: "user");

			migrationBuilder.AddColumn<string>(
				name: "refresh_token",
				schema: "user",
				table: "user",
				type: "text",
				nullable: true);

			migrationBuilder.AddColumn<DateTime>(
				name: "refresh_token_expiry_time",
				schema: "user",
				table: "user",
				type: "timestamp without time zone",
				nullable: true);
		}
	}
}
