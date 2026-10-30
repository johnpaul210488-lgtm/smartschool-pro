using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace SmartSchool.Infrastructure.Data.Migrations;

[DbContext(typeof(SchoolDbContext))]
[Migration("20261030000000_WhatsAppExplicitConsent")]
public partial class WhatsAppExplicitConsent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WhatsAppConsents",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                StudentId = table.Column<int>(type: "int", nullable: false),
                PhoneE164 = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                Granted = table.Column<bool>(type: "bit", nullable: false),
                ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                ChangedByUserId = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WhatsAppConsents", x => x.Id);
                table.ForeignKey("FK_WhatsAppConsents_Students_StudentId", x => x.StudentId,
                    "Students", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_WhatsAppConsents_StudentId_PhoneE164", "WhatsAppConsents",
            new[] { "StudentId", "PhoneE164" }, unique: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("WhatsAppConsents");
}
