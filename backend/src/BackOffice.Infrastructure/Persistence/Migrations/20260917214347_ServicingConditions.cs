using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingConditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServicingCondition",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferralId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EffectiveDatesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicingCondition", x => x.Id);
                    table.UniqueConstraint("AK_ServicingCondition_Id_ReferralId_CycleId_DraftId_RevisionId_RatingId", x => new { x.Id, x.ReferralId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId });
                    table.CheckConstraint("CK_ServicingCondition_Actor", "[CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_ServicingCondition_Code", "JSON_VALUE([DefinitionJson],'$.code') IS NOT NULL AND [Code]=JSON_VALUE([DefinitionJson],'$.code') AND [Code] IN ('provide-driver-proof','provide-premises-security','provide-signed-statement','provide-trading-history','overnight-security','named-drivers-only','any-driver-minimum-licence','revise-stock-limit','revise-vehicle-limit')");
                    table.CheckConstraint("CK_ServicingCondition_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ServicingCondition_Dates", "ISJSON([EffectiveDatesJson],ARRAY)=1 AND DATALENGTH([EffectiveDatesJson])<=16384 AND JSON_VALUE([EffectiveDatesJson],'$[0]') IS NOT NULL");
                    table.CheckConstraint("CK_ServicingCondition_DefinitionJson", "ISJSON([DefinitionJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[DefinitionJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_ServicingCondition_Kind", "[Kind] IN ('documentary','warranty','risk-change')");
                    table.CheckConstraint("CK_ServicingCondition_Sequence", "[Sequence] BETWEEN 1 AND 20");
                    table.CheckConstraint("CK_ServicingCondition_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_ServicingCondition_ServicingReferralDecision_DecisionId_ReferralId_CycleId_DraftId_RevisionId_RatingId",
                        columns: x => new { x.DecisionId, x.ReferralId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId },
                        principalTable: "ServicingReferralDecision",
                        principalColumns: new[] { "Id", "ReferralId", "CycleId", "DraftId", "RevisionId", "RatingId" });
                    table.ForeignKey(
                        name: "FK_ServicingCondition_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCondition_CreatedBy",
                table: "ServicingCondition",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCondition_DecisionId_ReferralId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingCondition",
                columns: new[] { "DecisionId", "ReferralId", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCondition_DecisionId_Sequence",
                table: "ServicingCondition",
                columns: new[] { "DecisionId", "Sequence" },
                unique: true);
            ServicingConditionGuards.Up(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServicingCondition");
        }
    }
}
