using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommercialAtomicIssue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReferencePrefix",
                table: "Policy",
                type: "nvarchar(6)",
                maxLength: 6,
                nullable: false,
                defaultValue: "PL-MT-");

            migrationBuilder.AlterColumn<string>(
                name: "Reference",
                table: "Policy",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                computedColumnSql: "[ReferencePrefix] + RIGHT('0000000000' + CONVERT(varchar(20), [Number]), 10)",
                stored: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(40)",
                oldMaxLength: 40,
                oldComputedColumnSql: "'PL-MT-' + RIGHT('0000000000' + CONVERT(varchar(20), [Number]), 10)",
                oldStored: true);

            migrationBuilder.CreateTable(
                name: "CommercialExposureIssueDecision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExposureVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecisionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DecisionHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialExposureIssueDecision", x => x.Id);
                    table.CheckConstraint("CK_CommercialExposureIssueDecision_AssessedAt_Utc", "DATEPART(TZOFFSET,[AssessedAt]) = 0");
                    table.CheckConstraint("CK_CommercialExposureIssueDecision_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_CommercialExposureIssueDecision_DecisionJson_Json", "ISJSON([DecisionJson]) = 1");
                    table.CheckConstraint("CK_CommercialExposureIssueDecision_Provenance", "[CreatedBy] IS NOT NULL AND [CreatedAt]=[AssessedAt] AND [DecisionHash]<>0x0000000000000000000000000000000000000000000000000000000000000000");
                    table.ForeignKey(
                        name: "FK_CommercialExposureIssueDecision_CommercialExposureVersion_ExposureVersionId",
                        column: x => x.ExposureVersionId,
                        principalTable: "CommercialExposureVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CommercialExposureIssueDecision_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Policy_ReferencePrefix",
                table: "Policy",
                sql: "[ReferencePrefix] IN ('PL-MT-','PL-CC-')");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialExposureIssueDecision_CreatedBy",
                table: "CommercialExposureIssueDecision",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialExposureIssueDecision_ExposureVersionId",
                table: "CommercialExposureIssueDecision",
                column: "ExposureVersionId",
                unique: true);
            AddCommercialIssueGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropCommercialIssueGuards(migrationBuilder);
            migrationBuilder.DropTable(
                name: "CommercialExposureIssueDecision");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Policy_ReferencePrefix",
                table: "Policy");

            migrationBuilder.AlterColumn<string>(
                name: "Reference",
                table: "Policy",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                computedColumnSql: "'PL-MT-' + RIGHT('0000000000' + CONVERT(varchar(20), [Number]), 10)",
                stored: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(40)",
                oldMaxLength: 40,
                oldComputedColumnSql: "[ReferencePrefix] + RIGHT('0000000000' + CONVERT(varchar(20), [Number]), 10)",
                oldStored: true);

            migrationBuilder.DropColumn(
                name: "ReferencePrefix",
                table: "Policy");

        }
    }
}
