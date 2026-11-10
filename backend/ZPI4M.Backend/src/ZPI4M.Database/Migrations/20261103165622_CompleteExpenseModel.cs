using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ZPI4M.Database.Migrations
{
	/// <inheritdoc />
	public partial class CompleteExpenseModel : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			// SQL init contains constraints that were absent from the EF snapshot.
			// This squashed migration targets an empty Expense schema; no payer is inferred.
			migrationBuilder.Sql("""
                LOCK TABLE expense.expense, expense.expense_item, expense.expense_share IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM expense.expense) OR EXISTS (SELECT 1 FROM expense.expense_item)
                        OR EXISTS (SELECT 1 FROM expense.expense_share) THEN
                        RAISE EXCEPTION 'CompleteExpenseModel requires empty expenses, items and shares';
                    END IF;
                END $$;
                """);
			migrationBuilder.DropCheckConstraint(
				name: "c_expense_item_unit_price_above_zero", schema: "expense", table: "expense_item");

			migrationBuilder.DropColumn(
				name: "fk_user_id",
				schema: "expense",
				table: "expense_item");

			migrationBuilder.DropColumn(
				name: "color",
				schema: "expense",
				table: "category");

			migrationBuilder.AddColumn<decimal>(
				name: "discount",
				schema: "expense",
				table: "expense_item",
				type: "numeric(18,2)",
				precision: 18,
				scale: 2,
				nullable: false,
				defaultValue: 0m);

			migrationBuilder.AddColumn<bool>(
				name: "is_system_generated",
				schema: "expense",
				table: "expense_item",
				type: "boolean",
				nullable: false,
				defaultValue: false);

			migrationBuilder.AddColumn<string>(
				name: "unit",
				schema: "expense",
				table: "expense_item",
				type: "text",
				nullable: true);

			migrationBuilder.AddColumn<Guid>(
				name: "fk_payer_id",
				schema: "expense",
				table: "expense",
				type: "uuid",
				nullable: false);

			migrationBuilder.AddColumn<Guid>(
				name: "version",
				schema: "expense",
				table: "expense",
				type: "uuid",
				nullable: false,
				defaultValueSql: "gen_random_uuid()");

			migrationBuilder.InsertData(
				schema: "expense",
				table: "category",
				columns: new[] { "_id", "description", "icon", "name" },
				values: new object[,]
				{
					{ new Guid("30000000-0000-0000-0000-000000000001"), "Zakupy spożywcze i napoje bezalkoholowe, również gotowa żywność ze sklepu.", "shopping-cart", "żywność i napoje" },
					{ new Guid("30000000-0000-0000-0000-000000000002"), "Posiłki i napoje bezalkoholowe z gastronomii, także dostawy; wymaga kontekstu gastronomicznego.", "utensils", "restauracje i kawiarnie" },
					{ new Guid("30000000-0000-0000-0000-000000000003"), "Paliwo, części, serwis auta, parking, taxi, komunikacja miejska i przejazdy lądowe.", "car", "transport i samochód" },
					{ new Guid("30000000-0000-0000-0000-000000000004"), "Media, prąd, gaz, woda, telefon, internet i opłaty administracyjne.", "receipt", "rachunki i opłaty" },
					{ new Guid("30000000-0000-0000-0000-000000000005"), "Automatyczne dopełnienie kwoty wydatku", "circle-help", "Nieznane" },
					{ new Guid("30000000-0000-0000-0000-000000000006"), "Napoje alkoholowe, papierosy i produkty nikotynowe, również kupione w restauracji.", "wine", "alkohol i tytoń" },
					{ new Guid("30000000-0000-0000-0000-000000000007"), "Odzież, obuwie, torby, biżuteria i dodatki odzieżowe.", "shirt", "ubrania i dodatki" },
					{ new Guid("30000000-0000-0000-0000-000000000008"), "Sprzęt elektroniczny, przewody, ładowarki i części elektroniczne.", "smartphone", "elektronika" },
					{ new Guid("30000000-0000-0000-0000-000000000009"), "Wyposażenie, meble, narzędzia, rośliny, artykuły kuchenne i ręczniki papierowe.", "home", "dom i ogród" },
					{ new Guid("30000000-0000-0000-0000-000000000010"), "Detergenty i środki czystości, np. płyn do naczyń, proszek do prania, płyn do WC.", "spray-can", "chemia gospodarcza" },
					{ new Guid("30000000-0000-0000-0000-000000000011"), "Pielęgnacja i higiena osobista, np. szampon, pasta do zębów, podpaski, papier toaletowy.", "sparkles", "kosmetyki i higiena" },
					{ new Guid("30000000-0000-0000-0000-000000000012"), "Leki, wyroby medyczne, badania, wizyty lekarskie, dentysta i rehabilitacja.", "heart-pulse", "zdrowie" },
					{ new Guid("30000000-0000-0000-0000-000000000013"), "Karma, akcesoria dla zwierząt, ich pielęgnacja i weterynarz.", "paw-print", "zwierzęta" },
					{ new Guid("30000000-0000-0000-0000-000000000014"), "Hotele, loty, wycieczki i usługi turystyczne; nie zgaduj celu zwykłego przejazdu.", "plane", "podróże i noclegi" },
					{ new Guid("30000000-0000-0000-0000-000000000015"), "Kino, koncerty, gry, abonamenty rozrywkowe i książki rekreacyjne.", "ticket", "rozrywka i wydarzenia" },
					{ new Guid("30000000-0000-0000-0000-000000000016"), "Siłownia, sprzęt sportowy, zajęcia rekreacyjne i materiały hobbystyczne.", "dumbbell", "sport i hobby" },
					{ new Guid("30000000-0000-0000-0000-000000000017"), "Kursy, podręczniki, szkolenia i opłaty za naukę.", "graduation-cap", "edukacja" },
					{ new Guid("30000000-0000-0000-0000-000000000018"), "Fryzjer, barber, krawiec, naprawa obuwia, dorabianie kluczy; tylko usługi bez trafniejszej kategorii.", "wrench", "usługi" },
					{ new Guid("30000000-0000-0000-0000-000000000019"), "Rozpoznany wydatek, dla którego żadna z pozostałych kategorii nie pasuje; nie służy do oznaczania niepewności.", "package", "inne" },
					{ new Guid("30000000-0000-0000-0000-000000000020"), "Samodzielna pozycja obniżająca wartość zakupów, np. opust za butelki lub rabat na cały paragon. Nie obejmuje zwrotu kaucji ani rabatu przypisanego już do produktu.", "badge-percent", "zniżka" }
				});

			migrationBuilder.AddCheckConstraint(
				name: "c_expense_item_discount_nonpositive",
				schema: "expense",
				table: "expense_item",
				sql: "discount <= 0");

			migrationBuilder.CreateIndex(
				name: "ix_expense_fk_payer_id",
				schema: "expense",
				table: "expense",
				column: "fk_payer_id");

			migrationBuilder.AddForeignKey(
				name: "c_expense_fk_payer_id",
				schema: "expense",
				table: "expense",
				column: "fk_payer_id",
				principalSchema: "user",
				principalTable: "user",
				principalColumn: "_id");

		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.Sql("""
                LOCK TABLE expense.expense, expense.expense_item, expense.expense_share IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM expense.expense) OR EXISTS (SELECT 1 FROM expense.expense_item)
                        OR EXISTS (SELECT 1 FROM expense.expense_share) THEN
                        RAISE EXCEPTION 'CompleteExpenseModel downgrade requires empty expenses, items and shares';
                    END IF;
                END $$;
                """);
			migrationBuilder.DropForeignKey(
				name: "c_expense_fk_payer_id", schema: "expense", table: "expense");

			migrationBuilder.DropCheckConstraint(
				name: "c_expense_item_discount_nonpositive",
				schema: "expense",
				table: "expense_item");

			migrationBuilder.DropIndex(
				name: "ix_expense_fk_payer_id",
				schema: "expense",
				table: "expense");

			migrationBuilder.DeleteData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000001"));

			migrationBuilder.DeleteData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000002"));

			migrationBuilder.DeleteData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000003"));

			migrationBuilder.DeleteData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000004"));

			migrationBuilder.DeleteData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000005"));

			migrationBuilder.DeleteData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000006"));

			migrationBuilder.DeleteData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000007"));

			migrationBuilder.DeleteData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000008"));

			migrationBuilder.DeleteData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000009"));

			migrationBuilder.DeleteData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000010"));

			migrationBuilder.DeleteData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000011"));

			migrationBuilder.DeleteData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000012"));

			migrationBuilder.DeleteData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000013"));

			migrationBuilder.DeleteData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000014"));

			migrationBuilder.DeleteData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000015"));

			migrationBuilder.DeleteData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000016"));

			migrationBuilder.DeleteData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000017"));

			migrationBuilder.DeleteData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000018"));

			migrationBuilder.DeleteData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000019"));

			migrationBuilder.DeleteData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000020"));

			migrationBuilder.DropColumn(
				name: "discount",
				schema: "expense",
				table: "expense_item");

			migrationBuilder.DropColumn(
				name: "is_system_generated",
				schema: "expense",
				table: "expense_item");

			migrationBuilder.DropColumn(
				name: "unit",
				schema: "expense",
				table: "expense_item");

			migrationBuilder.DropColumn(
				name: "fk_payer_id",
				schema: "expense",
				table: "expense");

			migrationBuilder.DropColumn(
				name: "version",
				schema: "expense",
				table: "expense");

			migrationBuilder.AddColumn<Guid>(
				name: "fk_user_id",
				schema: "expense",
				table: "expense_item",
				type: "uuid",
				nullable: true);

			migrationBuilder.AddColumn<string>(
				name: "color",
				schema: "expense",
				table: "category",
				type: "text",
				nullable: false,
				defaultValue: "");
			migrationBuilder.AddCheckConstraint(
				name: "c_expense_item_unit_price_above_zero", schema: "expense", table: "expense_item", sql: "unit_price > 0");
			migrationBuilder.AddForeignKey(
				name: "c_expense_item_fk_user_id", schema: "expense", table: "expense_item", column: "fk_user_id",
				principalSchema: "user", principalTable: "user", principalColumn: "_id");
		}
	}
}
