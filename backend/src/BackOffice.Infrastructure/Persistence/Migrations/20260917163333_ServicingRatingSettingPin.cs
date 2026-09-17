using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingRatingSettingPin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ServicingSettingVersionId",
                table: "ServicingCycle",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCycle_ServicingSettingVersionId",
                table: "ServicingCycle",
                column: "ServicingSettingVersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_ServicingCycle_SettingVersion_ServicingSettingVersionId",
                table: "ServicingCycle",
                column: "ServicingSettingVersionId",
                principalTable: "SettingVersion",
                principalColumn: "Id");
            AddSettingPinGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ServicingCycle_SettingInsert; DROP TRIGGER IF EXISTS TR_ServicingCycle_SettingUpdate;");
            migrationBuilder.DropForeignKey(
                name: "FK_ServicingCycle_SettingVersion_ServicingSettingVersionId",
                table: "ServicingCycle");

            migrationBuilder.DropIndex(
                name: "IX_ServicingCycle_ServicingSettingVersionId",
                table: "ServicingCycle");

            migrationBuilder.DropColumn(
                name: "ServicingSettingVersionId",
                table: "ServicingCycle");
        }
    }
}
