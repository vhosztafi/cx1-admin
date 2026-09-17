using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingDraftStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServicingDraft",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseTermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    State = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicingDraft", x => x.Id);
                    table.UniqueConstraint("AK_ServicingDraft_Id_PolicyId_BaseTermId_BaseVersionId", x => new { x.Id, x.PolicyId, x.BaseTermId, x.BaseVersionId });
                    table.CheckConstraint("CK_ServicingDraft_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ServicingDraft_Creator", "[CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_ServicingDraft_Kind", "[Kind] IN ('adjustment','renewal','cancellation')");
                    table.CheckConstraint("CK_ServicingDraft_State", "[State] IN ('draft','abandoned')");
                    table.CheckConstraint("CK_ServicingDraft_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_ServicingDraft_PolicyVersion_BaseVersionId_BaseTermId_PolicyId",
                        columns: x => new { x.BaseVersionId, x.BaseTermId, x.PolicyId },
                        principalTable: "PolicyVersion",
                        principalColumns: new[] { "Id", "TermId", "PolicyId" });
                    table.ForeignKey(
                        name: "FK_ServicingDraft_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ServicingLease",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HolderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Token = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Generation = table.Column<int>(type: "int", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicingLease", x => x.Id);
                    table.CheckConstraint("CK_ServicingLease_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ServicingLease_ExpiresAt_Utc", "DATEPART(TZOFFSET,[ExpiresAt]) = 0");
                    table.CheckConstraint("CK_ServicingLease_Fence", "[Generation]>0 AND [Token]<>'00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ServicingLease_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_ServicingLease_ServicingDraft_DraftId",
                        column: x => x.DraftId,
                        principalTable: "ServicingDraft",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingLease_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingLease_User_HolderId",
                        column: x => x.HolderId,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ServicingRevision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProposalJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicingRevision", x => x.Id);
                    table.UniqueConstraint("AK_ServicingRevision_Id_DraftId", x => new { x.Id, x.DraftId });
                    table.CheckConstraint("CK_ServicingRevision_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ServicingRevision_Creator", "[CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_ServicingRevision_ProposalJson", "ISJSON([ProposalJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[ProposalJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_ServicingRevision_Schema", "[SchemaVersion]='1.0' AND COALESCE(JSON_VALUE([ProposalJson],'$.schemaVersion'),'')=[SchemaVersion]");
                    table.CheckConstraint("CK_ServicingRevision_Sequence", "[Sequence]>0");
                    table.ForeignKey(
                        name: "FK_ServicingRevision_ServicingDraft_DraftId",
                        column: x => x.DraftId,
                        principalTable: "ServicingDraft",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingRevision_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingDraft_BaseVersionId_BaseTermId_PolicyId",
                table: "ServicingDraft",
                columns: new[] { "BaseVersionId", "BaseTermId", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingDraft_CreatedBy",
                table: "ServicingDraft",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingDraft_CurrentRevisionId_Id",
                table: "ServicingDraft",
                columns: new[] { "CurrentRevisionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_ServicingDraft_LiveAdjustment",
                table: "ServicingDraft",
                column: "BaseTermId",
                unique: true,
                filter: "[Kind]='adjustment' AND [State]='draft'");

            migrationBuilder.CreateIndex(
                name: "UX_ServicingDraft_LiveRenewal",
                table: "ServicingDraft",
                column: "BaseTermId",
                unique: true,
                filter: "[Kind]='renewal' AND [State]='draft'");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingLease_CreatedBy",
                table: "ServicingLease",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingLease_DraftId",
                table: "ServicingLease",
                column: "DraftId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicingLease_HolderId",
                table: "ServicingLease",
                column: "HolderId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingRevision_CreatedBy",
                table: "ServicingRevision",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingRevision_DraftId_Sequence",
                table: "ServicingRevision",
                columns: new[] { "DraftId", "Sequence" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ServicingDraft_ServicingRevision_CurrentRevisionId_Id",
                table: "ServicingDraft",
                columns: new[] { "CurrentRevisionId", "Id" },
                principalTable: "ServicingRevision",
                principalColumns: new[] { "Id", "DraftId" });
            AddServicingGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServicingDraft_ServicingRevision_CurrentRevisionId_Id",
                table: "ServicingDraft");

            migrationBuilder.DropTable(
                name: "ServicingLease");

            migrationBuilder.DropTable(
                name: "ServicingRevision");

            migrationBuilder.DropTable(
                name: "ServicingDraft");
        }
    }
}
