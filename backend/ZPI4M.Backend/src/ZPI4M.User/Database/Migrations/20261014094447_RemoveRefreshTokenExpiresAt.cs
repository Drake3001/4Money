using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZPI4M.User.Database.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRefreshTokenExpiresAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RefreshTokenExpiryDate",
                schema: "user",
                table: "user");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RefreshTokenExpiryDate",
                schema: "user",
                table: "user",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
