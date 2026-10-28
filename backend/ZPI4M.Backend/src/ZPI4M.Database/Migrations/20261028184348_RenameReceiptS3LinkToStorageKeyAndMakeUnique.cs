using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZPI4M.Database.Migrations
{
	/// <inheritdoc />
	public partial class RenameReceiptS3LinkToStorageKeyAndMakeUnique : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.RenameColumn(
				name: "s3_link",
				schema: "receipt",
				table: "receipt",
				newName: "storage_key");

			migrationBuilder.CreateIndex(
				name: "ux_receipt_storage_key",
				schema: "receipt",
				table: "receipt",
				column: "storage_key",
				unique: true);
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropIndex(
				name: "ux_receipt_storage_key",
				schema: "receipt",
				table: "receipt");

			migrationBuilder.RenameColumn(
				name: "storage_key",
				schema: "receipt",
				table: "receipt",
				newName: "s3_link");
		}
	}
}
