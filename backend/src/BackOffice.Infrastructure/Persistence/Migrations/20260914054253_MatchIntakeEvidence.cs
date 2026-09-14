using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MatchIntakeEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_ClientAgencyRelationship_Id_ClientId_AgencyId",
                table: "ClientAgencyRelationship",
                columns: new[] { "Id", "ClientId", "AgencyId" });

            migrationBuilder.CreateTable(
                name: "MatchSubmission",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdentitySnapshot = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    LinkedClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LinkedRelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SeparateClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchSubmission", x => x.Id);
                    table.CheckConstraint("CK_MatchSubmission_Association", "([LinkedClientId] IS NULL AND [LinkedRelationshipId] IS NULL) OR ([LinkedClientId] IS NOT NULL AND [LinkedRelationshipId] IS NOT NULL)");
                    table.CheckConstraint("CK_MatchSubmission_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_MatchSubmission_IdentitySnapshot_Json", "ISJSON([IdentitySnapshot]) = 1");
                    table.CheckConstraint("CK_MatchSubmission_Reference", "LEN(TRIM([Reference]))>0");
                    table.CheckConstraint("CK_MatchSubmission_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_MatchSubmission_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MatchSubmission_ClientAccount_SeparateClientId",
                        column: x => x.SeparateClientId,
                        principalTable: "ClientAccount",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MatchSubmission_ClientAgencyRelationship_LinkedRelationshipId_LinkedClientId_AgencyId",
                        columns: x => new { x.LinkedRelationshipId, x.LinkedClientId, x.AgencyId },
                        principalTable: "ClientAgencyRelationship",
                        principalColumns: new[] { "Id", "ClientId", "AgencyId" });
                    table.ForeignKey(
                        name: "FK_MatchSubmission_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MatchReview",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CandidateClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CandidateRelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleSnapshot = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    Signals = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Confidence = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    State = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchReview", x => x.Id);
                    table.CheckConstraint("CK_MatchReview_Confidence", "[Confidence] IN ('low','medium','high')");
                    table.CheckConstraint("CK_MatchReview_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_MatchReview_RuleIdentity", "COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE([RuleSnapshot],'$.id')),'00000000-0000-0000-0000-000000000000')=[RuleVersionId] AND COALESCE(TRY_CONVERT(int,JSON_VALUE([RuleSnapshot],'$.version')),0)>0");
                    table.CheckConstraint("CK_MatchReview_RuleSnapshot_Json", "ISJSON([RuleSnapshot]) = 1");
                    table.CheckConstraint("CK_MatchReview_Signals_Json", "ISJSON([Signals]) = 1");
                    table.CheckConstraint("CK_MatchReview_SignalsShape", "ISJSON([Signals],ARRAY)=1 AND DATALENGTH([Signals])<=2500000");
                    table.CheckConstraint("CK_MatchReview_State", "[State] IN ('pending','linked','separate','declined','queried')");
                    table.CheckConstraint("CK_MatchReview_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_MatchReview_ClientAgencyRelationship_CandidateRelationshipId_CandidateClientId",
                        columns: x => new { x.CandidateRelationshipId, x.CandidateClientId },
                        principalTable: "ClientAgencyRelationship",
                        principalColumns: new[] { "Id", "ClientId" });
                    table.ForeignKey(
                        name: "FK_MatchReview_MatchSubmission_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "MatchSubmission",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MatchReview_SettingVersion_RuleVersionId",
                        column: x => x.RuleVersionId,
                        principalTable: "SettingVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MatchReview_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MatchInformationRequest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeliveryState = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchInformationRequest", x => x.Id);
                    table.UniqueConstraint("AK_MatchInformationRequest_Id_MatchId", x => new { x.Id, x.MatchId });
                    table.CheckConstraint("CK_MatchInformationRequest_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_MatchInformationRequest_DeliveryState", "[DeliveryState]='recorded'");
                    table.CheckConstraint("CK_MatchInformationRequest_Description", "LEN(TRIM([Description]))>0");
                    table.CheckConstraint("CK_MatchInformationRequest_RecordedAt_Utc", "DATEPART(TZOFFSET,[RecordedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_MatchInformationRequest_MatchReview_MatchId",
                        column: x => x.MatchId,
                        principalTable: "MatchReview",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MatchInformationRequest_User_ActorId",
                        column: x => x.ActorId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MatchInformationRequest_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MatchDecision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InformationRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchDecision", x => x.Id);
                    table.CheckConstraint("CK_MatchDecision_Association", "([ClientId] IS NULL AND [RelationshipId] IS NULL AND [Outcome] NOT IN ('link','separate')) OR ([ClientId] IS NOT NULL AND [RelationshipId] IS NOT NULL)");
                    table.CheckConstraint("CK_MatchDecision_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_MatchDecision_InformationRequest", "([Outcome]='query' AND [InformationRequestId] IS NOT NULL) OR ([Outcome]<>'query' AND [InformationRequestId] IS NULL)");
                    table.CheckConstraint("CK_MatchDecision_OccurredAt_Utc", "DATEPART(TZOFFSET,[OccurredAt]) = 0");
                    table.CheckConstraint("CK_MatchDecision_Outcome", "[Outcome] IN ('link','separate','decline','query','reopen')");
                    table.CheckConstraint("CK_MatchDecision_Reason", "LEN(TRIM([Reason]))>0");
                    table.ForeignKey(
                        name: "FK_MatchDecision_ClientAgencyRelationship_RelationshipId_ClientId",
                        columns: x => new { x.RelationshipId, x.ClientId },
                        principalTable: "ClientAgencyRelationship",
                        principalColumns: new[] { "Id", "ClientId" });
                    table.ForeignKey(
                        name: "FK_MatchDecision_MatchInformationRequest_InformationRequestId_MatchId",
                        columns: x => new { x.InformationRequestId, x.MatchId },
                        principalTable: "MatchInformationRequest",
                        principalColumns: new[] { "Id", "MatchId" });
                    table.ForeignKey(
                        name: "FK_MatchDecision_MatchReview_MatchId",
                        column: x => x.MatchId,
                        principalTable: "MatchReview",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MatchDecision_User_ActorId",
                        column: x => x.ActorId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MatchDecision_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchDecision_ActorId",
                table: "MatchDecision",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchDecision_CreatedBy",
                table: "MatchDecision",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_MatchDecision_InformationRequestId",
                table: "MatchDecision",
                column: "InformationRequestId",
                unique: true,
                filter: "[InformationRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MatchDecision_InformationRequestId_MatchId",
                table: "MatchDecision",
                columns: new[] { "InformationRequestId", "MatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchDecision_MatchId_OccurredAt_Id",
                table: "MatchDecision",
                columns: new[] { "MatchId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchDecision_RelationshipId_ClientId",
                table: "MatchDecision",
                columns: new[] { "RelationshipId", "ClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchInformationRequest_ActorId",
                table: "MatchInformationRequest",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchInformationRequest_CreatedBy",
                table: "MatchInformationRequest",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_MatchInformationRequest_MatchId_RecordedAt_Id",
                table: "MatchInformationRequest",
                columns: new[] { "MatchId", "RecordedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchReview_CandidateRelationshipId_CandidateClientId",
                table: "MatchReview",
                columns: new[] { "CandidateRelationshipId", "CandidateClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchReview_CreatedBy",
                table: "MatchReview",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_MatchReview_RuleVersionId",
                table: "MatchReview",
                column: "RuleVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchReview_State_CreatedAt_Id",
                table: "MatchReview",
                columns: new[] { "State", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchReview_SubmissionId",
                table: "MatchReview",
                column: "SubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchSubmission_AgencyId_CreatedAt_Id",
                table: "MatchSubmission",
                columns: new[] { "AgencyId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchSubmission_CreatedBy",
                table: "MatchSubmission",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_MatchSubmission_LinkedRelationshipId_LinkedClientId_AgencyId",
                table: "MatchSubmission",
                columns: new[] { "LinkedRelationshipId", "LinkedClientId", "AgencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchSubmission_Reference",
                table: "MatchSubmission",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchSubmission_SeparateClientId",
                table: "MatchSubmission",
                column: "SeparateClientId");
            AddEvidenceGuards(migrationBuilder);
        }

        private static void AddEvidenceGuards(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_MatchSubmission_Evidence] ON [MatchSubmission] AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL)
                    THROW 51006, 'Submitted evidence cannot be deleted.', 1;
                  IF EXISTS(SELECT 1 FROM deleted) AND (UPDATE(Reference) OR UPDATE(AgencyId) OR UPDATE(IdentitySnapshot) OR UPDATE(CreatedAt) OR UPDATE(CreatedBy))
                    THROW 51006, 'Submitted evidence is immutable.', 1;
                  IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE d.SeparateClientId IS NOT NULL AND (i.SeparateClientId IS NULL OR i.SeparateClientId<>d.SeparateClientId))
                    THROW 51006, 'The separately created account must be retained.', 1;
                  IF EXISTS(SELECT 1 FROM inserted WHERE ISJSON(IdentitySnapshot,OBJECT)<>1 OR DATALENGTH(IdentitySnapshot)>32000)
                    THROW 51006, 'Submitted identity must be a bounded object.', 1;
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_MatchReview_Evidence] ON [MatchReview] AFTER INSERT, UPDATE, DELETE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL)
                    THROW 51007, 'Review evidence cannot be deleted.', 1;
                  IF EXISTS(SELECT 1 FROM deleted) AND (UPDATE(SubmissionId) OR UPDATE(CandidateClientId) OR UPDATE(CandidateRelationshipId) OR UPDATE(RuleVersionId) OR UPDATE(RuleSnapshot) OR UPDATE(Signals) OR UPDATE(Confidence) OR UPDATE(CreatedAt) OR UPDATE(CreatedBy))
                    THROW 51007, 'Review evidence is immutable.', 1;
                  IF EXISTS(SELECT 1 FROM inserted i JOIN SettingVersion s ON s.Id=i.RuleVersionId WHERE s.Scope<>'matching-rule' OR s.Version<>TRY_CONVERT(int,JSON_VALUE(i.RuleSnapshot,'$.version')) OR ISJSON(i.RuleSnapshot,OBJECT)<>1 OR DATALENGTH(i.RuleSnapshot)>16000)
                    THROW 51007, 'Pin the matching rule version used for this review.', 1;
                  IF EXISTS(SELECT 1 FROM inserted i WHERE (SELECT COUNT(*) FROM OPENJSON(i.Signals)) NOT BETWEEN 1 AND 100)
                    THROW 51007, 'Supply bounded comparison evidence.', 1;
                END;
                """);
            migrationBuilder.Sql("CREATE TRIGGER [TR_MatchDecision_AppendOnly] ON [MatchDecision] AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; THROW 51008, 'Match decisions are append-only.', 1; END;");
            migrationBuilder.Sql("CREATE TRIGGER [TR_MatchInformationRequest_AppendOnly] ON [MatchInformationRequest] AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; THROW 51009, 'Recorded information requests are append-only.', 1; END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [TR_MatchSubmission_Evidence]; DROP TRIGGER [TR_MatchReview_Evidence]; DROP TRIGGER [TR_MatchDecision_AppendOnly]; DROP TRIGGER [TR_MatchInformationRequest_AppendOnly];");
            migrationBuilder.DropTable(
                name: "MatchDecision");

            migrationBuilder.DropTable(
                name: "MatchInformationRequest");

            migrationBuilder.DropTable(
                name: "MatchReview");

            migrationBuilder.DropTable(
                name: "MatchSubmission");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ClientAgencyRelationship_Id_ClientId_AgencyId",
                table: "ClientAgencyRelationship");
        }
    }
}
