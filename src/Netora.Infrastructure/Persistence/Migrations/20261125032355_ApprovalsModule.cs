using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Netora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApprovalsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "After",
                table: "ApprovalRequests",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Before",
                table: "ApprovalRequests",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProvisioningAttempts",
                table: "ApprovalRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ProvisioningError",
                table: "ApprovalRequests",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProvisioningLastAttemptAtUtc",
                table: "ApprovalRequests",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProvisioningStatus",
                table: "ApprovalRequests",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotApplicable");

            migrationBuilder.AddColumn<string>(
                name: "TargetPlanName",
                table: "ApprovalRequests",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            // Requests approved before this module were never sent to the router.
            migrationBuilder.Sql("UPDATE ApprovalRequests SET ProvisioningStatus = 'NotSent' WHERE Status = 'Approved';");

            // Before/after summaries for the existing disable and enable requests.
            migrationBuilder.Sql("UPDATE ApprovalRequests SET [Before] = 'Enabled', [After] = 'Disabled' WHERE Operation = 'DisablePppoe' AND [Before] IS NULL;");
            migrationBuilder.Sql("UPDATE ApprovalRequests SET [Before] = 'Disabled', [After] = 'Enabled' WHERE Operation = 'EnablePppoe' AND [Before] IS NULL;");

            // Customers already waiting for approval get the creation request that new customers now get on create,
            // so they appear under Pending Approvals. Skipped when the customer already has a pending request.
            migrationBuilder.Sql(@"
INSERT INTO ApprovalRequests (CustomerId, Operation, Status, Reason, [Before], [After], RequestedByUserId, RequestedByUserName,
                              RequestedAtUtc, ProvisioningStatus, ProvisioningAttempts)
SELECT c.Id, 'CreatePppoe', 'Pending', NULL, 'Not created',
       CONCAT('Enabled, username ', c.PppoeUsername, ', plan ', c.PlanName),
       COALESCE(c.CreatedByUserId, ''), COALESCE(u.UserName, 'unknown'), c.CreatedAtUtc, 'NotApplicable', 0
FROM Customers c
LEFT JOIN AspNetUsers u ON u.Id = c.CreatedByUserId
WHERE c.Status = 'PendingApproval'
  AND NOT EXISTS (SELECT 1 FROM ApprovalRequests r WHERE r.CustomerId = c.Id AND r.Status = 'Pending');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Requests of the kinds added by this module (including the backfilled creation requests) cannot exist before it.
            migrationBuilder.Sql("DELETE FROM ApprovalRequests WHERE Operation IN ('CreatePppoe', 'ChangePlan', 'TerminatePppoe');");

            migrationBuilder.DropColumn(
                name: "After",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "Before",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "ProvisioningAttempts",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "ProvisioningError",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "ProvisioningLastAttemptAtUtc",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "ProvisioningStatus",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "TargetPlanName",
                table: "ApprovalRequests");
        }
    }
}
