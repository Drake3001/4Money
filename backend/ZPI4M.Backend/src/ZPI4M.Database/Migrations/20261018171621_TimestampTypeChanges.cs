using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZPI4M.Database.Migrations
{
	/// <inheritdoc />
	public partial class TimestampTypeChanges : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.AlterColumn<DateTime>(
				name: "last_login_time",
				schema: "user",
				table: "user",
				type: "timestamp with time zone",
				nullable: true,
				oldClrType: typeof(DateTime),
				oldType: "timestamp without time zone",
				oldNullable: true);

			migrationBuilder.AlterColumn<DateTime>(
				name: "created_at",
				schema: "user",
				table: "user",
				type: "timestamp with time zone",
				nullable: false,
				defaultValueSql: "CURRENT_TIMESTAMP",
				oldClrType: typeof(DateTime),
				oldType: "timestamp without time zone",
				oldDefaultValueSql: "CURRENT_TIMESTAMP");

			migrationBuilder.AlterColumn<DateTime>(
				name: "transaction_time",
				schema: "expense",
				table: "expense",
				type: "timestamp with time zone",
				nullable: false,
				oldClrType: typeof(DateTime),
				oldType: "timestamp without time zone");
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.AlterColumn<DateTime>(
				name: "last_login_time",
				schema: "user",
				table: "user",
				type: "timestamp without time zone",
				nullable: true,
				oldClrType: typeof(DateTime),
				oldType: "timestamp with time zone",
				oldNullable: true);

			migrationBuilder.AlterColumn<DateTime>(
				name: "created_at",
				schema: "user",
				table: "user",
				type: "timestamp without time zone",
				nullable: false,
				defaultValueSql: "CURRENT_TIMESTAMP",
				oldClrType: typeof(DateTime),
				oldType: "timestamp with time zone",
				oldDefaultValueSql: "CURRENT_TIMESTAMP");

			migrationBuilder.AlterColumn<DateTime>(
				name: "transaction_time",
				schema: "expense",
				table: "expense",
				type: "timestamp without time zone",
				nullable: false,
				oldClrType: typeof(DateTime),
				oldType: "timestamp with time zone");
		}
	}
}
