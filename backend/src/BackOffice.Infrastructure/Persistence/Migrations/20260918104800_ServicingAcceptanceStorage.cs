using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingAcceptanceStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrentAcceptanceId",
                table: "ServicingCycle",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServicingAcceptance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermsVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermsHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AssuranceHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AccepterLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EvidenceAssociationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvidenceReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicingAcceptance", x => x.Id);
                    table.UniqueConstraint("AK_ServicingAcceptance_Id_DeliveryId_TermsVersionId_CycleId_DraftId_RevisionId_RatingId", x => new { x.Id, x.DeliveryId, x.TermsVersionId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId });
                    table.CheckConstraint("CK_ServicingAcceptance_AcceptedAt_Utc", "DATEPART(TZOFFSET,[AcceptedAt]) = 0");
                    table.CheckConstraint("CK_ServicingAcceptance_AssuranceHash", "LEN([AssuranceHash])=64 AND [AssuranceHash] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_ServicingAcceptance_Channel", "[Channel] IN ('email','written','telephone')");
                    table.CheckConstraint("CK_ServicingAcceptance_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ServicingAcceptance_Provenance", "LEN(TRIM([AccepterLabel]))>0 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[RecordedBy] AND [RecordedAt]=[CreatedAt] AND [AcceptedAt]<=[RecordedAt]");
                    table.CheckConstraint("CK_ServicingAcceptance_RecordedAt_Utc", "DATEPART(TZOFFSET,[RecordedAt]) = 0");
                    table.CheckConstraint("CK_ServicingAcceptance_TermsHash", "LEN([TermsHash])=64 AND [TermsHash] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.ForeignKey(
                        name: "FK_ServicingAcceptance_ServicingEvidenceEvent_EvidenceReviewId_EvidenceAssociationId_CycleId_DraftId_RevisionId_RatingId",
                        columns: x => new { x.EvidenceReviewId, x.EvidenceAssociationId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId },
                        principalTable: "ServicingEvidenceEvent",
                        principalColumns: new[] { "Id", "AssociationId", "CycleId", "DraftId", "RevisionId", "RatingId" });
                    table.ForeignKey(
                        name: "FK_ServicingAcceptance_ServicingTermsDelivery_DeliveryId_TermsVersionId_CycleId_DraftId_RevisionId_RatingId",
                        columns: x => new { x.DeliveryId, x.TermsVersionId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId },
                        principalTable: "ServicingTermsDelivery",
                        principalColumns: new[] { "Id", "TermsVersionId", "CycleId", "DraftId", "RevisionId", "RatingId" });
                    table.ForeignKey(
                        name: "FK_ServicingAcceptance_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingAcceptance_User_RecordedBy",
                        column: x => x.RecordedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCycle_CurrentAcceptanceId_CurrentDeliveryId_CurrentTermsVersionId_Id_DraftId_RevisionId_CurrentRatingId",
                table: "ServicingCycle",
                columns: new[] { "CurrentAcceptanceId", "CurrentDeliveryId", "CurrentTermsVersionId", "Id", "DraftId", "RevisionId", "CurrentRatingId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingCycle_AcceptancePointer",
                table: "ServicingCycle",
                sql: "[CurrentAcceptanceId] IS NULL OR ([CurrentDeliveryId] IS NOT NULL AND [CurrentTermsVersionId] IS NOT NULL AND [CurrentRatingId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingAcceptance_CreatedBy",
                table: "ServicingAcceptance",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingAcceptance_DeliveryId_TermsVersionId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingAcceptance",
                columns: new[] { "DeliveryId", "TermsVersionId", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingAcceptance_DraftId_RecordedAt_Id",
                table: "ServicingAcceptance",
                columns: new[] { "DraftId", "RecordedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingAcceptance_EvidenceReviewId_EvidenceAssociationId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingAcceptance",
                columns: new[] { "EvidenceReviewId", "EvidenceAssociationId", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingAcceptance_RecordedBy",
                table: "ServicingAcceptance",
                column: "RecordedBy");

            migrationBuilder.AddForeignKey(
                name: "FK_ServicingCycle_ServicingAcceptance_CurrentAcceptanceId_CurrentDeliveryId_CurrentTermsVersionId_Id_DraftId_RevisionId_Current~",
                table: "ServicingCycle",
                columns: new[] { "CurrentAcceptanceId", "CurrentDeliveryId", "CurrentTermsVersionId", "Id", "DraftId", "RevisionId", "CurrentRatingId" },
                principalTable: "ServicingAcceptance",
                principalColumns: new[] { "Id", "DeliveryId", "TermsVersionId", "CycleId", "DraftId", "RevisionId", "RatingId" });
            ServicingAcceptanceStorageGuards.Up(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ServicingAcceptanceStorageGuards.Down(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_ServicingCycle_ServicingAcceptance_CurrentAcceptanceId_CurrentDeliveryId_CurrentTermsVersionId_Id_DraftId_RevisionId_Current~",
                table: "ServicingCycle");

            migrationBuilder.DropTable(
                name: "ServicingAcceptance");

            migrationBuilder.DropIndex(
                name: "IX_ServicingCycle_CurrentAcceptanceId_CurrentDeliveryId_CurrentTermsVersionId_Id_DraftId_RevisionId_CurrentRatingId",
                table: "ServicingCycle");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingCycle_AcceptancePointer",
                table: "ServicingCycle");

            migrationBuilder.DropColumn(
                name: "CurrentAcceptanceId",
                table: "ServicingCycle");
        }
    }
}
