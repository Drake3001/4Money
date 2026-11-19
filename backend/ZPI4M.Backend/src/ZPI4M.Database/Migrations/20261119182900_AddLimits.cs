using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZPI4M.Database.Migrations
{
	/// <inheritdoc />
	public partial class AddLimits : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.Sql("""
                LOCK TABLE limits.group_limit, limits.category_group_limit IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM limits.group_limit) OR EXISTS (SELECT 1 FROM limits.category_group_limit) THEN
                        RAISE EXCEPTION 'AddLimits requires empty Limits tables. No data was removed.';
                    END IF;
                END $$;
                """);

			migrationBuilder.DropTable(
				name: "category_group_limit",
				schema: "limits");

			migrationBuilder.DropIndex(
				name: "IX_group_limit_group_id_period",
				schema: "limits",
				table: "group_limit");

			migrationBuilder.DropCheckConstraint(
				name: "c_group_limit_period",
				schema: "limits",
				table: "group_limit");

			migrationBuilder.AddColumn<Guid>(
				name: "category_id",
				schema: "limits",
				table: "group_limit",
				type: "uuid",
				nullable: true);

			migrationBuilder.AddColumn<string>(
				name: "currency_code",
				schema: "limits",
				table: "group_limit",
				type: "character varying(3)",
				maxLength: 3,
				nullable: false);

			migrationBuilder.AddColumn<DateOnly>(
				name: "end_date",
				schema: "limits",
				table: "group_limit",
				type: "date",
				nullable: true);

			migrationBuilder.AddColumn<bool>(
				name: "is_enabled",
				schema: "limits",
				table: "group_limit",
				type: "boolean",
				nullable: false,
				defaultValue: true);

			migrationBuilder.AddColumn<DateTime>(
				name: "set_date",
				schema: "limits",
				table: "group_limit",
				type: "timestamp with time zone",
				nullable: false,
				defaultValueSql: "CURRENT_TIMESTAMP");

			migrationBuilder.AddColumn<DateOnly>(
				name: "start_date",
				schema: "limits",
				table: "group_limit",
				type: "date",
				nullable: true);

			migrationBuilder.AddColumn<Guid>(
				name: "version",
				schema: "limits",
				table: "group_limit",
				type: "uuid",
				nullable: false,
				defaultValueSql: "gen_random_uuid()");

			migrationBuilder.CreateTable(
				name: "group_limit_alert_state",
				schema: "limits",
				columns: table => new
				{
					limit_id = table.Column<Guid>(type: "uuid", nullable: false),
					period_start_date = table.Column<DateOnly>(type: "date", nullable: false),
					approaching_handled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
					exceeded_notified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_group_limit_alert_state", x => new { x.limit_id, x.period_start_date });
					table.ForeignKey(
						name: "FK_group_limit_alert_state_group_limit_limit_id",
						column: x => x.limit_id,
						principalSchema: "limits",
						principalTable: "group_limit",
						principalColumn: "id");
				});

			migrationBuilder.CreateTable(
				name: "user_limit",
				schema: "limits",
				columns: table => new
				{
					id = table.Column<Guid>(type: "uuid", nullable: false),
					user_id = table.Column<Guid>(type: "uuid", nullable: false),
					category_id = table.Column<Guid>(type: "uuid", nullable: true),
					amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
					currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
					period = table.Column<int>(type: "integer", nullable: false),
					start_date = table.Column<DateOnly>(type: "date", nullable: true),
					end_date = table.Column<DateOnly>(type: "date", nullable: true),
					set_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
					is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
					version = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()")
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_user_limit", x => x.id);
					table.CheckConstraint("c_user_limit_amount", "amount > 0");
					table.CheckConstraint("c_user_limit_currency", "currency_code = 'PLN'");
					table.CheckConstraint("c_user_limit_dates", "(period IN (1,2,3) AND start_date IS NULL AND end_date IS NULL) OR (period = 4 AND start_date IS NOT NULL AND end_date IS NOT NULL AND isfinite(start_date) AND isfinite(end_date) AND start_date <= end_date)");
					table.CheckConstraint("c_user_limit_period", "period IN (1,2,3,4)");
					table.ForeignKey(
						name: "FK_user_limit_category_category_id",
						column: x => x.category_id,
						principalSchema: "expense",
						principalTable: "category",
						principalColumn: "_id");
					table.ForeignKey(
						name: "FK_user_limit_user_user_id",
						column: x => x.user_id,
						principalSchema: "user",
						principalTable: "user",
						principalColumn: "_id");
				});

			migrationBuilder.CreateTable(
				name: "user_limit_alert_state",
				schema: "limits",
				columns: table => new
				{
					limit_id = table.Column<Guid>(type: "uuid", nullable: false),
					period_start_date = table.Column<DateOnly>(type: "date", nullable: false),
					approaching_handled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
					exceeded_notified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_user_limit_alert_state", x => new { x.limit_id, x.period_start_date });
					table.ForeignKey(
						name: "FK_user_limit_alert_state_user_limit_limit_id",
						column: x => x.limit_id,
						principalSchema: "limits",
						principalTable: "user_limit",
						principalColumn: "id");
				});

			migrationBuilder.CreateIndex(
				name: "IX_group_limit_category_id",
				schema: "limits",
				table: "group_limit",
				column: "category_id");

			migrationBuilder.CreateIndex(
				name: "IX_group_limit_group_id",
				schema: "limits",
				table: "group_limit",
				column: "group_id");

			migrationBuilder.CreateIndex(
				name: "IX_group_limit_is_enabled_id",
				schema: "limits",
				table: "group_limit",
				columns: new[] { "is_enabled", "id" });

			migrationBuilder.AddCheckConstraint(
				name: "c_group_limit_currency",
				schema: "limits",
				table: "group_limit",
				sql: "currency_code = 'PLN'");

			migrationBuilder.AddCheckConstraint(
				name: "c_group_limit_dates",
				schema: "limits",
				table: "group_limit",
				sql: "(period IN (1,2,3) AND start_date IS NULL AND end_date IS NULL) OR (period = 4 AND start_date IS NOT NULL AND end_date IS NOT NULL AND isfinite(start_date) AND isfinite(end_date) AND start_date <= end_date)");

			migrationBuilder.AddCheckConstraint(
				name: "c_group_limit_period",
				schema: "limits",
				table: "group_limit",
				sql: "period IN (1,2,3,4)");

			migrationBuilder.CreateIndex(
				name: "IX_user_limit_category_id",
				schema: "limits",
				table: "user_limit",
				column: "category_id");

			migrationBuilder.CreateIndex(
				name: "IX_user_limit_is_enabled_id",
				schema: "limits",
				table: "user_limit",
				columns: new[] { "is_enabled", "id" });

			migrationBuilder.CreateIndex(
				name: "IX_user_limit_user_id",
				schema: "limits",
				table: "user_limit",
				column: "user_id");

			migrationBuilder.AddForeignKey(
				name: "FK_group_limit_category_category_id",
				schema: "limits",
				table: "group_limit",
				column: "category_id",
				principalSchema: "expense",
				principalTable: "category",
				principalColumn: "_id");
			migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS btree_gist;
                ALTER TABLE limits.user_limit ADD CONSTRAINT ex_user_limit_enabled_general
                    EXCLUDE USING gist (user_id WITH =, currency_code WITH =, daterange(start_date,end_date,'[]') WITH &&)
                    WHERE (is_enabled AND period IN (1,2,3) AND category_id IS NULL);
                ALTER TABLE limits.user_limit ADD CONSTRAINT ex_user_limit_enabled_category
                    EXCLUDE USING gist (user_id WITH =, category_id WITH =, currency_code WITH =, daterange(start_date,end_date,'[]') WITH &&)
                    WHERE (is_enabled AND period IN (1,2,3) AND category_id IS NOT NULL);
                ALTER TABLE limits.group_limit ADD CONSTRAINT ex_group_limit_enabled_general
                    EXCLUDE USING gist (group_id WITH =, currency_code WITH =, daterange(start_date,end_date,'[]') WITH &&)
                    WHERE (is_enabled AND period IN (1,2,3) AND category_id IS NULL);
                ALTER TABLE limits.group_limit ADD CONSTRAINT ex_group_limit_enabled_category
                    EXCLUDE USING gist (group_id WITH =, category_id WITH =, currency_code WITH =, daterange(start_date,end_date,'[]') WITH &&)
                    WHERE (is_enabled AND period IN (1,2,3) AND category_id IS NOT NULL);
                """);
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.Sql("""
                LOCK TABLE limits.group_limit, limits.user_limit, limits.group_limit_alert_state, limits.user_limit_alert_state IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM limits.group_limit) OR EXISTS (SELECT 1 FROM limits.user_limit) OR EXISTS (SELECT 1 FROM limits.group_limit_alert_state) OR EXISTS (SELECT 1 FROM limits.user_limit_alert_state) THEN
                        RAISE EXCEPTION 'AddLimits requires empty Limits tables. No data was removed.';
                    END IF;
                END $$;
                """);

			// The extension may be shared by unrelated tables and is retained.
			migrationBuilder.Sql("""
                ALTER TABLE limits.group_limit DROP CONSTRAINT ex_group_limit_enabled_general, DROP CONSTRAINT ex_group_limit_enabled_category;
                """);

			migrationBuilder.DropForeignKey(
				name: "FK_group_limit_category_category_id",
				schema: "limits",
				table: "group_limit");

			migrationBuilder.DropTable(
				name: "group_limit_alert_state",
				schema: "limits");

			migrationBuilder.DropTable(
				name: "user_limit_alert_state",
				schema: "limits");

			migrationBuilder.DropTable(
				name: "user_limit",
				schema: "limits");

			migrationBuilder.DropIndex(
				name: "IX_group_limit_category_id",
				schema: "limits",
				table: "group_limit");

			migrationBuilder.DropIndex(
				name: "IX_group_limit_group_id",
				schema: "limits",
				table: "group_limit");

			migrationBuilder.DropIndex(
				name: "IX_group_limit_is_enabled_id",
				schema: "limits",
				table: "group_limit");

			migrationBuilder.DropCheckConstraint(
				name: "c_group_limit_currency",
				schema: "limits",
				table: "group_limit");

			migrationBuilder.DropCheckConstraint(
				name: "c_group_limit_dates",
				schema: "limits",
				table: "group_limit");

			migrationBuilder.DropCheckConstraint(
				name: "c_group_limit_period",
				schema: "limits",
				table: "group_limit");

			migrationBuilder.DropColumn(
				name: "category_id",
				schema: "limits",
				table: "group_limit");

			migrationBuilder.DropColumn(
				name: "currency_code",
				schema: "limits",
				table: "group_limit");

			migrationBuilder.DropColumn(
				name: "end_date",
				schema: "limits",
				table: "group_limit");

			migrationBuilder.DropColumn(
				name: "is_enabled",
				schema: "limits",
				table: "group_limit");

			migrationBuilder.DropColumn(
				name: "set_date",
				schema: "limits",
				table: "group_limit");

			migrationBuilder.DropColumn(
				name: "start_date",
				schema: "limits",
				table: "group_limit");

			migrationBuilder.DropColumn(
				name: "version",
				schema: "limits",
				table: "group_limit");

			migrationBuilder.CreateTable(
				name: "category_group_limit",
				schema: "limits",
				columns: table => new
				{
					fk_category_id = table.Column<Guid>(type: "uuid", nullable: false),
					fk_group_id = table.Column<Guid>(type: "uuid", nullable: false),
					monthly_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true, defaultValueSql: "NULL::numeric"),
					weekly_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true, defaultValueSql: "NULL::numeric"),
					year_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true, defaultValueSql: "NULL::numeric")
				},
				constraints: table =>
				{
					table.PrimaryKey("category_group_limit_pkey", x => new { x.fk_category_id, x.fk_group_id });
					// These constraints came from SQL init and were absent from the previous EF snapshot.
					table.CheckConstraint("c_category_group_limit_weekly_limit_above_zero", "weekly_limit > 0");
					table.CheckConstraint("c_category_group_limit_monthly_limit_above_zero", "monthly_limit > 0");
					table.CheckConstraint("c_category_group_limit_year_limit_above_zero", "year_limit > 0");
					table.ForeignKey(
						name: "c_category_group_limit_fk_category_id",
						column: x => x.fk_category_id,
						principalSchema: "expense",
						principalTable: "category",
						principalColumn: "_id");
					table.ForeignKey(
						name: "c_category_group_limit_fk_group_id",
						column: x => x.fk_group_id,
						principalSchema: "grouping",
						principalTable: "group",
						principalColumn: "id");
				});

			migrationBuilder.CreateIndex(
				name: "IX_group_limit_group_id_period",
				schema: "limits",
				table: "group_limit",
				columns: new[] { "group_id", "period" },
				unique: true);

			migrationBuilder.AddCheckConstraint(
				name: "c_group_limit_period",
				schema: "limits",
				table: "group_limit",
				sql: "period IN (1, 2, 3)");
		}
	}
}
