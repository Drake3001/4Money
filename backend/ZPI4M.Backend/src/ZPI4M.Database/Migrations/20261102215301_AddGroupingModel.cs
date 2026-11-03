using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZPI4M.Database.Migrations
{
	/// <inheritdoc />
	public partial class AddGroupingModel : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.Sql("""
LOCK TABLE grouping."group", grouping.group_member IN ACCESS EXCLUSIVE MODE;
DO $$ BEGIN IF EXISTS(SELECT 1 FROM grouping."group") OR EXISTS(SELECT 1 FROM grouping.group_member)
THEN RAISE EXCEPTION 'AddGroupingModel requires empty groups and members'; END IF; END $$;
""");
			migrationBuilder.DropCheckConstraint(
				name: "c_notification_valid_type",
				schema: "notifications",
				table: "notification");

			migrationBuilder.AddCheckConstraint(
				name: "c_notification_valid_type",
				schema: "notifications",
				table: "notification",
				sql: "type IN (0, 1, 2, 3, 4, 5)");

			migrationBuilder.DropColumn(
				name: "monthly_limit",
				schema: "grouping",
				table: "group");

			migrationBuilder.DropColumn(
				name: "weekly_limit",
				schema: "grouping",
				table: "group");

			migrationBuilder.DropColumn(
				name: "year_limit",
				schema: "grouping",
				table: "group");

			migrationBuilder.DropColumn(
				name: "is_admin",
				schema: "grouping",
				table: "group_member");

			migrationBuilder.RenameTable(
				name: "group_member",
				schema: "grouping",
				newName: "group_user",
				newSchema: "grouping");

			migrationBuilder.RenameColumn(
				name: "_id",
				schema: "grouping",
				table: "group",
				newName: "id");

			migrationBuilder.RenameColumn(
				name: "fk_group_id",
				schema: "grouping",
				table: "group_user",
				newName: "group_id");

			migrationBuilder.RenameColumn(
				name: "fk_user_id",
				schema: "grouping",
				table: "group_user",
				newName: "user_id");

			migrationBuilder.Sql("""
ALTER INDEX IF EXISTS grouping."IX_group_member_fk_group_id" RENAME TO "IX_group_user_group_id";
CREATE INDEX IF NOT EXISTS "IX_group_user_group_id" ON grouping.group_user (group_id);
""");

			migrationBuilder.AddColumn<Guid>(
				name: "admin_user_id",
				schema: "grouping",
				table: "group",
				type: "uuid",
				nullable: false);

			migrationBuilder.AddColumn<string>(
				name: "currency_code",
				schema: "grouping",
				table: "group",
				type: "character varying(3)",
				maxLength: 3,
				nullable: false);

			migrationBuilder.AddColumn<string>(
				name: "description",
				schema: "grouping",
				table: "group",
				type: "character varying(255)",
				maxLength: 255,
				nullable: true);

			migrationBuilder.AddColumn<string>(
				name: "icon_storage_key",
				schema: "grouping",
				table: "group",
				type: "text",
				nullable: true);

			migrationBuilder.AddColumn<bool>(
				name: "is_archived",
				schema: "grouping",
				table: "group",
				type: "boolean",
				nullable: false,
				defaultValue: false);

			migrationBuilder.AddColumn<bool>(
				name: "is_member",
				schema: "grouping",
				table: "group_user",
				type: "boolean",
				nullable: false,
				defaultValue: true);

			migrationBuilder.AddColumn<DateTime>(
				name: "joined_at",
				schema: "grouping",
				table: "group_user",
				type: "timestamp with time zone",
				nullable: false,
				defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

			migrationBuilder.AddColumn<decimal>(
				name: "total_paid",
				schema: "grouping",
				table: "group_user",
				type: "numeric(18,2)",
				precision: 18,
				scale: 2,
				nullable: false,
				defaultValue: 0m);

			migrationBuilder.AddColumn<decimal>(
				name: "total_settlements_received",
				schema: "grouping",
				table: "group_user",
				type: "numeric(18,2)",
				precision: 18,
				scale: 2,
				nullable: false,
				defaultValue: 0m);

			migrationBuilder.AddColumn<decimal>(
				name: "total_settlements_sent",
				schema: "grouping",
				table: "group_user",
				type: "numeric(18,2)",
				precision: 18,
				scale: 2,
				nullable: false,
				defaultValue: 0m);

			migrationBuilder.AddColumn<decimal>(
				name: "total_share",
				schema: "grouping",
				table: "group_user",
				type: "numeric(18,2)",
				precision: 18,
				scale: 2,
				nullable: false,
				defaultValue: 0m);

			migrationBuilder.CreateTable(
				name: "group_invitation",
				schema: "grouping",
				columns: table => new
				{
					id = table.Column<Guid>(type: "uuid", nullable: false),
					group_id = table.Column<Guid>(type: "uuid", nullable: false),
					created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
					token_hash = table.Column<string>(type: "text", nullable: false),
					max_uses = table.Column<int>(type: "integer", nullable: false),
					use_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
					expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
					is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_group_invitation", x => x.id);
					table.CheckConstraint("c_group_invitation_uses", "max_uses > 0 AND use_count >= 0 AND use_count <= max_uses");
					table.ForeignKey(
						name: "FK_group_invitation_group_group_id",
						column: x => x.group_id,
						principalSchema: "grouping",
						principalTable: "group",
						principalColumn: "id");
					table.ForeignKey(
						name: "FK_group_invitation_user_created_by_user_id",
						column: x => x.created_by_user_id,
						principalSchema: "user",
						principalTable: "user",
						principalColumn: "_id");
				});

			migrationBuilder.CreateTable(
				name: "group_limit",
				schema: "limits",
				columns: table => new
				{
					id = table.Column<Guid>(type: "uuid", nullable: false),
					group_id = table.Column<Guid>(type: "uuid", nullable: false),
					period = table.Column<int>(type: "integer", nullable: false),
					amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_group_limit", x => x.id);
					table.CheckConstraint("c_group_limit_amount", "amount > 0");
					table.CheckConstraint("c_group_limit_period", "period IN (1, 2, 3)");
					table.ForeignKey(
						name: "FK_group_limit_group_group_id",
						column: x => x.group_id,
						principalSchema: "grouping",
						principalTable: "group",
						principalColumn: "id");
				});

			migrationBuilder.CreateTable(
				name: "group_settlements",
				schema: "grouping",
				columns: table => new
				{
					id = table.Column<Guid>(type: "uuid", nullable: false),
					group_id = table.Column<Guid>(type: "uuid", nullable: false),
					from_user_id = table.Column<Guid>(type: "uuid", nullable: false),
					to_user_id = table.Column<Guid>(type: "uuid", nullable: false),
					amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
					method = table.Column<int>(type: "integer", nullable: false),
					settled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
					bank_transaction_id = table.Column<Guid>(type: "uuid", nullable: true)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_group_settlements", x => x.id);
					table.CheckConstraint("c_group_settlements_amount", "amount > 0");
					table.CheckConstraint("c_group_settlements_method", "method IN (1, 2, 3)");
					table.CheckConstraint("c_group_settlements_users", "from_user_id <> to_user_id");
					table.ForeignKey(
						name: "FK_group_settlements_bank_transaction_bank_transaction_id",
						column: x => x.bank_transaction_id,
						principalSchema: "banking",
						principalTable: "bank_transaction",
						principalColumn: "_id");
					table.ForeignKey(
						name: "FK_group_settlements_group_group_id",
						column: x => x.group_id,
						principalSchema: "grouping",
						principalTable: "group",
						principalColumn: "id");
					table.ForeignKey(
						name: "FK_group_settlements_user_from_user_id",
						column: x => x.from_user_id,
						principalSchema: "user",
						principalTable: "user",
						principalColumn: "_id");
					table.ForeignKey(
						name: "FK_group_settlements_user_to_user_id",
						column: x => x.to_user_id,
						principalSchema: "user",
						principalTable: "user",
						principalColumn: "_id");
				});

			migrationBuilder.CreateIndex(
				name: "IX_group_admin_user_id",
				schema: "grouping",
				table: "group",
				column: "admin_user_id");

			migrationBuilder.AddCheckConstraint(
				name: "c_group_currency",
				schema: "grouping",
				table: "group",
				sql: "currency_code ~ '^[A-Z]{3}$'");

			migrationBuilder.AddCheckConstraint(
				name: "c_group_icon_source",
				schema: "grouping",
				table: "group",
				sql: "icon IS NULL OR icon_storage_key IS NULL");

			migrationBuilder.AddCheckConstraint(
				name: "c_group_user_total_paid",
				schema: "grouping",
				table: "group_user",
				sql: "total_paid >= 0");

			migrationBuilder.AddCheckConstraint(
				name: "c_group_user_total_settlements_received",
				schema: "grouping",
				table: "group_user",
				sql: "total_settlements_received >= 0");

			migrationBuilder.AddCheckConstraint(
				name: "c_group_user_total_settlements_sent",
				schema: "grouping",
				table: "group_user",
				sql: "total_settlements_sent >= 0");

			migrationBuilder.AddCheckConstraint(
				name: "c_group_user_total_share",
				schema: "grouping",
				table: "group_user",
				sql: "total_share >= 0");

			migrationBuilder.CreateIndex(
				name: "IX_group_invitation_created_by_user_id",
				schema: "grouping",
				table: "group_invitation",
				column: "created_by_user_id");

			migrationBuilder.CreateIndex(
				name: "IX_group_invitation_group_id",
				schema: "grouping",
				table: "group_invitation",
				column: "group_id");

			migrationBuilder.CreateIndex(
				name: "IX_group_invitation_token_hash",
				schema: "grouping",
				table: "group_invitation",
				column: "token_hash",
				unique: true);

			migrationBuilder.CreateIndex(
				name: "IX_group_limit_group_id_period",
				schema: "limits",
				table: "group_limit",
				columns: new[] { "group_id", "period" },
				unique: true);

			migrationBuilder.CreateIndex(
				name: "IX_group_settlements_bank_transaction_id",
				schema: "grouping",
				table: "group_settlements",
				column: "bank_transaction_id",
				unique: true);

			migrationBuilder.CreateIndex(
				name: "IX_group_settlements_from_user_id",
				schema: "grouping",
				table: "group_settlements",
				column: "from_user_id");

			migrationBuilder.CreateIndex(
				name: "IX_group_settlements_group_id",
				schema: "grouping",
				table: "group_settlements",
				column: "group_id");

			migrationBuilder.CreateIndex(
				name: "IX_group_settlements_to_user_id",
				schema: "grouping",
				table: "group_settlements",
				column: "to_user_id");

			migrationBuilder.AddForeignKey(
				name: "FK_group_user_admin_user_id",
				schema: "grouping",
				table: "group",
				column: "admin_user_id",
				principalSchema: "user",
				principalTable: "user",
				principalColumn: "_id");

		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.Sql("""
LOCK TABLE grouping."group", grouping.group_user IN ACCESS EXCLUSIVE MODE;
DO $$ BEGIN IF EXISTS(SELECT 1 FROM grouping."group") OR EXISTS(SELECT 1 FROM grouping.group_user)
THEN RAISE EXCEPTION 'AddGroupingModel requires empty groups and members'; END IF; END $$;
""");
			migrationBuilder.DropCheckConstraint(
				name: "c_notification_valid_type",
				schema: "notifications",
				table: "notification");

			migrationBuilder.Sql("UPDATE notifications.notification SET type = 0 WHERE type = 5;");

			migrationBuilder.AddCheckConstraint(
				name: "c_notification_valid_type",
				schema: "notifications",
				table: "notification",
				sql: "type IN (0, 1, 2, 3, 4)");

			migrationBuilder.DropForeignKey(
				name: "FK_group_user_admin_user_id",
				schema: "grouping",
				table: "group");


			migrationBuilder.DropTable(
				name: "group_invitation",
				schema: "grouping");

			migrationBuilder.DropTable(
				name: "group_limit",
				schema: "limits");

			migrationBuilder.DropTable(
				name: "group_settlements",
				schema: "grouping");

			migrationBuilder.DropIndex(
				name: "IX_group_admin_user_id",
				schema: "grouping",
				table: "group");

			migrationBuilder.DropCheckConstraint(
				name: "c_group_currency",
				schema: "grouping",
				table: "group");

			migrationBuilder.DropCheckConstraint(
				name: "c_group_icon_source",
				schema: "grouping",
				table: "group");

			migrationBuilder.DropCheckConstraint(
				name: "c_group_user_total_paid",
				schema: "grouping",
				table: "group_user");

			migrationBuilder.DropCheckConstraint(
				name: "c_group_user_total_settlements_received",
				schema: "grouping",
				table: "group_user");

			migrationBuilder.DropCheckConstraint(
				name: "c_group_user_total_settlements_sent",
				schema: "grouping",
				table: "group_user");

			migrationBuilder.DropCheckConstraint(
				name: "c_group_user_total_share",
				schema: "grouping",
				table: "group_user");

			migrationBuilder.DropColumn(
				name: "admin_user_id",
				schema: "grouping",
				table: "group");

			migrationBuilder.DropColumn(
				name: "currency_code",
				schema: "grouping",
				table: "group");

			migrationBuilder.DropColumn(
				name: "description",
				schema: "grouping",
				table: "group");

			migrationBuilder.DropColumn(
				name: "icon_storage_key",
				schema: "grouping",
				table: "group");

			migrationBuilder.DropColumn(
				name: "is_archived",
				schema: "grouping",
				table: "group");

			migrationBuilder.DropColumn(
				name: "is_member",
				schema: "grouping",
				table: "group_user");

			migrationBuilder.DropColumn(
				name: "joined_at",
				schema: "grouping",
				table: "group_user");

			migrationBuilder.DropColumn(
				name: "total_paid",
				schema: "grouping",
				table: "group_user");

			migrationBuilder.DropColumn(
				name: "total_settlements_received",
				schema: "grouping",
				table: "group_user");

			migrationBuilder.DropColumn(
				name: "total_settlements_sent",
				schema: "grouping",
				table: "group_user");

			migrationBuilder.DropColumn(
				name: "total_share",
				schema: "grouping",
				table: "group_user");

			migrationBuilder.RenameTable(
				name: "group_user",
				schema: "grouping",
				newName: "group_member",
				newSchema: "grouping");

			migrationBuilder.RenameColumn(
				name: "id",
				schema: "grouping",
				table: "group",
				newName: "_id");

			migrationBuilder.RenameColumn(
				name: "group_id",
				schema: "grouping",
				table: "group_member",
				newName: "fk_group_id");

			migrationBuilder.RenameColumn(
				name: "user_id",
				schema: "grouping",
				table: "group_member",
				newName: "fk_user_id");

			migrationBuilder.RenameIndex(
				name: "IX_group_user_group_id",
				schema: "grouping",
				table: "group_member",
				newName: "IX_group_member_fk_group_id");

			migrationBuilder.AddColumn<decimal>(
				name: "monthly_limit",
				schema: "grouping",
				table: "group",
				type: "numeric(18,2)",
				precision: 18,
				scale: 2,
				nullable: true,
				defaultValueSql: "NULL::numeric");

			migrationBuilder.AddColumn<decimal>(
				name: "weekly_limit",
				schema: "grouping",
				table: "group",
				type: "numeric(18,2)",
				precision: 18,
				scale: 2,
				nullable: true,
				defaultValueSql: "NULL::numeric");

			migrationBuilder.AddColumn<decimal>(
				name: "year_limit",
				schema: "grouping",
				table: "group",
				type: "numeric(18,2)",
				precision: 18,
				scale: 2,
				nullable: true,
				defaultValueSql: "NULL::numeric");

			migrationBuilder.AddColumn<bool>(
				name: "is_admin",
				schema: "grouping",
				table: "group_member",
				type: "boolean",
				nullable: false,
				defaultValue: false);
		}
	}
}
