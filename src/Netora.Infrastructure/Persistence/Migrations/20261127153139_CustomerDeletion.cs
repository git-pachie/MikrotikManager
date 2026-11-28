using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Netora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAtUtc",
                table: "Customers",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "Customers");
        }
    }
}
