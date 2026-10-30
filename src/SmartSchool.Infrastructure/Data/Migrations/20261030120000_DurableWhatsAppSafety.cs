using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSchool.Infrastructure.Data.Migrations;

public partial class DurableWhatsAppSafety : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_WhatsAppLogs_Status",
            table: "WhatsAppLogs");

        migrationBuilder.AddColumn<string>(
            name: "FailureCode",
            table: "WhatsAppLogs",
            type: "nvarchar(40)",
            maxLength: 40,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "NextAttemptAtUtc",
            table: "WhatsAppLogs",
            type: "datetime2",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "RetryWindowStartedAtUtc",
            table: "WhatsAppLogs",
            type: "datetime2",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_WhatsAppLogs_Status_NextAttemptAtUtc",
            table: "WhatsAppLogs",
            columns: new[] { "Status", "NextAttemptAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_WhatsAppLogs_Status_NextAttemptAtUtc",
            table: "WhatsAppLogs");

        migrationBuilder.DropColumn(
            name: "FailureCode",
            table: "WhatsAppLogs");

        migrationBuilder.DropColumn(
            name: "NextAttemptAtUtc",
            table: "WhatsAppLogs");

        migrationBuilder.DropColumn(
            name: "RetryWindowStartedAtUtc",
            table: "WhatsAppLogs");

        migrationBuilder.CreateIndex(
            name: "IX_WhatsAppLogs_Status",
            table: "WhatsAppLogs",
            column: "Status");
    }
}
