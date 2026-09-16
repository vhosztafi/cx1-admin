using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnderwritingDecisionStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LatestDecisionId",
                table: "QuoteReferral",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "QuoteReferralDecision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferralId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Question = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorityVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConditionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteReferralDecision", x => x.Id);
                    table.UniqueConstraint("AK_QuoteReferralDecision_Id_ReferralId_CycleId_QuoteId", x => new { x.Id, x.ReferralId, x.CycleId, x.QuoteId });
                    table.CheckConstraint("CK_QuoteReferralDecision_Conditions", "ISJSON([ConditionsJson],ARRAY)=1 AND DATALENGTH([ConditionsJson])<=131072 AND (([Outcome] IN ('approve-with-conditions','query') AND JSON_QUERY([ConditionsJson],'$[0]') IS NOT NULL) OR ([Outcome] NOT IN ('approve-with-conditions','query') AND [ConditionsJson]='[]'))");
                    table.CheckConstraint("CK_QuoteReferralDecision_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_QuoteReferralDecision_DecidedAt_Utc", "DATEPART(TZOFFSET,[DecidedAt]) = 0");
                    table.CheckConstraint("CK_QuoteReferralDecision_Outcome", "[Outcome] IN ('approve','approve-with-conditions','query','decline','reopen')");
                    table.CheckConstraint("CK_QuoteReferralDecision_Question", "([Outcome]='query' AND [Question] IS NOT NULL AND LEN(TRIM([Question]))>0) OR ([Outcome]<>'query' AND [Question] IS NULL)");
                    table.CheckConstraint("CK_QuoteReferralDecision_Reason", "LEN(TRIM([Reason]))>0 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId] AND [DecidedAt]>=[CreatedAt]");
                    table.CheckConstraint("CK_QuoteReferralDecision_Sequence", "[Sequence]>0");
                    table.ForeignKey(
                        name: "FK_QuoteReferralDecision_AuthorityVersion_AuthorityVersionId",
                        column: x => x.AuthorityVersionId,
                        principalTable: "AuthorityVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteReferralDecision_QuoteReferral_ReferralId_CycleId_QuoteId",
                        columns: x => new { x.ReferralId, x.CycleId, x.QuoteId },
                        principalTable: "QuoteReferral",
                        principalColumns: new[] { "Id", "CycleId", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_QuoteReferralDecision_User_ActorId",
                        column: x => x.ActorId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteReferralDecision_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuoteCondition",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferralId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Wording = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    EndorsementCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    LatestResolutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteCondition", x => x.Id);
                    table.UniqueConstraint("AK_QuoteCondition_Id_CycleId_QuoteId", x => new { x.Id, x.CycleId, x.QuoteId });
                    table.CheckConstraint("CK_QuoteCondition_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_QuoteCondition_Definition", "COALESCE(JSON_VALUE([DefinitionJson],'$.code'),'')=[Code] AND (([Kind]='documentary' AND [Code] IN ('provide-driver-proof','provide-premises-security','provide-signed-statement','provide-trading-history') AND [EndorsementCode] IS NULL) OR ([Kind]='warranty' AND [Code] IN ('overnight-security','named-drivers-only','any-driver-minimum-licence') AND [EndorsementCode] IS NOT NULL AND LEN(TRIM([Wording]))>0) OR ([Kind]='risk-change' AND [Code] IN ('revise-stock-limit','revise-vehicle-limit') AND [EndorsementCode] IS NULL))");
                    table.CheckConstraint("CK_QuoteCondition_DefinitionJson", "ISJSON([DefinitionJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[DefinitionJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_QuoteCondition_SecurityCode", "[Code]<>'overnight-security' OR [EndorsementCode]='W-07'");
                    table.CheckConstraint("CK_QuoteCondition_Sequence", "[Sequence]>0");
                    table.CheckConstraint("CK_QuoteCondition_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_QuoteCondition_QuoteReferralDecision_DecisionId_ReferralId_CycleId_QuoteId",
                        columns: x => new { x.DecisionId, x.ReferralId, x.CycleId, x.QuoteId },
                        principalTable: "QuoteReferralDecision",
                        principalColumns: new[] { "Id", "ReferralId", "CycleId", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_QuoteCondition_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuoteConditionResolution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConditionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EvidenceAssociationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvidenceReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorityVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteConditionResolution", x => x.Id);
                    table.UniqueConstraint("AK_QuoteConditionResolution_Id_ConditionId_CycleId_QuoteId", x => new { x.Id, x.ConditionId, x.CycleId, x.QuoteId });
                    table.CheckConstraint("CK_QuoteConditionResolution_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_QuoteConditionResolution_Outcome", "[Outcome] IN ('satisfied','rejected')");
                    table.CheckConstraint("CK_QuoteConditionResolution_Reason", "LEN(TRIM([Reason]))>0 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId] AND [RecordedAt]>=[CreatedAt]");
                    table.CheckConstraint("CK_QuoteConditionResolution_RecordedAt_Utc", "DATEPART(TZOFFSET,[RecordedAt]) = 0");
                    table.CheckConstraint("CK_QuoteConditionResolution_Sequence", "[Sequence]>0");
                    table.ForeignKey(
                        name: "FK_QuoteConditionResolution_AuthorityVersion_AuthorityVersionId",
                        column: x => x.AuthorityVersionId,
                        principalTable: "AuthorityVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteConditionResolution_QuoteCondition_ConditionId_CycleId_QuoteId",
                        columns: x => new { x.ConditionId, x.CycleId, x.QuoteId },
                        principalTable: "QuoteCondition",
                        principalColumns: new[] { "Id", "CycleId", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_QuoteConditionResolution_User_ActorId",
                        column: x => x.ActorId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteConditionResolution_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UnderwritingEvidenceAssociation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequirementCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    RiskItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConditionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TermsVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InputFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    LatestReviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WithdrawnEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnderwritingEvidenceAssociation", x => x.Id);
                    table.UniqueConstraint("AK_UnderwritingEvidenceAssociation_Id_CycleId_QuoteId", x => new { x.Id, x.CycleId, x.QuoteId });
                    table.CheckConstraint("CK_UnderwritingEvidenceAssociation_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_UnderwritingEvidenceAssociation_Fingerprint", "LEN([InputFingerprint])=64 AND [InputFingerprint] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_UnderwritingEvidenceAssociation_Purpose", "[RequirementCode] IN ('motor-trader-proof','no-claims-proof','photocard-both-sides','driving-record','premises-security','trading-history','signed-statement','warranty-acknowledgement','acceptance-proof','capacity-response')");
                    table.CheckConstraint("CK_UnderwritingEvidenceAssociation_Reason", "LEN(TRIM([Reason]))>0 AND [CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_UnderwritingEvidenceAssociation_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_UnderwritingEvidenceAssociation_QuoteCondition_ConditionId_CycleId_QuoteId",
                        columns: x => new { x.ConditionId, x.CycleId, x.QuoteId },
                        principalTable: "QuoteCondition",
                        principalColumns: new[] { "Id", "CycleId", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_UnderwritingEvidenceAssociation_QuoteEvidenceFile_FileId_QuoteId",
                        columns: x => new { x.FileId, x.QuoteId },
                        principalTable: "QuoteEvidenceFile",
                        principalColumns: new[] { "Id", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_UnderwritingEvidenceAssociation_UnderwritingCycle_CycleId_QuoteId",
                        columns: x => new { x.CycleId, x.QuoteId },
                        principalTable: "UnderwritingCycle",
                        principalColumns: new[] { "Id", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_UnderwritingEvidenceAssociation_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UnderwritingEvidenceEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssociationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorityVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    InputFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnderwritingEvidenceEvent", x => x.Id);
                    table.UniqueConstraint("AK_UnderwritingEvidenceEvent_Id_AssociationId_CycleId_QuoteId", x => new { x.Id, x.AssociationId, x.CycleId, x.QuoteId });
                    table.CheckConstraint("CK_UnderwritingEvidenceEvent_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_UnderwritingEvidenceEvent_Fingerprint", "LEN([InputFingerprint])=64 AND [InputFingerprint] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_UnderwritingEvidenceEvent_Outcome", "([Kind]='review' AND [Outcome] IS NOT NULL AND [Outcome] IN ('accepted','rejected') AND [AuthorityVersionId] IS NOT NULL) OR ([Kind]='withdrawal' AND [Outcome] IS NULL AND [AuthorityVersionId] IS NULL)");
                    table.CheckConstraint("CK_UnderwritingEvidenceEvent_Reason", "LEN(TRIM([Reason]))>0 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId] AND [RecordedAt]>=[CreatedAt]");
                    table.CheckConstraint("CK_UnderwritingEvidenceEvent_RecordedAt_Utc", "DATEPART(TZOFFSET,[RecordedAt]) = 0");
                    table.CheckConstraint("CK_UnderwritingEvidenceEvent_Sequence", "[Sequence]>0");
                    table.ForeignKey(
                        name: "FK_UnderwritingEvidenceEvent_AuthorityVersion_AuthorityVersionId",
                        column: x => x.AuthorityVersionId,
                        principalTable: "AuthorityVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UnderwritingEvidenceEvent_UnderwritingEvidenceAssociation_AssociationId_CycleId_QuoteId",
                        columns: x => new { x.AssociationId, x.CycleId, x.QuoteId },
                        principalTable: "UnderwritingEvidenceAssociation",
                        principalColumns: new[] { "Id", "CycleId", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_UnderwritingEvidenceEvent_User_ActorId",
                        column: x => x.ActorId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UnderwritingEvidenceEvent_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteReferral_LatestDecisionId_Id_CycleId_QuoteId",
                table: "QuoteReferral",
                columns: new[] { "LatestDecisionId", "Id", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteCondition_CreatedBy",
                table: "QuoteCondition",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteCondition_DecisionId_ReferralId_CycleId_QuoteId",
                table: "QuoteCondition",
                columns: new[] { "DecisionId", "ReferralId", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteCondition_DecisionId_Sequence",
                table: "QuoteCondition",
                columns: new[] { "DecisionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteCondition_LatestResolutionId_Id_CycleId_QuoteId",
                table: "QuoteCondition",
                columns: new[] { "LatestResolutionId", "Id", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteConditionResolution_ActorId",
                table: "QuoteConditionResolution",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteConditionResolution_AuthorityVersionId",
                table: "QuoteConditionResolution",
                column: "AuthorityVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteConditionResolution_ConditionId_CycleId_QuoteId",
                table: "QuoteConditionResolution",
                columns: new[] { "ConditionId", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteConditionResolution_ConditionId_Sequence",
                table: "QuoteConditionResolution",
                columns: new[] { "ConditionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteConditionResolution_CreatedBy",
                table: "QuoteConditionResolution",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteConditionResolution_EvidenceReviewId_EvidenceAssociationId_CycleId_QuoteId",
                table: "QuoteConditionResolution",
                columns: new[] { "EvidenceReviewId", "EvidenceAssociationId", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteReferralDecision_ActorId",
                table: "QuoteReferralDecision",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteReferralDecision_AuthorityVersionId",
                table: "QuoteReferralDecision",
                column: "AuthorityVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteReferralDecision_CreatedBy",
                table: "QuoteReferralDecision",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteReferralDecision_ReferralId_CycleId_QuoteId",
                table: "QuoteReferralDecision",
                columns: new[] { "ReferralId", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteReferralDecision_ReferralId_Sequence",
                table: "QuoteReferralDecision",
                columns: new[] { "ReferralId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingEvidenceAssociation_ConditionId_CycleId_QuoteId",
                table: "UnderwritingEvidenceAssociation",
                columns: new[] { "ConditionId", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingEvidenceAssociation_CreatedBy",
                table: "UnderwritingEvidenceAssociation",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingEvidenceAssociation_CycleId_CreatedAt_Id",
                table: "UnderwritingEvidenceAssociation",
                columns: new[] { "CycleId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingEvidenceAssociation_CycleId_QuoteId",
                table: "UnderwritingEvidenceAssociation",
                columns: new[] { "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingEvidenceAssociation_FileId_QuoteId",
                table: "UnderwritingEvidenceAssociation",
                columns: new[] { "FileId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingEvidenceAssociation_LatestReviewId_Id_CycleId_QuoteId",
                table: "UnderwritingEvidenceAssociation",
                columns: new[] { "LatestReviewId", "Id", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingEvidenceAssociation_WithdrawnEventId_Id_CycleId_QuoteId",
                table: "UnderwritingEvidenceAssociation",
                columns: new[] { "WithdrawnEventId", "Id", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingEvidenceEvent_ActorId",
                table: "UnderwritingEvidenceEvent",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingEvidenceEvent_AssociationId_CycleId_QuoteId",
                table: "UnderwritingEvidenceEvent",
                columns: new[] { "AssociationId", "CycleId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingEvidenceEvent_AssociationId_Sequence",
                table: "UnderwritingEvidenceEvent",
                columns: new[] { "AssociationId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingEvidenceEvent_AuthorityVersionId",
                table: "UnderwritingEvidenceEvent",
                column: "AuthorityVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_UnderwritingEvidenceEvent_CreatedBy",
                table: "UnderwritingEvidenceEvent",
                column: "CreatedBy");

            migrationBuilder.AddForeignKey(
                name: "FK_QuoteReferral_QuoteReferralDecision_LatestDecisionId_Id_CycleId_QuoteId",
                table: "QuoteReferral",
                columns: new[] { "LatestDecisionId", "Id", "CycleId", "QuoteId" },
                principalTable: "QuoteReferralDecision",
                principalColumns: new[] { "Id", "ReferralId", "CycleId", "QuoteId" });

            migrationBuilder.AddForeignKey(
                name: "FK_QuoteCondition_QuoteConditionResolution_LatestResolutionId_Id_CycleId_QuoteId",
                table: "QuoteCondition",
                columns: new[] { "LatestResolutionId", "Id", "CycleId", "QuoteId" },
                principalTable: "QuoteConditionResolution",
                principalColumns: new[] { "Id", "ConditionId", "CycleId", "QuoteId" });

            migrationBuilder.AddForeignKey(
                name: "FK_QuoteConditionResolution_UnderwritingEvidenceEvent_EvidenceReviewId_EvidenceAssociationId_CycleId_QuoteId",
                table: "QuoteConditionResolution",
                columns: new[] { "EvidenceReviewId", "EvidenceAssociationId", "CycleId", "QuoteId" },
                principalTable: "UnderwritingEvidenceEvent",
                principalColumns: new[] { "Id", "AssociationId", "CycleId", "QuoteId" });

            migrationBuilder.AddForeignKey(
                name: "FK_UnderwritingEvidenceAssociation_UnderwritingEvidenceEvent_LatestReviewId_Id_CycleId_QuoteId",
                table: "UnderwritingEvidenceAssociation",
                columns: new[] { "LatestReviewId", "Id", "CycleId", "QuoteId" },
                principalTable: "UnderwritingEvidenceEvent",
                principalColumns: new[] { "Id", "AssociationId", "CycleId", "QuoteId" });

            migrationBuilder.AddForeignKey(
                name: "FK_UnderwritingEvidenceAssociation_UnderwritingEvidenceEvent_WithdrawnEventId_Id_CycleId_QuoteId",
                table: "UnderwritingEvidenceAssociation",
                columns: new[] { "WithdrawnEventId", "Id", "CycleId", "QuoteId" },
                principalTable: "UnderwritingEvidenceEvent",
                principalColumns: new[] { "Id", "AssociationId", "CycleId", "QuoteId" });
            AddDecisionGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveDecisionGuards(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_QuoteReferral_QuoteReferralDecision_LatestDecisionId_Id_CycleId_QuoteId",
                table: "QuoteReferral");

            migrationBuilder.DropForeignKey(
                name: "FK_QuoteCondition_QuoteConditionResolution_LatestResolutionId_Id_CycleId_QuoteId",
                table: "QuoteCondition");

            migrationBuilder.DropForeignKey(
                name: "FK_QuoteCondition_QuoteReferralDecision_DecisionId_ReferralId_CycleId_QuoteId",
                table: "QuoteCondition");

            migrationBuilder.DropForeignKey(
                name: "FK_UnderwritingEvidenceAssociation_QuoteCondition_ConditionId_CycleId_QuoteId",
                table: "UnderwritingEvidenceAssociation");

            migrationBuilder.DropForeignKey(
                name: "FK_UnderwritingEvidenceAssociation_UnderwritingEvidenceEvent_LatestReviewId_Id_CycleId_QuoteId",
                table: "UnderwritingEvidenceAssociation");

            migrationBuilder.DropForeignKey(
                name: "FK_UnderwritingEvidenceAssociation_UnderwritingEvidenceEvent_WithdrawnEventId_Id_CycleId_QuoteId",
                table: "UnderwritingEvidenceAssociation");

            migrationBuilder.DropTable(
                name: "QuoteConditionResolution");

            migrationBuilder.DropTable(
                name: "QuoteReferralDecision");

            migrationBuilder.DropTable(
                name: "QuoteCondition");

            migrationBuilder.DropTable(
                name: "UnderwritingEvidenceEvent");

            migrationBuilder.DropTable(
                name: "UnderwritingEvidenceAssociation");

            migrationBuilder.DropIndex(
                name: "IX_QuoteReferral_LatestDecisionId_Id_CycleId_QuoteId",
                table: "QuoteReferral");

            migrationBuilder.DropColumn(
                name: "LatestDecisionId",
                table: "QuoteReferral");
        }
    }
}
