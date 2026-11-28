using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Netora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReuseArchivedPppoeUsername : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_PppoeUsername",
                table: "Customers");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_PppoeUsername",
                table: "Customers",
                column: "PppoeUsername",
                unique: true,
                filter: "[DeletedAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_PppoeUsername",
                table: "Customers");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_PppoeUsername",
                table: "Customers",
                column: "PppoeUsername",
                unique: true);
        }
    }
}
