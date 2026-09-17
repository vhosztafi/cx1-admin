using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuoteTermsDeliveryStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_UnderwritingEvidenceAssociation_PreparedTermsOwner",
                table: "UnderwritingEvidenceAssociation");

            migrationBuilder.AddColumn<Guid>(
                name: "CurrentAcceptanceId",
                table: "UnderwritingCycle",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CurrentDeliveryId",
                table: "UnderwritingCycle",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CurrentTermsVersionId",
                table: "UnderwritingCycle",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TemplateVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateVersion", x => x.Id);
                    table.CheckConstraint("CK_TemplateVersion_ContentJson", "ISJSON([ContentJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[ContentJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_TemplateVersion_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_TemplateVersion_EffectiveFrom_Utc", "DATEPART(TZOFFSET,[EffectiveFrom]) = 0");
                    table.CheckConstraint("CK_TemplateVersion_EffectiveTo_Utc", "DATEPART(TZOFFSET,[EffectiveTo]) = 0");
                    table.CheckConstraint("CK_TemplateVersion_Interval", "[EffectiveFrom]<[EffectiveTo]");
                    table.CheckConstraint("CK_TemplateVersion_Kind", "[Kind]='quote-terms'");
                    table.CheckConstraint("CK_TemplateVersion_State", "[State] IN ('published','retired')");
                    table.CheckConstraint("CK_TemplateVersion_Version", "[Version]>0");
                    table.ForeignKey(
                        name: "FK_TemplateVersion_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TemplateVersion_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuoteTermsVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermsHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AssuranceHashAtPreparation = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TermsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PreparedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PreparedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteTermsVersion", x => x.Id);
                    table.UniqueConstraint("AK_QuoteTermsVersion_Id_CycleId_QuoteId", x => new { x.Id, x.CycleId, x.QuoteId });
                    table.UniqueConstraint("AK_QuoteTermsVersion_Id_RatingId_CycleId_QuoteId", x => new { x.Id, x.RatingId, x.CycleId, x.QuoteId });
                    table.CheckConstraint("CK_QuoteTermsVersion_AssuranceHashAtPreparation", "LEN([AssuranceHashAtPreparation])=64 AND [AssuranceHashAtPreparation] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_QuoteTermsVersion_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_QuoteTermsVersion_Number", "[Number]>0");
                    table.CheckConstraint("CK_QuoteTermsVersion_PreparedAt_Utc", "DATEPART(TZOFFSET,[PreparedAt]) = 0");
                    table.CheckConstraint("CK_QuoteTermsVersion_Provenance", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[PreparedBy] AND [PreparedAt]=[CreatedAt]");
                    table.CheckConstraint("CK_QuoteTermsVersion_TermsHash", "LEN([TermsHash])=64 AND [TermsHash] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_QuoteTermsVersion_TermsJson", "ISJSON([TermsJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[TermsJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.ForeignKey(
                        name: "FK_QuoteTermsVersion_QuoteRatingResult_RatingId_CycleId_QuoteId",
                        columns: x => new { x.RatingId, x.CycleId, x.QuoteId },
                        principalTable: "QuoteRatingResult",
                        principalColumns: new[] { "Id", "CycleId", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_QuoteTermsVersion_TemplateVersion_TemplateVersionId",
                        column: x => x.TemplateVersionId,
                        principalTable: "TemplateVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteTermsVersion_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteTermsVersion_User_PreparedBy",
                        column: x => x.PreparedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuoteTermsDelivery",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_QuoteTermsDelivery", x => x.Id);
                    table.UniqueConstraint("AK_QuoteTermsDelivery_Id_TermsVersionId_CycleId_QuoteId", x => new { x.Id, x.TermsVersionId, x.CycleId, x.QuoteId });
                    table.CheckConstraint("CK_QuoteTermsDelivery_AssuranceHashAtSend", "LEN([AssuranceHashAtSend])=64 AND [AssuranceHashAtSend] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_QuoteTermsDelivery_CompletedAt_Utc", "DATEPART(TZOFFSET,[CompletedAt]) = 0");
                    table.CheckConstraint("CK_QuoteTermsDelivery_Completion", "([State]='queued' AND [CompletedAt] IS NULL) OR ([State]<>'queued' AND [CompletedAt] IS NOT NULL AND [CompletedAt]>=[CreatedAt])");
                    table.CheckConstraint("CK_QuoteTermsDelivery_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_QuoteTermsDelivery_Delivered", "[State]<>'delivered' OR ([ProviderOperationId] IS NOT NULL AND [AttemptId] IS NOT NULL)");
                    table.CheckConstraint("CK_QuoteTermsDelivery_PayloadHash", "LEN([PayloadHash])=64 AND [PayloadHash] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_QuoteTermsDelivery_PayloadJson", "ISJSON([PayloadJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[PayloadJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_QuoteTermsDelivery_Provenance", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[SentBy]");
                    table.CheckConstraint("CK_QuoteTermsDelivery_RecipientSnapshotJson", "ISJSON([RecipientSnapshotJson],ARRAY)=1 AND DATALENGTH(CONVERT(varchar(max),[RecipientSnapshotJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_QuoteTermsDelivery_State", "[State] IN ('queued','delivered','failed','superseded')");
                    table.CheckConstraint("CK_QuoteTermsDelivery_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_QuoteTermsDelivery_AdapterAttempt_AttemptId_WorkId",
                        columns: x => new { x.AttemptId, x.WorkId },
                        principalTable: "AdapterAttempt",
                        principalColumns: new[] { "Id", "WorkId" });
                    table.ForeignKey(
                        name: "FK_QuoteTermsDelivery_DemoProviderOperation_ProviderOperationId",
                        column: x => x.ProviderOperationId,
                        principalTable: "DemoProviderOperation",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteTermsDelivery_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteTermsDelivery_QuoteTermsVersion_TermsVersionId_CycleId_QuoteId",
                        columns: x => new { x.TermsVersionId, x.CycleId, x.QuoteId },
                        principalTable: "QuoteTermsVersion",
                        principalColumns: new[] { "Id", "CycleId", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_QuoteTermsDelivery_SettingVersion_ScenarioVersionId",
                        column: x => x.ScenarioVersionId,
                        principalTable: "SettingVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteTermsDelivery_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteTermsDelivery_User_SentBy",
                        column: x => x.SentBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuoteAcceptance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_QuoteAcceptance", x => x.Id);
                    table.UniqueConstraint("AK_QuoteAcceptance_Id_CycleId_QuoteId", x => new { x.Id, x.CycleId, x.QuoteId });
                    table.CheckConstraint("CK_QuoteAcceptance_AcceptedAt_Utc", "DATEPART(TZOFFSET,[AcceptedAt]) = 0");
                    table.CheckConstraint("CK_QuoteAcceptance_AssuranceHash", "LEN([AssuranceHash])=64 AND [AssuranceHash] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_QuoteAcceptance_Channel", "[Channel] IN ('email','written','telephone')");
                    table.CheckConstraint("CK_QuoteAcceptance_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_QuoteAcceptance_Provenance", "LEN(TRIM([AccepterLabel]))>0 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[RecordedBy] AND [RecordedAt]=[CreatedAt] AND [AcceptedAt]<=[RecordedAt]");
                    table.CheckConstraint("CK_QuoteAcceptance_RecordedAt_Utc", "DATEPART(TZOFFSET,[RecordedAt]) = 0");
                    table.CheckConstraint("CK_QuoteAcceptance_TermsHash", "LEN([TermsHash])=64 AND [TermsHash] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.ForeignKey(
                        name: "FK_QuoteAcceptance_QuoteTermsDelivery_DeliveryId_TermsVersionId_CycleId_QuoteId",
                        columns: x => new { x.DeliveryId, x.TermsVersionId, x.CycleId, x.QuoteId },
                        principalTable: "QuoteTermsDelivery",
                        principalColumns: new[] { "Id", "TermsVersionId", "CycleId", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_QuoteAcceptance_QuoteTermsVersion_TermsVersionId_RatingId_CycleId_QuoteId",
                        columns: x => new { x.TermsVersionId, x.RatingId, x.CycleId, x.QuoteId },
                        principalTable: "QuoteTermsVersion",
                        principalColumns: new[] { "Id", "RatingId", "CycleId", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_QuoteAcceptance_UnderwritingEvidenceEvent_EvidenceReviewId_EvidenceAssociationId_CycleId_QuoteId",
                        columns: x => new { x.EvidenceReviewId, x.EvidenceAssociationId, x.CycleId, x.QuoteId },
                        principalTable: "UnderwritingEvidenceEvent",
                        principalColumns: new[] { "Id", "AssociationId", "CycleId", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_QuoteAcceptance_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteAcceptance_User_RecordedBy",
                        column: x => x.RecordedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingEvidenceAssociation_TermsVersionId_CycleId_QuoteId",
                table: "UnderwritingEvidenceAssociation",
                columns: new[] { "TermsVersionId", "CycleId", "QuoteId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_UnderwritingEvidenceAssociation_PreparedTermsOwner",
                table: "UnderwritingEvidenceAssociation",
                sql: "([TermsVersionId] IS NULL AND [RequirementCode] NOT IN ('signed-statement','acceptance-proof')) OR ([TermsVersionId] IS NOT NULL AND [RequirementCode] IN ('signed-statement','acceptance-proof'))");

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingCycle_CurrentAcceptanceId_Id_QuoteId",
                table: "UnderwritingCycle",
                columns: new[] { "CurrentAcceptanceId", "Id", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingCycle_CurrentDeliveryId_CurrentTermsVersionId_Id_QuoteId",
                table: "UnderwritingCycle",
                columns: new[] { "CurrentDeliveryId", "CurrentTermsVersionId", "Id", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingCycle_CurrentTermsVersionId_Id_QuoteId",
                table: "UnderwritingCycle",
                columns: new[] { "CurrentTermsVersionId", "Id", "QuoteId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_UnderwritingCycle_TermsPointers",
                table: "UnderwritingCycle",
                sql: "([CurrentDeliveryId] IS NULL AND [CurrentAcceptanceId] IS NULL) OR [CurrentTermsVersionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteAcceptance_CreatedBy",
                table: "QuoteAcceptance",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteAcceptance_DeliveryId_TermsVersionId_CycleId_QuoteId",
                table: "QuoteAcceptance",
                columns: new[] { "DeliveryId", "TermsVersionId", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteAcceptance_EvidenceReviewId_EvidenceAssociationId_CycleId_QuoteId",
                table: "QuoteAcceptance",
                columns: new[] { "EvidenceReviewId", "EvidenceAssociationId", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteAcceptance_QuoteId_RecordedAt_Id",
                table: "QuoteAcceptance",
                columns: new[] { "QuoteId", "RecordedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteAcceptance_RecordedBy",
                table: "QuoteAcceptance",
                column: "RecordedBy");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteAcceptance_TermsVersionId_RatingId_CycleId_QuoteId",
                table: "QuoteAcceptance",
                columns: new[] { "TermsVersionId", "RatingId", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTermsDelivery_AttemptId_WorkId",
                table: "QuoteTermsDelivery",
                columns: new[] { "AttemptId", "WorkId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTermsDelivery_CreatedBy",
                table: "QuoteTermsDelivery",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTermsDelivery_ProviderOperationId",
                table: "QuoteTermsDelivery",
                column: "ProviderOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTermsDelivery_QuoteId_CreatedAt_Id",
                table: "QuoteTermsDelivery",
                columns: new[] { "QuoteId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTermsDelivery_ScenarioVersionId",
                table: "QuoteTermsDelivery",
                column: "ScenarioVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTermsDelivery_SentBy",
                table: "QuoteTermsDelivery",
                column: "SentBy");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTermsDelivery_TermsVersionId_CycleId_QuoteId",
                table: "QuoteTermsDelivery",
                columns: new[] { "TermsVersionId", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTermsDelivery_WorkId",
                table: "QuoteTermsDelivery",
                column: "WorkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTermsVersion_CreatedBy",
                table: "QuoteTermsVersion",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTermsVersion_CycleId_Number",
                table: "QuoteTermsVersion",
                columns: new[] { "CycleId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTermsVersion_PreparedBy",
                table: "QuoteTermsVersion",
                column: "PreparedBy");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTermsVersion_RatingId_CycleId_QuoteId",
                table: "QuoteTermsVersion",
                columns: new[] { "RatingId", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteTermsVersion_TemplateVersionId",
                table: "QuoteTermsVersion",
                column: "TemplateVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateVersion_Code_ProductId_Version",
                table: "TemplateVersion",
                columns: new[] { "Code", "ProductId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TemplateVersion_CreatedBy",
                table: "TemplateVersion",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateVersion_ProductId",
                table: "TemplateVersion",
                column: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_UnderwritingCycle_QuoteAcceptance_CurrentAcceptanceId_Id_QuoteId",
                table: "UnderwritingCycle",
                columns: new[] { "CurrentAcceptanceId", "Id", "QuoteId" },
                principalTable: "QuoteAcceptance",
                principalColumns: new[] { "Id", "CycleId", "QuoteId" });

            migrationBuilder.AddForeignKey(
                name: "FK_UnderwritingCycle_QuoteTermsDelivery_CurrentDeliveryId_CurrentTermsVersionId_Id_QuoteId",
                table: "UnderwritingCycle",
                columns: new[] { "CurrentDeliveryId", "CurrentTermsVersionId", "Id", "QuoteId" },
                principalTable: "QuoteTermsDelivery",
                principalColumns: new[] { "Id", "TermsVersionId", "CycleId", "QuoteId" });

            migrationBuilder.AddForeignKey(
                name: "FK_UnderwritingCycle_QuoteTermsVersion_CurrentTermsVersionId_Id_QuoteId",
                table: "UnderwritingCycle",
                columns: new[] { "CurrentTermsVersionId", "Id", "QuoteId" },
                principalTable: "QuoteTermsVersion",
                principalColumns: new[] { "Id", "CycleId", "QuoteId" });

            migrationBuilder.AddForeignKey(
                name: "FK_UnderwritingEvidenceAssociation_QuoteTermsVersion_TermsVersionId_CycleId_QuoteId",
                table: "UnderwritingEvidenceAssociation",
                columns: new[] { "TermsVersionId", "CycleId", "QuoteId" },
                principalTable: "QuoteTermsVersion",
                principalColumns: new[] { "Id", "CycleId", "QuoteId" });
            AddTermsGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropTermsGuards(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_UnderwritingCycle_QuoteAcceptance_CurrentAcceptanceId_Id_QuoteId",
                table: "UnderwritingCycle");

            migrationBuilder.DropForeignKey(
                name: "FK_UnderwritingCycle_QuoteTermsDelivery_CurrentDeliveryId_CurrentTermsVersionId_Id_QuoteId",
                table: "UnderwritingCycle");

            migrationBuilder.DropForeignKey(
                name: "FK_UnderwritingCycle_QuoteTermsVersion_CurrentTermsVersionId_Id_QuoteId",
                table: "UnderwritingCycle");

            migrationBuilder.DropForeignKey(
                name: "FK_UnderwritingEvidenceAssociation_QuoteTermsVersion_TermsVersionId_CycleId_QuoteId",
                table: "UnderwritingEvidenceAssociation");

            migrationBuilder.DropTable(
                name: "QuoteAcceptance");

            migrationBuilder.DropTable(
                name: "QuoteTermsDelivery");

            migrationBuilder.DropTable(
                name: "QuoteTermsVersion");

            migrationBuilder.DropTable(
                name: "TemplateVersion");

            migrationBuilder.DropIndex(
                name: "IX_UnderwritingEvidenceAssociation_TermsVersionId_CycleId_QuoteId",
                table: "UnderwritingEvidenceAssociation");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UnderwritingEvidenceAssociation_PreparedTermsOwner",
                table: "UnderwritingEvidenceAssociation");

            migrationBuilder.DropIndex(
                name: "IX_UnderwritingCycle_CurrentAcceptanceId_Id_QuoteId",
                table: "UnderwritingCycle");

            migrationBuilder.DropIndex(
                name: "IX_UnderwritingCycle_CurrentDeliveryId_CurrentTermsVersionId_Id_QuoteId",
                table: "UnderwritingCycle");

            migrationBuilder.DropIndex(
                name: "IX_UnderwritingCycle_CurrentTermsVersionId_Id_QuoteId",
                table: "UnderwritingCycle");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UnderwritingCycle_TermsPointers",
                table: "UnderwritingCycle");

            migrationBuilder.DropColumn(
                name: "CurrentAcceptanceId",
                table: "UnderwritingCycle");

            migrationBuilder.DropColumn(
                name: "CurrentDeliveryId",
                table: "UnderwritingCycle");

            migrationBuilder.DropColumn(
                name: "CurrentTermsVersionId",
                table: "UnderwritingCycle");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UnderwritingEvidenceAssociation_PreparedTermsOwner",
                table: "UnderwritingEvidenceAssociation",
                sql: "[TermsVersionId] IS NULL");
        }
    }
}
