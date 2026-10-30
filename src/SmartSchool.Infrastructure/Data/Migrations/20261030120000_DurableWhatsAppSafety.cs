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

        // Never release historical rows from the retired QR/Twilio queues during
        // upgrade. Ambiguous handoffs remain blocked as DeliveryUnknown; all other
        // unfinished legacy rows require a fresh attendance event.
        migrationBuilder.Sql(
            "UPDATE [WhatsAppLogs] SET [Status] = N'Failed', [FailureCode] = N'DeliveryUnknown', " +
            "[Error] = N'Legacy provider handoff outcome is unknown; automatic/manual replay is blocked.', [NextAttemptAtUtc] = NULL " +
            "WHERE [Status] IN (N'Sending', N'Submitted', N'Unknown');");
        migrationBuilder.Sql(
            "UPDATE [WhatsAppLogs] SET [Status] = N'Failed', [FailureCode] = N'legacy_not_replayed', " +
            "[Error] = N'Historical queued/held alert was not replayed during the provider upgrade.', [NextAttemptAtUtc] = NULL " +
            "WHERE [Status] IN (N'Queued', N'PendingQr', N'Held', N'Expired');");

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
