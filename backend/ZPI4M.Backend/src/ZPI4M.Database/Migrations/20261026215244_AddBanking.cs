using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZPI4M.Database.Migrations
{
	/// <inheritdoc />
	public partial class AddBanking : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			// Baseline SQL creates a UNIQUE constraint; EF-only schemas may have a standalone index.
			migrationBuilder.Sql("ALTER TABLE banking.bank_account DROP CONSTRAINT IF EXISTS c_bank_account_unique_fk_user_id_financial_institution_id_numbe;");
			migrationBuilder.Sql("DROP INDEX IF EXISTS banking.c_bank_account_unique_fk_user_id_financial_institution_id_numbe;");

			// Tink preserves debit/credit direction in the sign of the amount.
			migrationBuilder.Sql("ALTER TABLE banking.bank_transaction DROP CONSTRAINT IF EXISTS c_bank_transaction_amount_above_zero;");

			migrationBuilder.AlterColumn<string>(
				name: "number",
				schema: "banking",
				table: "bank_account",
				type: "text",
				nullable: true,
				oldClrType: typeof(string),
				oldType: "text");

			migrationBuilder.AlterColumn<string>(
				name: "iban",
				schema: "banking",
				table: "bank_account",
				type: "text",
				nullable: true,
				oldClrType: typeof(string),
				oldType: "text");

			migrationBuilder.AlterColumn<string>(
				name: "financial_institution_id",
				schema: "banking",
				table: "bank_account",
				type: "text",
				nullable: true,
				oldClrType: typeof(string),
				oldType: "text");

			migrationBuilder.AddColumn<DateTimeOffset>(
				name: "last_successful_sync_at",
				schema: "banking",
				table: "bank_account",
				type: "timestamp with time zone",
				nullable: true);

			migrationBuilder.AddColumn<string>(
				name: "tink_account_id",
				schema: "banking",
				table: "bank_account",
				type: "character varying(128)",
				maxLength: 128,
				nullable: true);

			migrationBuilder.AddColumn<string>(
				name: "tink_user_id",
				schema: "banking",
				table: "bank_account",
				type: "character varying(128)",
				maxLength: 128,
				nullable: true);

			migrationBuilder.AddColumn<DateTimeOffset>(
				name: "transactions_synced_through",
				schema: "banking",
				table: "bank_account",
				type: "timestamp with time zone",
				nullable: true);

			migrationBuilder.CreateTable(
				name: "bank_connection",
				schema: "banking",
				columns: table => new
				{
					_id = table.Column<Guid>(type: "uuid", nullable: false),
					four_money_user_id = table.Column<Guid>(type: "uuid", nullable: false),
					tink_user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
					credentials_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
					provider_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
					provider_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
					connection_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
					sync_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
					created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
					connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
					consent_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
					last_successful_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
					last_sync_failure_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_bank_connection", x => x._id);
					table.ForeignKey(
						name: "FK_bank_connection_user_four_money_user_id",
						column: x => x.four_money_user_id,
						principalSchema: "user",
						principalTable: "user",
						principalColumn: "_id",
						onDelete: ReferentialAction.Restrict);
				});

			migrationBuilder.CreateTable(
				name: "bank_refresh",
				schema: "banking",
				columns: table => new
				{
					_id = table.Column<Guid>(type: "uuid", nullable: false),
					four_money_user_id = table.Column<Guid>(type: "uuid", nullable: false),
					tink_user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
					status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
					created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
					started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
					completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
					next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
					lease_id = table.Column<Guid>(type: "uuid", nullable: true),
					lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
					attempts = table.Column<int>(type: "integer", nullable: false),
					discovery_completed = table.Column<bool>(type: "boolean", nullable: false),
					failure_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_bank_refresh", x => x._id);
					table.ForeignKey(
						name: "FK_bank_refresh_user_four_money_user_id",
						column: x => x.four_money_user_id,
						principalSchema: "user",
						principalTable: "user",
						principalColumn: "_id",
						onDelete: ReferentialAction.Restrict);
				});

			migrationBuilder.CreateTable(
				name: "bank_connection_attempt",
				schema: "banking",
				columns: table => new
				{
					_id = table.Column<Guid>(type: "uuid", nullable: false),
					four_money_user_id = table.Column<Guid>(type: "uuid", nullable: false),
					tink_user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
					status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
					correlation_state_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
					created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
					expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
					completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
					bank_connection_id = table.Column<Guid>(type: "uuid", nullable: true),
					failure_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
					version = table.Column<long>(type: "bigint", nullable: false)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_bank_connection_attempt", x => x._id);
					table.ForeignKey(
						name: "FK_bank_connection_attempt_bank_connection_bank_connection_id",
						column: x => x.bank_connection_id,
						principalSchema: "banking",
						principalTable: "bank_connection",
						principalColumn: "_id",
						onDelete: ReferentialAction.Restrict);
					table.ForeignKey(
						name: "FK_bank_connection_attempt_user_four_money_user_id",
						column: x => x.four_money_user_id,
						principalSchema: "user",
						principalTable: "user",
						principalColumn: "_id",
						onDelete: ReferentialAction.Restrict);
				});

			migrationBuilder.CreateTable(
				name: "bank_refresh_account",
				schema: "banking",
				columns: table => new
				{
					refresh_id = table.Column<Guid>(type: "uuid", nullable: false),
					account_id = table.Column<Guid>(type: "uuid", nullable: false),
					tink_account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
					status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
					failure_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
					retryable = table.Column<bool>(type: "boolean", nullable: false),
					retry_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
					from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
					through = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
					last_successful_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
					added_transactions = table.Column<int>(type: "integer", nullable: false),
					updated_transactions = table.Column<int>(type: "integer", nullable: false)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_bank_refresh_account", x => new { x.refresh_id, x.account_id });
					table.ForeignKey(
						name: "FK_bank_refresh_account_bank_account_account_id",
						column: x => x.account_id,
						principalSchema: "banking",
						principalTable: "bank_account",
						principalColumn: "_id",
						onDelete: ReferentialAction.Restrict);
					table.ForeignKey(
						name: "FK_bank_refresh_account_bank_refresh_refresh_id",
						column: x => x.refresh_id,
						principalSchema: "banking",
						principalTable: "bank_refresh",
						principalColumn: "_id",
						onDelete: ReferentialAction.Cascade);
				});

			migrationBuilder.CreateIndex(
				name: "IX_bank_account_tink_user_id_tink_account_id",
				schema: "banking",
				table: "bank_account",
				columns: new[] { "tink_user_id", "tink_account_id" },
				unique: true);

			migrationBuilder.CreateIndex(
				name: "IX_bank_connection_credentials_id",
				schema: "banking",
				table: "bank_connection",
				column: "credentials_id",
				unique: true);

			migrationBuilder.CreateIndex(
				name: "IX_bank_connection_four_money_user_id",
				schema: "banking",
				table: "bank_connection",
				column: "four_money_user_id");

			migrationBuilder.CreateIndex(
				name: "IX_bank_connection_attempt_bank_connection_id",
				schema: "banking",
				table: "bank_connection_attempt",
				column: "bank_connection_id");

			migrationBuilder.CreateIndex(
				name: "IX_bank_connection_attempt_correlation_state_hash",
				schema: "banking",
				table: "bank_connection_attempt",
				column: "correlation_state_hash",
				unique: true);

			migrationBuilder.CreateIndex(
				name: "IX_bank_connection_attempt_four_money_user_id_created_at",
				schema: "banking",
				table: "bank_connection_attempt",
				columns: new[] { "four_money_user_id", "created_at" });

			migrationBuilder.CreateIndex(
				name: "IX_bank_connection_attempt_status_expires_at",
				schema: "banking",
				table: "bank_connection_attempt",
				columns: new[] { "status", "expires_at" });

			migrationBuilder.CreateIndex(
				name: "IX_bank_refresh_four_money_user_id",
				schema: "banking",
				table: "bank_refresh",
				column: "four_money_user_id",
				unique: true,
				filter: "status IN ('QUEUED', 'RUNNING')");

			migrationBuilder.CreateIndex(
				name: "IX_bank_refresh_status_next_attempt_at",
				schema: "banking",
				table: "bank_refresh",
				columns: new[] { "status", "next_attempt_at" });

			migrationBuilder.CreateIndex(
				name: "IX_bank_refresh_account_account_id",
				schema: "banking",
				table: "bank_refresh_account",
				column: "account_id");
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			// Fail safely if imported signed amounts cannot satisfy the old schema.
			migrationBuilder.Sql("ALTER TABLE banking.bank_transaction ADD CONSTRAINT c_bank_transaction_amount_above_zero CHECK (amount > 0);");

			migrationBuilder.DropTable(
				name: "bank_connection_attempt",
				schema: "banking");

			migrationBuilder.DropTable(
				name: "bank_refresh_account",
				schema: "banking");

			migrationBuilder.DropTable(
				name: "bank_connection",
				schema: "banking");

			migrationBuilder.DropTable(
				name: "bank_refresh",
				schema: "banking");

			migrationBuilder.DropIndex(
				name: "IX_bank_account_tink_user_id_tink_account_id",
				schema: "banking",
				table: "bank_account");

			migrationBuilder.DropColumn(
				name: "last_successful_sync_at",
				schema: "banking",
				table: "bank_account");

			migrationBuilder.DropColumn(
				name: "tink_account_id",
				schema: "banking",
				table: "bank_account");

			migrationBuilder.DropColumn(
				name: "tink_user_id",
				schema: "banking",
				table: "bank_account");

			migrationBuilder.DropColumn(
				name: "transactions_synced_through",
				schema: "banking",
				table: "bank_account");

			migrationBuilder.AlterColumn<string>(
				name: "number",
				schema: "banking",
				table: "bank_account",
				type: "text",
				nullable: false,
				defaultValue: "",
				oldClrType: typeof(string),
				oldType: "text",
				oldNullable: true);

			migrationBuilder.AlterColumn<string>(
				name: "iban",
				schema: "banking",
				table: "bank_account",
				type: "text",
				nullable: false,
				defaultValue: "",
				oldClrType: typeof(string),
				oldType: "text",
				oldNullable: true);

			migrationBuilder.AlterColumn<string>(
				name: "financial_institution_id",
				schema: "banking",
				table: "bank_account",
				type: "text",
				nullable: false,
				defaultValue: "",
				oldClrType: typeof(string),
				oldType: "text",
				oldNullable: true);

			migrationBuilder.CreateIndex(
				name: "c_bank_account_unique_fk_user_id_financial_institution_id_numbe",
				schema: "banking",
				table: "bank_account",
				columns: new[] { "fk_user_id", "financial_institution_id", "number" },
				unique: true);
		}
	}
}
