using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperationalDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Document",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Visibility = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Document", x => x.Id);
                    table.CheckConstraint("CK_Document_Audience", "([Visibility] IN ('internal','insurer') AND [RelationshipId] IS NULL) OR ([Visibility]='agency' AND [RelationshipId] IS NOT NULL)");
                    table.CheckConstraint("CK_Document_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_Document_Creator", "[CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_Document_Kind", "[Kind] IN ('quotation','statement-of-fact','policy-schedule','policy-certificate','endorsement','renewal-invitation','cancellation-notice','evidence')");
                    table.ForeignKey(
                        name: "FK_Document_ClientAgencyRelationship_RelationshipId",
                        column: x => x.RelationshipId,
                        principalTable: "ClientAgencyRelationship",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Document_OperationalSubject_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "OperationalSubject",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Document_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DocumentVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    SourceKind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PolicyVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuoteRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuoteTermsVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ServicingTermsVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    TermsHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    TemplateHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    PolicyDocumentRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancellationConsequenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentVersion", x => x.Id);
                    table.CheckConstraint("CK_DocumentVersion_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_DocumentVersion_Identity", "[CreatedBy] IS NOT NULL AND [Number]>0 AND LEN(TRIM([OriginalName]))>0 AND LEN(TRIM([Reason]))>0");
                    table.CheckConstraint("CK_DocumentVersion_Request", "NOT ([PolicyDocumentRequestId] IS NOT NULL AND [CancellationConsequenceId] IS NOT NULL) AND (([PolicyDocumentRequestId] IS NULL AND [CancellationConsequenceId] IS NULL) OR [SourceKind]='policy-version')");
                    table.CheckConstraint("CK_DocumentVersion_Source", "([SourceKind]='policy-version' AND [PolicyVersionId] IS NOT NULL AND [QuoteRevisionId] IS NULL AND [QuoteTermsVersionId] IS NULL AND [ServicingTermsVersionId] IS NULL AND [TermsHash] IS NULL) OR\n([SourceKind]='quote-revision' AND [QuoteRevisionId] IS NOT NULL AND [PolicyVersionId] IS NULL AND [ServicingTermsVersionId] IS NULL AND (([QuoteTermsVersionId] IS NULL AND [TermsHash] IS NULL) OR ([QuoteTermsVersionId] IS NOT NULL AND [TermsHash] IS NOT NULL))) OR\n([SourceKind]='servicing-terms' AND [ServicingTermsVersionId] IS NOT NULL AND [PolicyVersionId] IS NULL AND [QuoteRevisionId] IS NULL AND [QuoteTermsVersionId] IS NULL AND [TermsHash] IS NULL) OR\n([SourceKind]='upload' AND [PolicyVersionId] IS NULL AND [QuoteRevisionId] IS NULL AND [QuoteTermsVersionId] IS NULL AND [ServicingTermsVersionId] IS NULL AND [TermsHash] IS NULL)");
                    table.CheckConstraint("CK_DocumentVersion_SourceHash", "[SourceHash] IS NULL OR (DATALENGTH([SourceHash])=64 AND [SourceHash] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2)");
                    table.CheckConstraint("CK_DocumentVersion_Template", "([SourceKind]='upload' AND [TemplateVersionId] IS NULL AND [SourceHash] IS NULL AND [TemplateHash] IS NULL) OR ([SourceKind]<>'upload' AND [TemplateVersionId] IS NOT NULL AND [SourceHash] IS NOT NULL AND [TemplateHash] IS NOT NULL)");
                    table.CheckConstraint("CK_DocumentVersion_TemplateHash", "[TemplateHash] IS NULL OR (DATALENGTH([TemplateHash])=64 AND [TemplateHash] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2)");
                    table.CheckConstraint("CK_DocumentVersion_TermsHash", "[TermsHash] IS NULL OR (DATALENGTH([TermsHash])=64 AND [TermsHash] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2)");
                    table.ForeignKey(
                        name: "FK_DocumentVersion_CancellationConsequence_CancellationConsequenceId",
                        column: x => x.CancellationConsequenceId,
                        principalTable: "CancellationConsequence",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DocumentVersion_Document_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Document",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DocumentVersion_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DocumentVersion_PolicyDocumentRequest_PolicyDocumentRequestId",
                        column: x => x.PolicyDocumentRequestId,
                        principalTable: "PolicyDocumentRequest",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DocumentVersion_PolicyVersion_PolicyVersionId",
                        column: x => x.PolicyVersionId,
                        principalTable: "PolicyVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DocumentVersion_QuoteRevision_QuoteRevisionId",
                        column: x => x.QuoteRevisionId,
                        principalTable: "QuoteRevision",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DocumentVersion_QuoteTermsVersion_QuoteTermsVersionId",
                        column: x => x.QuoteTermsVersionId,
                        principalTable: "QuoteTermsVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DocumentVersion_ServicingTermsVersion_ServicingTermsVersionId",
                        column: x => x.ServicingTermsVersionId,
                        principalTable: "ServicingTermsVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DocumentVersion_TemplateVersion_TemplateVersionId",
                        column: x => x.TemplateVersionId,
                        principalTable: "TemplateVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DocumentVersion_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DocumentVersionContent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PageCount = table.Column<int>(type: "int", nullable: true),
                    RendererVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProjectionVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FontVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentVersionContent", x => x.Id);
                    table.CheckConstraint("CK_DocumentVersionContent_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_DocumentVersionContent_Creator", "[CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_DocumentVersionContent_Renderer", "([PageCount] IS NULL AND [RendererVersion] IS NULL AND [ProjectionVersion] IS NULL AND [FontVersion] IS NULL) OR ([PageCount] BETWEEN 1 AND 300 AND [RendererVersion] IS NOT NULL AND [ProjectionVersion] IS NOT NULL AND [FontVersion] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_DocumentVersionContent_DocumentVersion_VersionId",
                        column: x => x.VersionId,
                        principalTable: "DocumentVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DocumentVersionContent_FileObject_FileObjectId",
                        column: x => x.FileObjectId,
                        principalTable: "FileObject",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DocumentVersionContent_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Document_CreatedBy",
                table: "Document",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Document_RelationshipId",
                table: "Document",
                column: "RelationshipId");

            migrationBuilder.CreateIndex(
                name: "IX_Document_SubjectId_CreatedAt_Id",
                table: "Document",
                columns: new[] { "SubjectId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersion_CancellationConsequenceId",
                table: "DocumentVersion",
                column: "CancellationConsequenceId",
                unique: true,
                filter: "[CancellationConsequenceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersion_CreatedBy",
                table: "DocumentVersion",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersion_DocumentId_Number",
                table: "DocumentVersion",
                columns: new[] { "DocumentId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersion_PolicyDocumentRequestId",
                table: "DocumentVersion",
                column: "PolicyDocumentRequestId",
                unique: true,
                filter: "[PolicyDocumentRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersion_PolicyVersionId",
                table: "DocumentVersion",
                column: "PolicyVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersion_QuoteRevisionId",
                table: "DocumentVersion",
                column: "QuoteRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersion_QuoteTermsVersionId",
                table: "DocumentVersion",
                column: "QuoteTermsVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersion_ServicingTermsVersionId",
                table: "DocumentVersion",
                column: "ServicingTermsVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersion_TemplateVersionId",
                table: "DocumentVersion",
                column: "TemplateVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersion_WorkId",
                table: "DocumentVersion",
                column: "WorkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersionContent_CreatedBy",
                table: "DocumentVersionContent",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersionContent_FileObjectId",
                table: "DocumentVersionContent",
                column: "FileObjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersionContent_VersionId",
                table: "DocumentVersionContent",
                column: "VersionId",
                unique: true);
            OperationalDocumentStorageGuards.Up(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            OperationalDocumentStorageGuards.BeforeDown(migrationBuilder);
            migrationBuilder.DropTable(
                name: "DocumentVersionContent");

            migrationBuilder.DropTable(
                name: "DocumentVersion");

            migrationBuilder.DropTable(
                name: "Document");
        }
    }
}
