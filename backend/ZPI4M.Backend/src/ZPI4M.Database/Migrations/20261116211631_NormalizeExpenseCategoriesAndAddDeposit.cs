using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZPI4M.Database.Migrations
{
	/// <inheritdoc />
	public partial class NormalizeExpenseCategoriesAndAddDeposit : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000001"),
				column: "name",
				value: "Żywność i napoje");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000002"),
				column: "name",
				value: "Restauracje i kawiarnie");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000003"),
				column: "name",
				value: "Transport i samochód");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000004"),
				column: "name",
				value: "Rachunki i opłaty");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000006"),
				column: "name",
				value: "Alkohol i tytoń");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000007"),
				column: "name",
				value: "Ubrania i dodatki");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000008"),
				column: "name",
				value: "Elektronika");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000009"),
				column: "name",
				value: "Dom i ogród");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000010"),
				column: "name",
				value: "Chemia gospodarcza");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000011"),
				column: "name",
				value: "Kosmetyki i higiena");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000012"),
				column: "name",
				value: "Zdrowie");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000013"),
				column: "name",
				value: "Zwierzęta");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000014"),
				column: "name",
				value: "Podróże i noclegi");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000015"),
				column: "name",
				value: "Rozrywka i wydarzenia");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000016"),
				column: "name",
				value: "Sport i hobby");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000017"),
				column: "name",
				value: "Edukacja");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000018"),
				column: "name",
				value: "Usługi");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000019"),
				column: "name",
				value: "Inne");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000020"),
				column: "name",
				value: "Zniżka");

			migrationBuilder.InsertData(
				schema: "expense",
				table: "category",
				columns: new[] { "_id", "description", "icon", "name" },
				values: new object[] { new Guid("30000000-0000-0000-0000-000000000021"), "Kaucje za opakowania i ich zwroty.", "recycle", "Kaucja" });
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DeleteData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000021"));

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000001"),
				column: "name",
				value: "żywność i napoje");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000002"),
				column: "name",
				value: "restauracje i kawiarnie");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000003"),
				column: "name",
				value: "transport i samochód");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000004"),
				column: "name",
				value: "rachunki i opłaty");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000006"),
				column: "name",
				value: "alkohol i tytoń");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000007"),
				column: "name",
				value: "ubrania i dodatki");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000008"),
				column: "name",
				value: "elektronika");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000009"),
				column: "name",
				value: "dom i ogród");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000010"),
				column: "name",
				value: "chemia gospodarcza");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000011"),
				column: "name",
				value: "kosmetyki i higiena");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000012"),
				column: "name",
				value: "zdrowie");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000013"),
				column: "name",
				value: "zwierzęta");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000014"),
				column: "name",
				value: "podróże i noclegi");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000015"),
				column: "name",
				value: "rozrywka i wydarzenia");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000016"),
				column: "name",
				value: "sport i hobby");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000017"),
				column: "name",
				value: "edukacja");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000018"),
				column: "name",
				value: "usługi");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000019"),
				column: "name",
				value: "inne");

			migrationBuilder.UpdateData(
				schema: "expense",
				table: "category",
				keyColumn: "_id",
				keyValue: new Guid("30000000-0000-0000-0000-000000000020"),
				column: "name",
				value: "zniżka");
		}
	}
}
