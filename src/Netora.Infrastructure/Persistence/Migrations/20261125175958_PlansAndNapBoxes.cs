using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Netora.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Modules 05 and 06: Internet Plans and NAP Boxes become tables, and customers link to them by ID.
    /// Existing text values are kept: every plan name in use (on customers or in Change Plan requests) becomes a plan
    /// whose PPP profile has the same name, as provisioning did before; every NAP code in use becomes a NAP box, plus the
    /// built-in UNKNOWN. Fees, speeds, areas and descriptions stay empty until a Manager fills them in.
    /// </summary>
    public partial class PlansAndNapBoxes : Migration
    {
        private const string NewStamp = "REPLACE(CONVERT(nvarchar(36), NEWID()), '-', '')";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InternetPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MonthlyFee = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    DownloadMbps = table.Column<int>(type: "int", nullable: true),
                    UploadMbps = table.Column<int>(type: "int", nullable: true),
                    RouterProfile = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternetPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NapBoxes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Area = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NapBoxes", x => x.Id);
                });

            migrationBuilder.CreateIndex(name: "IX_InternetPlans_Name", table: "InternetPlans", column: "Name", unique: true);
            migrationBuilder.CreateIndex(name: "IX_NapBoxes_Code", table: "NapBoxes", column: "Code", unique: true);

            migrationBuilder.AddColumn<int>(name: "PlanId", table: "Customers", type: "int", nullable: true);
            migrationBuilder.AddColumn<int>(name: "NapBoxId", table: "Customers", type: "int", nullable: true);
            migrationBuilder.AddColumn<int>(name: "TargetPlanId", table: "ApprovalRequests", type: "int", nullable: true);

            // Plans from the names in use. The database collation is case-insensitive, like the unique index.
            migrationBuilder.Sql($"""
                INSERT INTO InternetPlans (Name, RouterProfile, IsActive, CreatedAtUtc, ConcurrencyStamp)
                SELECT x.Name, x.Name, 1, SYSDATETIMEOFFSET(), {NewStamp}
                FROM (SELECT PlanName AS Name FROM Customers
                      UNION SELECT TargetPlanName FROM ApprovalRequests WHERE TargetPlanName IS NOT NULL) x
                WHERE LTRIM(RTRIM(x.Name)) <> '';
                """);
            migrationBuilder.Sql($"""
                INSERT INTO NapBoxes (Code, IsActive, IsSystem, CreatedAtUtc, ConcurrencyStamp)
                VALUES ('UNKNOWN', 1, 1, SYSDATETIMEOFFSET(), {NewStamp});
                INSERT INTO NapBoxes (Code, IsActive, IsSystem, CreatedAtUtc, ConcurrencyStamp)
                SELECT x.Code, 1, 0, SYSDATETIMEOFFSET(), {NewStamp}
                FROM (SELECT DISTINCT NapBox AS Code FROM Customers) x
                WHERE x.Code <> 'UNKNOWN' AND LTRIM(RTRIM(x.Code)) <> '';
                """);
            migrationBuilder.Sql("""
                UPDATE c SET PlanId = p.Id FROM Customers c JOIN InternetPlans p ON p.Name = c.PlanName;
                UPDATE c SET NapBoxId = n.Id FROM Customers c JOIN NapBoxes n ON n.Code = c.NapBox;
                UPDATE Customers SET NapBoxId = (SELECT Id FROM NapBoxes WHERE Code = 'UNKNOWN') WHERE NapBoxId IS NULL;
                UPDATE r SET TargetPlanId = p.Id FROM ApprovalRequests r JOIN InternetPlans p ON p.Name = r.TargetPlanName;
                """);

            migrationBuilder.AlterColumn<int>(name: "PlanId", table: "Customers", type: "int", nullable: false,
                oldClrType: typeof(int), oldType: "int", oldNullable: true);
            migrationBuilder.AlterColumn<int>(name: "NapBoxId", table: "Customers", type: "int", nullable: false,
                oldClrType: typeof(int), oldType: "int", oldNullable: true);

            migrationBuilder.DropColumn(name: "NapBox", table: "Customers");
            migrationBuilder.DropColumn(name: "PlanName", table: "Customers");

            migrationBuilder.CreateIndex(name: "IX_Customers_NapBoxId", table: "Customers", column: "NapBoxId");
            migrationBuilder.CreateIndex(name: "IX_Customers_PlanId", table: "Customers", column: "PlanId");
            migrationBuilder.CreateIndex(name: "IX_ApprovalRequests_TargetPlanId", table: "ApprovalRequests", column: "TargetPlanId");

            migrationBuilder.AddForeignKey(
                name: "FK_ApprovalRequests_InternetPlans_TargetPlanId",
                table: "ApprovalRequests",
                column: "TargetPlanId",
                principalTable: "InternetPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "FK_Customers_InternetPlans_PlanId",
                table: "Customers",
                column: "PlanId",
                principalTable: "InternetPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "FK_Customers_NapBoxes_NapBoxId",
                table: "Customers",
                column: "NapBoxId",
                principalTable: "NapBoxes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to text columns, filled from the linked rows. Plan details (fee, speeds, profile) are lost.
            migrationBuilder.AddColumn<string>(name: "PlanName", table: "Customers", type: "nvarchar(100)", maxLength: 100, nullable: true);
            migrationBuilder.AddColumn<string>(name: "NapBox", table: "Customers", type: "nvarchar(50)", maxLength: 50, nullable: true);
            migrationBuilder.Sql("""
                UPDATE c SET PlanName = p.Name FROM Customers c JOIN InternetPlans p ON p.Id = c.PlanId;
                UPDATE c SET NapBox = n.Code FROM Customers c JOIN NapBoxes n ON n.Id = c.NapBoxId;
                """);
            migrationBuilder.AlterColumn<string>(name: "PlanName", table: "Customers", type: "nvarchar(100)", maxLength: 100, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(100)", oldMaxLength: 100, oldNullable: true);
            migrationBuilder.AlterColumn<string>(name: "NapBox", table: "Customers", type: "nvarchar(50)", maxLength: 50, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(50)", oldMaxLength: 50, oldNullable: true);

            migrationBuilder.DropForeignKey(name: "FK_ApprovalRequests_InternetPlans_TargetPlanId", table: "ApprovalRequests");
            migrationBuilder.DropForeignKey(name: "FK_Customers_InternetPlans_PlanId", table: "Customers");
            migrationBuilder.DropForeignKey(name: "FK_Customers_NapBoxes_NapBoxId", table: "Customers");
            migrationBuilder.DropIndex(name: "IX_Customers_NapBoxId", table: "Customers");
            migrationBuilder.DropIndex(name: "IX_Customers_PlanId", table: "Customers");
            migrationBuilder.DropIndex(name: "IX_ApprovalRequests_TargetPlanId", table: "ApprovalRequests");
            migrationBuilder.DropColumn(name: "NapBoxId", table: "Customers");
            migrationBuilder.DropColumn(name: "PlanId", table: "Customers");
            migrationBuilder.DropColumn(name: "TargetPlanId", table: "ApprovalRequests");
            migrationBuilder.DropTable(name: "InternetPlans");
            migrationBuilder.DropTable(name: "NapBoxes");
        }
    }
}
