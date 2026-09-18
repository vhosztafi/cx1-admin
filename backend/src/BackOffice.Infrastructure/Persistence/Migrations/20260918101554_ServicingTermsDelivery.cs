using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingTermsDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrentDeliveryId",
                table: "ServicingCycle",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServicingTermsDelivery",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermsVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AssuranceHashAtSend = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SentBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProviderOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OutcomeCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicingTermsDelivery", x => x.Id);
                    table.UniqueConstraint("AK_ServicingTermsDelivery_Id_TermsVersionId_CycleId_DraftId_RevisionId_RatingId", x => new { x.Id, x.TermsVersionId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId });
                    table.CheckConstraint("CK_ServicingTermsDelivery_AssuranceHashAtSendFormat", "LEN([AssuranceHashAtSend])=64 AND [AssuranceHashAtSend] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_ServicingTermsDelivery_CompletedAt_Utc", "DATEPART(TZOFFSET,[CompletedAt]) = 0");
                    table.CheckConstraint("CK_ServicingTermsDelivery_Completion", "([State]='queued' AND [CompletedAt] IS NULL AND [ProviderOperationId] IS NULL AND [AttemptId] IS NULL AND [OutcomeCode] IS NULL) OR ([State]<>'queued' AND [CompletedAt] IS NOT NULL AND [CompletedAt]>=[CreatedAt] AND [AttemptId] IS NOT NULL)");
                    table.CheckConstraint("CK_ServicingTermsDelivery_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ServicingTermsDelivery_Delivered", "[State]<>'delivered' OR ([ProviderOperationId] IS NOT NULL AND [OutcomeCode] IS NULL)");
                    table.CheckConstraint("CK_ServicingTermsDelivery_Payload", "ISJSON([PayloadJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[PayloadJson] COLLATE Latin1_General_100_BIN2_UTF8))<=9437184");
                    table.CheckConstraint("CK_ServicingTermsDelivery_PayloadHash", "[PayloadHash]=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varchar(max),[PayloadJson] COLLATE Latin1_General_100_BIN2_UTF8)),2))");
                    table.CheckConstraint("CK_ServicingTermsDelivery_PayloadHashFormat", "LEN([PayloadHash])=64 AND [PayloadHash] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_ServicingTermsDelivery_Provenance", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[SentBy]");
                    table.CheckConstraint("CK_ServicingTermsDelivery_Recipients", "ISJSON([RecipientSnapshotJson],ARRAY)=1 AND DATALENGTH(CONVERT(varchar(max),[RecipientSnapshotJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_ServicingTermsDelivery_State", "[State] IN ('queued','delivered','failed','superseded')");
                    table.CheckConstraint("CK_ServicingTermsDelivery_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_ServicingTermsDelivery_AdapterAttempt_AttemptId_WorkId",
                        columns: x => new { x.AttemptId, x.WorkId },
                        principalTable: "AdapterAttempt",
                        principalColumns: new[] { "Id", "WorkId" });
                    table.ForeignKey(
                        name: "FK_ServicingTermsDelivery_DemoProviderOperation_ProviderOperationId",
                        column: x => x.ProviderOperationId,
                        principalTable: "DemoProviderOperation",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingTermsDelivery_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingTermsDelivery_ServicingTermsVersion_TermsVersionId_CycleId_DraftId_RevisionId_RatingId",
                        columns: x => new { x.TermsVersionId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId },
                        principalTable: "ServicingTermsVersion",
                        principalColumns: new[] { "Id", "CycleId", "DraftId", "RevisionId", "RatingId" });
                    table.ForeignKey(
                        name: "FK_ServicingTermsDelivery_SettingVersion_ScenarioVersionId",
                        column: x => x.ScenarioVersionId,
                        principalTable: "SettingVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingTermsDelivery_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingTermsDelivery_User_SentBy",
                        column: x => x.SentBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCycle_CurrentDeliveryId_CurrentTermsVersionId_Id_DraftId_RevisionId_CurrentRatingId",
                table: "ServicingCycle",
                columns: new[] { "CurrentDeliveryId", "CurrentTermsVersionId", "Id", "DraftId", "RevisionId", "CurrentRatingId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingCycle_DeliveryPointer",
                table: "ServicingCycle",
                sql: "[CurrentDeliveryId] IS NULL OR ([CurrentTermsVersionId] IS NOT NULL AND [CurrentRatingId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingTermsDelivery_AttemptId_WorkId",
                table: "ServicingTermsDelivery",
                columns: new[] { "AttemptId", "WorkId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingTermsDelivery_CreatedBy",
                table: "ServicingTermsDelivery",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingTermsDelivery_DraftId_CreatedAt_Id",
                table: "ServicingTermsDelivery",
                columns: new[] { "DraftId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingTermsDelivery_ProviderOperationId",
                table: "ServicingTermsDelivery",
                column: "ProviderOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingTermsDelivery_ScenarioVersionId",
                table: "ServicingTermsDelivery",
                column: "ScenarioVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingTermsDelivery_SentBy",
                table: "ServicingTermsDelivery",
                column: "SentBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingTermsDelivery_TermsVersionId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingTermsDelivery",
                columns: new[] { "TermsVersionId", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingTermsDelivery_WorkId",
                table: "ServicingTermsDelivery",
                column: "WorkId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ServicingCycle_ServicingTermsDelivery_CurrentDeliveryId_CurrentTermsVersionId_Id_DraftId_RevisionId_CurrentRatingId",
                table: "ServicingCycle",
                columns: new[] { "CurrentDeliveryId", "CurrentTermsVersionId", "Id", "DraftId", "RevisionId", "CurrentRatingId" },
                principalTable: "ServicingTermsDelivery",
                principalColumns: new[] { "Id", "TermsVersionId", "CycleId", "DraftId", "RevisionId", "RatingId" });
            ServicingTermsDeliveryGuards.Up(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ServicingTermsDeliveryGuards.Down(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_ServicingCycle_ServicingTermsDelivery_CurrentDeliveryId_CurrentTermsVersionId_Id_DraftId_RevisionId_CurrentRatingId",
                table: "ServicingCycle");

            migrationBuilder.DropTable(
                name: "ServicingTermsDelivery");

            migrationBuilder.DropIndex(
                name: "IX_ServicingCycle_CurrentDeliveryId_CurrentTermsVersionId_Id_DraftId_RevisionId_CurrentRatingId",
                table: "ServicingCycle");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingCycle_DeliveryPointer",
                table: "ServicingCycle");

            migrationBuilder.DropColumn(
                name: "CurrentDeliveryId",
                table: "ServicingCycle");
        }
    }
}
