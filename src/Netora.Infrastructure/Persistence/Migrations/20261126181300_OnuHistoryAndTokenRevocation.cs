using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Netora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OnuHistoryAndTokenRevocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OnuReplacements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    PreviousSerial = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PreviousModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NewSerial = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    NewModel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReplacedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OperatorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Operator = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnuReplacements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnuReplacements_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RevokedAccessTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RetainUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevokedAccessTokens", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnuReplacements_CustomerId_ReplacedOn",
                table: "OnuReplacements",
                columns: new[] { "CustomerId", "ReplacedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_OnuReplacements_ReplacedOn_Id",
                table: "OnuReplacements",
                columns: new[] { "ReplacedOn", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_RevokedAccessTokens_RetainUntilUtc",
                table: "RevokedAccessTokens",
                column: "RetainUntilUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OnuReplacements");

            migrationBuilder.DropTable(
                name: "RevokedAccessTokens");
        }
    }
}
