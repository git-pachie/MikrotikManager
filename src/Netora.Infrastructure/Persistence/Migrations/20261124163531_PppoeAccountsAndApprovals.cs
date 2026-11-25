using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Netora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PppoeAccountsAndApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PppoeComment",
                table: "Customers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PppoeLocalAddress",
                table: "Customers",
                type: "nvarchar(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PppoePasswordProtected",
                table: "Customers",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PppoeRemoteAddress",
                table: "Customers",
                type: "nvarchar(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RouterSyncStatus",
                table: "Customers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotSent");  // existing customers: nothing sent to a router yet

            migrationBuilder.CreateTable(
                name: "ApprovalRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RequestedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    DecidedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalRequests_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_OnePendingPerCustomer",
                table: "ApprovalRequests",
                column: "CustomerId",
                unique: true,
                filter: "[Status] = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_Status_RequestedAtUtc",
                table: "ApprovalRequests",
                columns: new[] { "Status", "RequestedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "PppoeComment",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PppoeLocalAddress",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PppoePasswordProtected",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PppoeRemoteAddress",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "RouterSyncStatus",
                table: "Customers");
        }
    }
}
