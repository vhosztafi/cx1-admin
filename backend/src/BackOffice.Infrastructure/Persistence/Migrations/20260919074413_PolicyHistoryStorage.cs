using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PolicyHistoryStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_PolicyVersion_Id_PolicyId_ContentHash",
                table: "PolicyVersion",
                columns: new[] { "Id", "PolicyId", "ContentHash" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PolicyVersion_Id_TermId_PolicyId_ContentHash",
                table: "PolicyVersion",
                columns: new[] { "Id", "TermId", "PolicyId", "ContentHash" });

            migrationBuilder.CreateTable(
                name: "PolicyQuoteClone",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ItemMapJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyQuoteClone", x => x.Id);
                    table.CheckConstraint("CK_PolicyQuoteClone_Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId] AND LEN(TRIM([Reason])) BETWEEN 10 AND 2000");
                    table.CheckConstraint("CK_PolicyQuoteClone_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_PolicyQuoteClone_ItemMapJson", "ISJSON([ItemMapJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[ItemMapJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.ForeignKey(
                        name: "FK_PolicyQuoteClone_PolicyVersion_VersionId_PolicyId_VersionHash",
                        columns: x => new { x.VersionId, x.PolicyId, x.VersionHash },
                        principalTable: "PolicyVersion",
                        principalColumns: new[] { "Id", "PolicyId", "ContentHash" });
                    table.ForeignKey(
                        name: "FK_PolicyQuoteClone_QuoteRevision_RevisionId_QuoteId",
                        columns: x => new { x.RevisionId, x.QuoteId },
                        principalTable: "QuoteRevision",
                        principalColumns: new[] { "Id", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_PolicyQuoteClone_User_ActorId",
                        column: x => x.ActorId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PolicyQuoteClone_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PolicyReconstructionRequest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VersionHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    KnownAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CoverageState = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ManifestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ManifestHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyReconstructionRequest", x => x.Id);
                    table.CheckConstraint("CK_PolicyReconstructionRequest_Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId] AND LEN(TRIM([Reason])) BETWEEN 10 AND 2000");
                    table.CheckConstraint("CK_PolicyReconstructionRequest_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_PolicyReconstructionRequest_Cutoffs", "DATEPART(TZOFFSET,[EffectiveAt])=0 AND DATEPART(TZOFFSET,[KnownAt])=0");
                    table.CheckConstraint("CK_PolicyReconstructionRequest_EffectiveAt_Utc", "DATEPART(TZOFFSET,[EffectiveAt]) = 0");
                    table.CheckConstraint("CK_PolicyReconstructionRequest_KnownAt_Utc", "DATEPART(TZOFFSET,[KnownAt]) = 0");
                    table.CheckConstraint("CK_PolicyReconstructionRequest_ManifestHash", "[ManifestHash]=HASHBYTES('SHA2_256',CONVERT(varchar(max),[ManifestJson] COLLATE Latin1_General_100_BIN2_UTF8))");
                    table.CheckConstraint("CK_PolicyReconstructionRequest_ManifestJson", "ISJSON([ManifestJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[ManifestJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_PolicyReconstructionRequest_Selection", "([VersionId] IS NULL AND [VersionHash] IS NULL AND [CoverageState]='not-covered') OR ([VersionId] IS NOT NULL AND [VersionHash] IS NOT NULL AND [CoverageState] IN ('scheduled','active','expired','cancelled'))");
                    table.ForeignKey(
                        name: "FK_PolicyReconstructionRequest_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PolicyReconstructionRequest_PolicyTerm_TermId_PolicyId",
                        columns: x => new { x.TermId, x.PolicyId },
                        principalTable: "PolicyTerm",
                        principalColumns: new[] { "Id", "PolicyId" });
                    table.ForeignKey(
                        name: "FK_PolicyReconstructionRequest_PolicyVersion_VersionId_TermId_PolicyId_VersionHash",
                        columns: x => new { x.VersionId, x.TermId, x.PolicyId, x.VersionHash },
                        principalTable: "PolicyVersion",
                        principalColumns: new[] { "Id", "TermId", "PolicyId", "ContentHash" });
                    table.ForeignKey(
                        name: "FK_PolicyReconstructionRequest_User_ActorId",
                        column: x => x.ActorId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PolicyReconstructionRequest_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyQuoteClone_ActorId",
                table: "PolicyQuoteClone",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyQuoteClone_CreatedBy",
                table: "PolicyQuoteClone",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyQuoteClone_PolicyId_CreatedAt_Id",
                table: "PolicyQuoteClone",
                columns: new[] { "PolicyId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyQuoteClone_QuoteId",
                table: "PolicyQuoteClone",
                column: "QuoteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolicyQuoteClone_RevisionId_QuoteId",
                table: "PolicyQuoteClone",
                columns: new[] { "RevisionId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyQuoteClone_VersionId_PolicyId_VersionHash",
                table: "PolicyQuoteClone",
                columns: new[] { "VersionId", "PolicyId", "VersionHash" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyReconstructionRequest_ActorId",
                table: "PolicyReconstructionRequest",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyReconstructionRequest_CreatedBy",
                table: "PolicyReconstructionRequest",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyReconstructionRequest_PolicyId_CreatedAt_Id",
                table: "PolicyReconstructionRequest",
                columns: new[] { "PolicyId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyReconstructionRequest_TermId_PolicyId",
                table: "PolicyReconstructionRequest",
                columns: new[] { "TermId", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyReconstructionRequest_VersionId_TermId_PolicyId_VersionHash",
                table: "PolicyReconstructionRequest",
                columns: new[] { "VersionId", "TermId", "PolicyId", "VersionHash" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyReconstructionRequest_WorkId",
                table: "PolicyReconstructionRequest",
                column: "WorkId",
                unique: true);
            AddHistoryGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveHistoryGuards(migrationBuilder);
            migrationBuilder.DropTable(
                name: "PolicyQuoteClone");

            migrationBuilder.DropTable(
                name: "PolicyReconstructionRequest");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PolicyVersion_Id_PolicyId_ContentHash",
                table: "PolicyVersion");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PolicyVersion_Id_TermId_PolicyId_ContentHash",
                table: "PolicyVersion");
        }
    }
}
