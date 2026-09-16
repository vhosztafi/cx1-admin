using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuoteLookupStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuoteLookup",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TargetScope = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RiskItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Query = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    InputFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ReferenceVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ScenarioVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scenario = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteLookup", x => x.Id);
                    table.UniqueConstraint("AK_QuoteLookup_Id_QuoteId_RevisionId_InputFingerprint", x => new { x.Id, x.QuoteId, x.RevisionId, x.InputFingerprint });
                    table.CheckConstraint("CK_QuoteLookup_CompletedAt_Utc", "DATEPART(TZOFFSET,[CompletedAt]) = 0");
                    table.CheckConstraint("CK_QuoteLookup_Configuration", "LEN(TRIM([ReferenceVersion]))>0 AND [Scenario] IN ('success','no-match','multiple','reject','fail-once','timeout-after-success') AND [CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_QuoteLookup_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_QuoteLookup_Fingerprint", "LEN([InputFingerprint])=64 AND [InputFingerprint] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_QuoteLookup_Outcome", "([State]='pending' AND [CompletedAt] IS NULL AND [ResultJson] IS NULL) OR ([State] IN ('succeeded','no-match','rejected','failed') AND [CompletedAt] IS NOT NULL AND [CompletedAt]>=[CreatedAt] AND [ResultJson] IS NOT NULL AND ISJSON([ResultJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[ResultJson] COLLATE Latin1_General_100_BIN2_UTF8))<=65536)");
                    table.CheckConstraint("CK_QuoteLookup_Query", "LEN([Query]) BETWEEN 2 AND 40 AND [Query] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_QuoteLookup_Target", "([Kind]='address' AND [TargetScope] IN ('insured','driver','premises') OR [Kind]='vehicle' AND [TargetScope]='vehicle' OR [Kind]='licence' AND [TargetScope]='driver') AND (([TargetScope]='insured' AND [RiskItemId] IS NULL) OR ([TargetScope]<>'insured' AND [RiskItemId] IS NOT NULL AND [RiskItemId]<>'00000000-0000-0000-0000-000000000000'))");
                    table.CheckConstraint("CK_QuoteLookup_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_QuoteLookup_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteLookup_QuoteRevision_RevisionId_QuoteId",
                        columns: x => new { x.RevisionId, x.QuoteId },
                        principalTable: "QuoteRevision",
                        principalColumns: new[] { "Id", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_QuoteLookup_SettingVersion_ScenarioVersionId",
                        column: x => x.ScenarioVersionId,
                        principalTable: "SettingVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteLookup_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuoteLookupSelection",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LookupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InputFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    NewRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ManualReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteLookupSelection", x => x.Id);
                    table.CheckConstraint("CK_QuoteLookupSelection_Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId]");
                    table.CheckConstraint("CK_QuoteLookupSelection_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_QuoteLookupSelection_Decision", "[SourceRevisionId]<>[NewRevisionId] AND (([CandidateId] IS NOT NULL AND [CandidateId]<>'00000000-0000-0000-0000-000000000000' AND [ManualReason] IS NULL) OR ([CandidateId] IS NULL AND [ManualReason] IS NOT NULL AND LEN(TRIM([ManualReason]))>0))");
                    table.ForeignKey(
                        name: "FK_QuoteLookupSelection_QuoteLookup_LookupId_QuoteId_SourceRevisionId_InputFingerprint",
                        columns: x => new { x.LookupId, x.QuoteId, x.SourceRevisionId, x.InputFingerprint },
                        principalTable: "QuoteLookup",
                        principalColumns: new[] { "Id", "QuoteId", "RevisionId", "InputFingerprint" });
                    table.ForeignKey(
                        name: "FK_QuoteLookupSelection_QuoteRevision_NewRevisionId_QuoteId",
                        columns: x => new { x.NewRevisionId, x.QuoteId },
                        principalTable: "QuoteRevision",
                        principalColumns: new[] { "Id", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_QuoteLookupSelection_User_ActorId",
                        column: x => x.ActorId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteLookupSelection_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteLookup_CreatedBy",
                table: "QuoteLookup",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteLookup_QuoteId_RequestHash",
                table: "QuoteLookup",
                columns: new[] { "QuoteId", "RequestHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteLookup_RevisionId_QuoteId",
                table: "QuoteLookup",
                columns: new[] { "RevisionId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteLookup_ScenarioVersionId",
                table: "QuoteLookup",
                column: "ScenarioVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteLookup_WorkId",
                table: "QuoteLookup",
                column: "WorkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteLookupSelection_ActorId",
                table: "QuoteLookupSelection",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteLookupSelection_CreatedBy",
                table: "QuoteLookupSelection",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteLookupSelection_LookupId",
                table: "QuoteLookupSelection",
                column: "LookupId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteLookupSelection_LookupId_QuoteId_SourceRevisionId_InputFingerprint",
                table: "QuoteLookupSelection",
                columns: new[] { "LookupId", "QuoteId", "SourceRevisionId", "InputFingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteLookupSelection_NewRevisionId_QuoteId",
                table: "QuoteLookupSelection",
                columns: new[] { "NewRevisionId", "QuoteId" });

            migrationBuilder.Sql("CREATE TRIGGER [TR_QuoteLookup_InputImmutable] ON [QuoteLookup] AFTER UPDATE AS BEGIN SET NOCOUNT ON; IF UPDATE(Id) OR UPDATE(QuoteId) OR UPDATE(RevisionId) OR UPDATE(Kind) OR UPDATE(TargetScope) OR UPDATE(RiskItemId) OR UPDATE(Query) OR UPDATE(InputFingerprint) OR UPDATE(RequestHash) OR UPDATE(ReferenceVersion) OR UPDATE(ScenarioVersionId) OR UPDATE(Scenario) OR UPDATE(WorkId) OR UPDATE(CreatedBy) OR UPDATE(CreatedAt) THROW 51064, 'Lookup input is immutable.', 1; IF EXISTS(SELECT 1 FROM deleted WHERE State<>'pending') AND (UPDATE(State) OR UPDATE(ResultJson) OR UPDATE(CompletedAt)) THROW 51069, 'Completed lookup outcome is immutable.', 1; END;");
            migrationBuilder.Sql("CREATE TRIGGER [TR_QuoteLookup_NoDelete] ON [QuoteLookup] AFTER DELETE AS BEGIN SET NOCOUNT ON; THROW 51065, 'Lookup history cannot be deleted.', 1; END;");
            migrationBuilder.Sql("CREATE TRIGGER [TR_QuoteLookupSelection_AppendOnly] ON [QuoteLookupSelection] AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; THROW 51066, 'Lookup selections are append-only.', 1; END;");
            migrationBuilder.Sql("CREATE TRIGGER [TR_QuoteLookup_Target] ON [QuoteLookup] AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted i JOIN QuoteRevision r ON r.Id=i.RevisionId AND r.QuoteId=i.QuoteId JOIN OutboxWork w ON w.Id=i.WorkId WHERE w.Kind<>'quote-lookup' OR w.SubjectRecordId IS NULL OR w.SubjectRecordId<>i.Id OR w.ScenarioVersionId IS NULL OR w.ScenarioVersionId<>i.ScenarioVersionId OR (i.TargetScope='insured' AND JSON_QUERY(r.ProposalJson,'$.insured') IS NULL) OR (i.TargetScope<>'insured' AND (SELECT COUNT(*) FROM OPENJSON(r.ProposalJson,CASE i.TargetScope WHEN 'driver' THEN '$.risk.drivers' WHEN 'vehicle' THEN '$.risk.vehicles' ELSE '$.risk.premises' END) a WHERE TRY_CONVERT(uniqueidentifier,JSON_VALUE(a.value,'$.id'))=i.RiskItemId)<>1)) THROW 51067, 'Lookup target or work ownership is invalid.', 1; END;");
            migrationBuilder.Sql("CREATE TRIGGER [TR_QuoteLookupSelection_Outcome] ON [QuoteLookupSelection] AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted i JOIN QuoteLookup l ON l.Id=i.LookupId JOIN QuoteRevision s ON s.Id=i.SourceRevisionId JOIN QuoteRevision n ON n.Id=i.NewRevisionId WHERE n.Number<=s.Number OR (i.CandidateId IS NOT NULL AND (l.State<>'succeeded' OR NOT EXISTS(SELECT 1 FROM OPENJSON(l.ResultJson,'$.candidates') a WHERE TRY_CONVERT(uniqueidentifier,JSON_VALUE(a.value,'$.id'))=i.CandidateId)))) THROW 51068, 'Lookup selection has no applicable outcome.', 1; END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [TR_QuoteLookupSelection_Outcome]; DROP TRIGGER [TR_QuoteLookupSelection_AppendOnly]; DROP TRIGGER [TR_QuoteLookup_Target]; DROP TRIGGER [TR_QuoteLookup_NoDelete]; DROP TRIGGER [TR_QuoteLookup_InputImmutable];");
            migrationBuilder.DropTable(
                name: "QuoteLookupSelection");

            migrationBuilder.DropTable(
                name: "QuoteLookup");
        }
    }
}
