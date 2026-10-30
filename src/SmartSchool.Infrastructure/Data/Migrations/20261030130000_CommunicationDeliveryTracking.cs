using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSchool.Infrastructure.Data.Migrations;

/// <summary>
/// Links each WhatsApp outbox row to the exact attendance event that created it,
/// so a delayed/restarted send can still update the attendance delivery audit.
/// </summary>
public partial class CommunicationDeliveryTracking : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "BusAttendanceId",
            table: "WhatsAppLogs",
            type: "bigint",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_WhatsAppLogs_BusAttendanceId",
            table: "WhatsAppLogs",
            column: "BusAttendanceId");

        migrationBuilder.AddForeignKey(
            name: "FK_WhatsAppLogs_BusAttendance_BusAttendanceId",
            table: "WhatsAppLogs",
            column: "BusAttendanceId",
            principalTable: "BusAttendance",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_WhatsAppLogs_BusAttendance_BusAttendanceId",
            table: "WhatsAppLogs");

        migrationBuilder.DropIndex(
            name: "IX_WhatsAppLogs_BusAttendanceId",
            table: "WhatsAppLogs");

        migrationBuilder.DropColumn(
            name: "BusAttendanceId",
            table: "WhatsAppLogs");
    }
}
