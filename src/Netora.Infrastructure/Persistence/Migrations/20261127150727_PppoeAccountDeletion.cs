using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Netora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PppoeAccountDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PppoeDeletedAtUtc",
                table: "Customers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PppoeDeletionRequestedAtUtc",
                table: "Customers",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PppoeDeletedAtUtc",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PppoeDeletionRequestedAtUtc",
                table: "Customers");
        }
    }
}
