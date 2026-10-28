using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZPI4M.Database.Migrations
{
	/// <inheritdoc />
	public partial class AddNotificationsConfigurationClass : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			// The table already exists from database-init; only its timestamp type changes.
			migrationBuilder.AlterColumn<DateTime>(
				name: "sent_at",
				schema: "notifications",
				table: "notification",
				type: "timestamp with time zone",
				nullable: false,
				defaultValueSql: "CURRENT_TIMESTAMP",
				oldClrType: typeof(DateTime),
				oldType: "timestamp without time zone",
				oldDefaultValueSql: "CURRENT_TIMESTAMP");
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.AlterColumn<DateTime>(
				name: "sent_at",
				schema: "notifications",
				table: "notification",
				type: "timestamp without time zone",
				nullable: false,
				defaultValueSql: "CURRENT_TIMESTAMP",
				oldClrType: typeof(DateTime),
				oldType: "timestamp with time zone",
				oldDefaultValueSql: "CURRENT_TIMESTAMP");
		}
	}
}
