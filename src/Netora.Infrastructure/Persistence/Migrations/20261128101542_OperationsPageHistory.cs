using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Netora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperationsPageHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalEmailDeliveries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<long>(type: "bigint", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalEmailDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalEmailDeliveries_ApprovalRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "ApprovalRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BackupRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Scheduled = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalEmailDeliveries_OccurredAtUtc",
                table: "ApprovalEmailDeliveries",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalEmailDeliveries_RequestId",
                table: "ApprovalEmailDeliveries",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupRuns_Scheduled_StartedAtUtc",
                table: "BackupRuns",
                columns: new[] { "Scheduled", "StartedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalEmailDeliveries");

            migrationBuilder.DropTable(
                name: "BackupRuns");
        }
    }
}
