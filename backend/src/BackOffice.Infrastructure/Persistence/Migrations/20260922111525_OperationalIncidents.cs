using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperationalIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "IncidentReferenceSequence");

            migrationBuilder.CreateTable(
                name: "Incident",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrentResolutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incident", x => x.Id);
                    table.CheckConstraint("CK_Incident_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_Incident_Creator", "[CreatedBy] IS NOT NULL AND LEN(TRIM([Reference]))>0");
                    table.CheckConstraint("CK_Incident_Product", "[ProductCode] IN ('motor-trade-road-risks','motor-trade-combined','commercial-combined')");
                    table.CheckConstraint("CK_Incident_State", "[State] IN ('draft','logged','queued','handed-off','failed')");
                    table.CheckConstraint("CK_Incident_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_Incident_Policy_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policy",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Incident_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "IncidentRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    DraftJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    AuthorLabel = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentRevision", x => x.Id);
                    table.CheckConstraint("CK_IncidentRevision_Content", "[CreatedBy] IS NOT NULL AND [Number]>0 AND ISJSON([DraftJson])=1 AND DATALENGTH([DraftJson])<=131072 AND LEN([ContentHash])=64 AND LEN(TRIM([Reason]))>0 AND LEN(TRIM([AuthorLabel]))>0");
                    table.CheckConstraint("CK_IncidentRevision_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_IncidentRevision_Incident_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incident",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IncidentRevision_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "IncidentEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentEvidence", x => x.Id);
                    table.CheckConstraint("CK_IncidentEvidence_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_IncidentEvidence_Creator", "[CreatedBy] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_IncidentEvidence_DocumentVersion_DocumentVersionId",
                        column: x => x.DocumentVersionId,
                        principalTable: "DocumentVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IncidentEvidence_IncidentRevision_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "IncidentRevision",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IncidentEvidence_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "IncidentOccurrenceResolution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KnownAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    State = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ResolutionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentOccurrenceResolution", x => x.Id);
                    table.CheckConstraint("CK_IncidentOccurrenceResolution_Content", "[CreatedBy] IS NOT NULL AND ISJSON([ResolutionJson])=1 AND LEN([ContentHash])=64 AND [KnownAt]<=[CreatedAt]");
                    table.CheckConstraint("CK_IncidentOccurrenceResolution_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_IncidentOccurrenceResolution_KnownAt_Utc", "DATEPART(TZOFFSET,[KnownAt]) = 0");
                    table.CheckConstraint("CK_IncidentOccurrenceResolution_State", "[State] IN ('resolved','ambiguous','partly-uncovered','uncovered','incomplete')");
                    table.ForeignKey(
                        name: "FK_IncidentOccurrenceResolution_IncidentRevision_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "IncidentRevision",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IncidentOccurrenceResolution_Incident_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incident",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IncidentOccurrenceResolution_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "IncidentResolutionSource",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResolutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    From = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    To = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentResolutionSource", x => x.Id);
                    table.CheckConstraint("CK_IncidentResolutionSource_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_IncidentResolutionSource_From_Utc", "DATEPART(TZOFFSET,[From]) = 0");
                    table.CheckConstraint("CK_IncidentResolutionSource_Interval", "[From]<=[To] AND LEN([SourceHash])=64 AND [CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_IncidentResolutionSource_To_Utc", "DATEPART(TZOFFSET,[To]) = 0");
                    table.ForeignKey(
                        name: "FK_IncidentResolutionSource_IncidentOccurrenceResolution_ResolutionId",
                        column: x => x.ResolutionId,
                        principalTable: "IncidentOccurrenceResolution",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IncidentResolutionSource_PolicyVersion_VersionId",
                        column: x => x.VersionId,
                        principalTable: "PolicyVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IncidentResolutionSource_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Incident_CreatedBy",
                table: "Incident",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Incident_CurrentResolutionId",
                table: "Incident",
                column: "CurrentResolutionId");

            migrationBuilder.CreateIndex(
                name: "IX_Incident_CurrentRevisionId",
                table: "Incident",
                column: "CurrentRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_Incident_PolicyId_CreatedAt_Id",
                table: "Incident",
                columns: new[] { "PolicyId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Incident_Reference",
                table: "Incident",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncidentEvidence_CreatedBy",
                table: "IncidentEvidence",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentEvidence_DocumentVersionId",
                table: "IncidentEvidence",
                column: "DocumentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentEvidence_RevisionId_DocumentVersionId",
                table: "IncidentEvidence",
                columns: new[] { "RevisionId", "DocumentVersionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncidentOccurrenceResolution_CreatedBy",
                table: "IncidentOccurrenceResolution",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentOccurrenceResolution_IncidentId",
                table: "IncidentOccurrenceResolution",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentOccurrenceResolution_RevisionId_CreatedAt_Id",
                table: "IncidentOccurrenceResolution",
                columns: new[] { "RevisionId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResolutionSource_CreatedBy",
                table: "IncidentResolutionSource",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResolutionSource_ResolutionId_VersionId_From_To",
                table: "IncidentResolutionSource",
                columns: new[] { "ResolutionId", "VersionId", "From", "To" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncidentResolutionSource_VersionId",
                table: "IncidentResolutionSource",
                column: "VersionId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentRevision_CreatedBy",
                table: "IncidentRevision",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentRevision_IncidentId_Number",
                table: "IncidentRevision",
                columns: new[] { "IncidentId", "Number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Incident_IncidentOccurrenceResolution_CurrentResolutionId",
                table: "Incident",
                column: "CurrentResolutionId",
                principalTable: "IncidentOccurrenceResolution",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Incident_IncidentRevision_CurrentRevisionId",
                table: "Incident",
                column: "CurrentRevisionId",
                principalTable: "IncidentRevision",
                principalColumn: "Id");
            AddIncidentGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM Incident) THROW 52000,'Cannot remove retained incident history.',1;");
            migrationBuilder.DropForeignKey(
                name: "FK_Incident_IncidentOccurrenceResolution_CurrentResolutionId",
                table: "Incident");

            migrationBuilder.DropForeignKey(
                name: "FK_Incident_IncidentRevision_CurrentRevisionId",
                table: "Incident");

            migrationBuilder.DropTable(
                name: "IncidentEvidence");

            migrationBuilder.DropTable(
                name: "IncidentResolutionSource");

            migrationBuilder.DropTable(
                name: "IncidentOccurrenceResolution");

            migrationBuilder.DropTable(
                name: "IncidentRevision");

            migrationBuilder.DropTable(
                name: "Incident");

            migrationBuilder.DropSequence(
                name: "IncidentReferenceSequence");
        }
    }
}
