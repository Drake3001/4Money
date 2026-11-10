using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZPI4M.Database.Migrations
{
	/// <inheritdoc />
	public partial class UserImprovements : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			// SQL init creates a UNIQUE constraint; EF-created schemas may have only an index.
			migrationBuilder.Sql("""
                ALTER TABLE "user"."user" DROP CONSTRAINT IF EXISTS c_user_unique_email;
                DROP INDEX IF EXISTS "user".c_user_unique_email;
                """);

			migrationBuilder.CreateIndex(
				name: "c_user_unique_email",
				schema: "user",
				table: "user",
				column: "email",
				unique: true,
				filter: "is_active = true");

			migrationBuilder.Sql("""
                CREATE FUNCTION grouping.create_user_starter_group()
                RETURNS trigger
                LANGUAGE plpgsql
                SET search_path = pg_catalog
                AS $function$
                DECLARE
                    starter_group_id uuid := gen_random_uuid();
                BEGIN
                    INSERT INTO grouping."group" (id, name, admin_user_id, currency_code, is_archived)
                    VALUES (starter_group_id, 'Wydatki osobiste', NEW._id, 'PLN', false);

                    INSERT INTO grouping.group_user
                        (user_id, group_id, joined_at, is_member,
                         total_paid, total_share, total_settlements_sent, total_settlements_received)
                    VALUES (NEW._id, starter_group_id, CURRENT_TIMESTAMP, true, 0, 0, 0, 0);

                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER create_user_starter_group
                AFTER INSERT ON "user"."user"
                FOR EACH ROW EXECUTE FUNCTION grouping.create_user_starter_group();
                """);

			migrationBuilder.AlterColumn<string>(
				name: "password_hash",
				schema: "user",
				table: "user",
				type: "text",
				nullable: true,
				oldClrType: typeof(string),
				oldType: "text");

			migrationBuilder.AddColumn<string>(
				name: "google_subject",
				schema: "user",
				table: "user",
				type: "text",
				nullable: true);

			migrationBuilder.CreateIndex(
				name: "c_user_unique_google_subject",
				schema: "user",
				table: "user",
				column: "google_subject",
				unique: true);

			migrationBuilder.AddCheckConstraint(
				name: "c_user_authentication_method",
				schema: "user",
				table: "user",
				sql: "password_hash IS NOT NULL OR google_subject IS NOT NULL");

			migrationBuilder.AddColumn<string>(
				name: "icon_storage_key",
				schema: "user",
				table: "user",
				type: "text",
				nullable: true);

			migrationBuilder.AddCheckConstraint(
				name: "c_user_icon_source",
				schema: "user",
				table: "user",
				sql: "icon IS NULL OR icon_storage_key IS NULL");
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.Sql("""
                LOCK TABLE "user"."user" IN ACCESS EXCLUSIVE MODE;
                DO $guard$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "user"."user" WHERE password_hash IS NULL) THEN
                        RAISE EXCEPTION 'UserImprovements cannot be reverted while users without local passwords exist.';
                    END IF;
                END;
                $guard$;
                """);

			migrationBuilder.DropCheckConstraint(
				name: "c_user_icon_source",
				schema: "user",
				table: "user");

			migrationBuilder.DropColumn(
				name: "icon_storage_key",
				schema: "user",
				table: "user");

			migrationBuilder.DropIndex(
				name: "c_user_unique_google_subject",
				schema: "user",
				table: "user");

			migrationBuilder.DropCheckConstraint(
				name: "c_user_authentication_method",
				schema: "user",
				table: "user");

			migrationBuilder.DropColumn(
				name: "google_subject",
				schema: "user",
				table: "user");

			migrationBuilder.AlterColumn<string>(
				name: "password_hash",
				schema: "user",
				table: "user",
				type: "text",
				nullable: false,
				oldClrType: typeof(string),
				oldType: "text",
				oldNullable: true);

			migrationBuilder.Sql("""
                DROP TRIGGER create_user_starter_group ON "user"."user";
                DROP FUNCTION grouping.create_user_starter_group();
                """);

			migrationBuilder.DropIndex(
				name: "c_user_unique_email",
				schema: "user",
				table: "user");

			migrationBuilder.CreateIndex(
				name: "c_user_unique_email",
				schema: "user",
				table: "user",
				column: "email",
				unique: true);
		}
	}
}
