using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingRatingResultStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrentRatingId",
                table: "ServicingCycle",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServicingRatingResult",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InputHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ResultHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BaseAnnualPremium = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Premium = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    BrokerCommission = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    GrossPayable = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    NetDue = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicingRatingResult", x => x.Id);
                    table.UniqueConstraint("AK_ServicingRatingResult_Id_CycleId_DraftId_RevisionId", x => new { x.Id, x.CycleId, x.DraftId, x.RevisionId });
                    table.CheckConstraint("CK_ServicingRatingResult_BaseAnnualPremium", "[BaseAnnualPremium] BETWEEN -9999999999999.99 AND 9999999999999.99");
                    table.CheckConstraint("CK_ServicingRatingResult_BrokerCommission", "[BrokerCommission] BETWEEN -9999999999999.99 AND 9999999999999.99");
                    table.CheckConstraint("CK_ServicingRatingResult_CompletedAt_Utc", "DATEPART(TZOFFSET,[CompletedAt]) = 0");
                    table.CheckConstraint("CK_ServicingRatingResult_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ServicingRatingResult_ExpiresAt_Utc", "DATEPART(TZOFFSET,[ExpiresAt]) = 0");
                    table.CheckConstraint("CK_ServicingRatingResult_Fee", "[Fee] BETWEEN -9999999999999.99 AND 9999999999999.99");
                    table.CheckConstraint("CK_ServicingRatingResult_GrossPayable", "[GrossPayable] BETWEEN -9999999999999.99 AND 9999999999999.99");
                    table.CheckConstraint("CK_ServicingRatingResult_Interval", "[CompletedAt]>=[CreatedAt] AND [ExpiresAt]>[CompletedAt]");
                    table.CheckConstraint("CK_ServicingRatingResult_NetDue", "[NetDue] BETWEEN -9999999999999.99 AND 9999999999999.99");
                    table.CheckConstraint("CK_ServicingRatingResult_Outcome", "[Outcome] IN ('rated','rejected')");
                    table.CheckConstraint("CK_ServicingRatingResult_Premium", "[Premium] BETWEEN -9999999999999.99 AND 9999999999999.99");
                    table.CheckConstraint("CK_ServicingRatingResult_ResultJson", "ISJSON([ResultJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[ResultJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_ServicingRatingResult_Tax", "[Tax] BETWEEN -9999999999999.99 AND 9999999999999.99");
                    table.CheckConstraint("CK_ServicingRatingResult_Totals", "[GrossPayable]=[Premium]+[Tax]+[Fee] AND [NetDue]=[GrossPayable]-[BrokerCommission] AND [Fee]>=0 AND (([Outcome]='rated' AND [BaseAnnualPremium]>0) OR ([Outcome]='rejected' AND [BaseAnnualPremium]=0 AND [Premium]=0 AND [Tax]=0 AND [Fee]=0 AND [BrokerCommission]=0 AND [GrossPayable]=0 AND [NetDue]=0))");
                    table.ForeignKey(
                        name: "FK_ServicingRatingResult_AdapterAttempt_AttemptId_WorkId",
                        columns: x => new { x.AttemptId, x.WorkId },
                        principalTable: "AdapterAttempt",
                        principalColumns: new[] { "Id", "WorkId" });
                    table.ForeignKey(
                        name: "FK_ServicingRatingResult_DemoProviderOperation_ProviderOperationId",
                        column: x => x.ProviderOperationId,
                        principalTable: "DemoProviderOperation",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingRatingResult_ServicingCycle_CycleId_DraftId_RevisionId_WorkId_RuleVersionId_InputHash",
                        columns: x => new { x.CycleId, x.DraftId, x.RevisionId, x.WorkId, x.RuleVersionId, x.InputHash },
                        principalTable: "ServicingCycle",
                        principalColumns: new[] { "Id", "DraftId", "RevisionId", "WorkId", "RatingRuleVersionId", "InputHash" });
                    table.ForeignKey(
                        name: "FK_ServicingRatingResult_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCycle_CurrentRatingId_Id_DraftId_RevisionId",
                table: "ServicingCycle",
                columns: new[] { "CurrentRatingId", "Id", "DraftId", "RevisionId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingCycle_RatedResult",
                table: "ServicingCycle",
                sql: "[State]<>'rated' OR [CurrentRatingId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingRatingResult_AttemptId",
                table: "ServicingRatingResult",
                column: "AttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicingRatingResult_AttemptId_WorkId",
                table: "ServicingRatingResult",
                columns: new[] { "AttemptId", "WorkId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingRatingResult_CreatedBy",
                table: "ServicingRatingResult",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingRatingResult_CycleId_DraftId_RevisionId_WorkId_RuleVersionId_InputHash",
                table: "ServicingRatingResult",
                columns: new[] { "CycleId", "DraftId", "RevisionId", "WorkId", "RuleVersionId", "InputHash" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingRatingResult_ProviderOperationId",
                table: "ServicingRatingResult",
                column: "ProviderOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicingRatingResult_WorkId",
                table: "ServicingRatingResult",
                column: "WorkId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ServicingCycle_ServicingRatingResult_CurrentRatingId_Id_DraftId_RevisionId",
                table: "ServicingCycle",
                columns: new[] { "CurrentRatingId", "Id", "DraftId", "RevisionId" },
                principalTable: "ServicingRatingResult",
                principalColumns: new[] { "Id", "CycleId", "DraftId", "RevisionId" });
            AddResultGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ServicingCycle_Rating;");
            migrationBuilder.DropForeignKey(
                name: "FK_ServicingCycle_ServicingRatingResult_CurrentRatingId_Id_DraftId_RevisionId",
                table: "ServicingCycle");

            migrationBuilder.DropTable(
                name: "ServicingRatingResult");

            migrationBuilder.DropIndex(
                name: "IX_ServicingCycle_CurrentRatingId_Id_DraftId_RevisionId",
                table: "ServicingCycle");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingCycle_RatedResult",
                table: "ServicingCycle");

            migrationBuilder.DropColumn(
                name: "CurrentRatingId",
                table: "ServicingCycle");
        }
    }
}
