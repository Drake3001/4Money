using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZPI4M.Database.Migrations
{
	/// <inheritdoc />
	public partial class AddNotificationsAfterRebase : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.RenameColumn(
				name: "sent_at",
				schema: "notifications",
				table: "notification",
				newName: "created_at");

			migrationBuilder.AddColumn<Guid>(
				name: "fk_expense_id",
				schema: "notifications",
				table: "notification",
				type: "uuid",
				nullable: true);

			migrationBuilder.AddColumn<string>(
				name: "title",
				schema: "notifications",
				table: "notification",
				type: "text",
				nullable: false,
				defaultValue: "");

			migrationBuilder.AddColumn<int>(
				name: "type",
				schema: "notifications",
				table: "notification",
				type: "integer",
				nullable: false,
				defaultValue: 0);

			migrationBuilder.CreateTable(
				name: "notification_recipient",
				schema: "notifications",
				columns: table => new
				{
					fk_notification_id = table.Column<Guid>(type: "uuid", nullable: false),
					fk_user_id = table.Column<Guid>(type: "uuid", nullable: false),
					read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
				},
				constraints: table =>
				{
					table.PrimaryKey("notification_recipient_pkey", x => new { x.fk_notification_id, x.fk_user_id });
					table.ForeignKey(
						name: "c_notification_recipient_fk_notification_id",
						column: x => x.fk_notification_id,
						principalSchema: "notifications",
						principalTable: "notification",
						principalColumn: "_id",
						onDelete: ReferentialAction.Cascade);
					table.ForeignKey(
						name: "c_notification_recipient_fk_user_id",
						column: x => x.fk_user_id,
						principalSchema: "user",
						principalTable: "user",
						principalColumn: "_id",
						onDelete: ReferentialAction.Restrict);
				});

			migrationBuilder.CreateIndex(
				name: "ix_notification_created_at_id",
				schema: "notifications",
				table: "notification",
				columns: new[] { "created_at", "_id" },
				descending: new bool[0]);

			migrationBuilder.CreateIndex(
				name: "ix_notification_fk_expense_id",
				schema: "notifications",
				table: "notification",
				column: "fk_expense_id");

			migrationBuilder.AddCheckConstraint(
				name: "c_notification_valid_type",
				schema: "notifications",
				table: "notification",
				sql: "type IN (0, 1, 2, 3, 4)");

			migrationBuilder.CreateIndex(
				name: "ix_notification_recipient_unread",
				schema: "notifications",
				table: "notification_recipient",
				columns: new[] { "fk_user_id", "fk_notification_id" },
				filter: "read_at IS NULL");

			migrationBuilder.CreateIndex(
				name: "ix_notification_recipient_user",
				schema: "notifications",
				table: "notification_recipient",
				columns: new[] { "fk_user_id", "fk_notification_id" });

			migrationBuilder.AddForeignKey(
				name: "c_notification_fk_expense_id",
				schema: "notifications",
				table: "notification",
				column: "fk_expense_id",
				principalSchema: "expense",
				principalTable: "expense",
				principalColumn: "_id",
				onDelete: ReferentialAction.SetNull);
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropForeignKey(
				name: "c_notification_fk_expense_id",
				schema: "notifications",
				table: "notification");

			migrationBuilder.DropTable(
				name: "notification_recipient",
				schema: "notifications");

			migrationBuilder.DropIndex(
				name: "ix_notification_created_at_id",
				schema: "notifications",
				table: "notification");

			migrationBuilder.DropIndex(
				name: "ix_notification_fk_expense_id",
				schema: "notifications",
				table: "notification");

			migrationBuilder.DropCheckConstraint(
				name: "c_notification_valid_type",
				schema: "notifications",
				table: "notification");

			migrationBuilder.DropColumn(
				name: "fk_expense_id",
				schema: "notifications",
				table: "notification");

			migrationBuilder.DropColumn(
				name: "title",
				schema: "notifications",
				table: "notification");

			migrationBuilder.DropColumn(
				name: "type",
				schema: "notifications",
				table: "notification");

			migrationBuilder.RenameColumn(
				name: "created_at",
				schema: "notifications",
				table: "notification",
				newName: "sent_at");
		}
	}
}
