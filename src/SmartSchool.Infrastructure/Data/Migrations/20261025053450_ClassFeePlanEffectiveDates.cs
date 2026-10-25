using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSchool.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClassFeePlanEffectiveDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveFrom",
                table: "ClassFeePlanItems",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EffectiveFrom",
                table: "ClassFeePlanItems");
        }
    }
}
