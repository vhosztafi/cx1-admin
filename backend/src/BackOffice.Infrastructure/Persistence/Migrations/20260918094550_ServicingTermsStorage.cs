using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingTermsStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TemplateVersion_Kind",
                table: "TemplateVersion");

            migrationBuilder.AddColumn<Guid>(
                name: "CurrentTermsVersionId",
                table: "ServicingCycle",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServicingTermsVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    TermsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TermsHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AssuranceHashAtPreparation = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PreparedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreparedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicingTermsVersion", x => x.Id);
                    table.UniqueConstraint("AK_ServicingTermsVersion_Id_CycleId_DraftId_RevisionId_RatingId", x => new { x.Id, x.CycleId, x.DraftId, x.RevisionId, x.RatingId });
                    table.CheckConstraint("CK_ServicingTermsVersion_AssuranceHashAtPreparation", "LEN([AssuranceHashAtPreparation])=64 AND [AssuranceHashAtPreparation] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_ServicingTermsVersion_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ServicingTermsVersion_Identity", "[Id]<>'00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ServicingTermsVersion_PayloadHash", "[TermsHash]=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varchar(max),[TermsJson] COLLATE Latin1_General_100_BIN2_UTF8)),2))");
                    table.CheckConstraint("CK_ServicingTermsVersion_PreparedAt_Utc", "DATEPART(TZOFFSET,[PreparedAt]) = 0");
                    table.CheckConstraint("CK_ServicingTermsVersion_Provenance", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[PreparedBy] AND [PreparedAt]=[CreatedAt]");
                    table.CheckConstraint("CK_ServicingTermsVersion_Sequence", "[Sequence]>0");
                    table.CheckConstraint("CK_ServicingTermsVersion_TermsHash", "LEN([TermsHash])=64 AND [TermsHash] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_ServicingTermsVersion_TermsJson", "ISJSON([TermsJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[TermsJson] COLLATE Latin1_General_100_BIN2_UTF8))<=8388608");
                    table.ForeignKey(
                        name: "FK_ServicingTermsVersion_PolicyVersion_BaseVersionId",
                        column: x => x.BaseVersionId,
                        principalTable: "PolicyVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingTermsVersion_ServicingRatingResult_RatingId_CycleId_DraftId_RevisionId",
                        columns: x => new { x.RatingId, x.CycleId, x.DraftId, x.RevisionId },
                        principalTable: "ServicingRatingResult",
                        principalColumns: new[] { "Id", "CycleId", "DraftId", "RevisionId" });
                    table.ForeignKey(
                        name: "FK_ServicingTermsVersion_TemplateVersion_TemplateVersionId",
                        column: x => x.TemplateVersionId,
                        principalTable: "TemplateVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingTermsVersion_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingTermsVersion_User_PreparedBy",
                        column: x => x.PreparedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TemplateVersion_Kind",
                table: "TemplateVersion",
                sql: "[Kind] IN ('quote-terms','servicing-terms','policy-schedule','policy-certificate','policy-statement')");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCycle_CurrentTermsVersionId_Id_DraftId_RevisionId_CurrentRatingId",
                table: "ServicingCycle",
                columns: new[] { "CurrentTermsVersionId", "Id", "DraftId", "RevisionId", "CurrentRatingId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingTermsVersion_BaseVersionId",
                table: "ServicingTermsVersion",
                column: "BaseVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingTermsVersion_CreatedBy",
                table: "ServicingTermsVersion",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingTermsVersion_CycleId_Sequence",
                table: "ServicingTermsVersion",
                columns: new[] { "CycleId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicingTermsVersion_DraftId_CreatedAt_Id",
                table: "ServicingTermsVersion",
                columns: new[] { "DraftId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingTermsVersion_PreparedBy",
                table: "ServicingTermsVersion",
                column: "PreparedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingTermsVersion_RatingId_CycleId_DraftId_RevisionId",
                table: "ServicingTermsVersion",
                columns: new[] { "RatingId", "CycleId", "DraftId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingTermsVersion_TemplateVersionId",
                table: "ServicingTermsVersion",
                column: "TemplateVersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_ServicingCycle_ServicingTermsVersion_CurrentTermsVersionId_Id_DraftId_RevisionId_CurrentRatingId",
                table: "ServicingCycle",
                columns: new[] { "CurrentTermsVersionId", "Id", "DraftId", "RevisionId", "CurrentRatingId" },
                principalTable: "ServicingTermsVersion",
                principalColumns: new[] { "Id", "CycleId", "DraftId", "RevisionId", "RatingId" });
            ServicingTermsStorageGuards.Up(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ServicingTermsStorageGuards.Down(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_ServicingCycle_ServicingTermsVersion_CurrentTermsVersionId_Id_DraftId_RevisionId_CurrentRatingId",
                table: "ServicingCycle");

            migrationBuilder.DropTable(
                name: "ServicingTermsVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TemplateVersion_Kind",
                table: "TemplateVersion");

            migrationBuilder.DropIndex(
                name: "IX_ServicingCycle_CurrentTermsVersionId_Id_DraftId_RevisionId_CurrentRatingId",
                table: "ServicingCycle");

            migrationBuilder.DropColumn(
                name: "CurrentTermsVersionId",
                table: "ServicingCycle");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TemplateVersion_Kind",
                table: "TemplateVersion",
                sql: "[Kind] IN ('quote-terms','policy-schedule','policy-certificate','policy-statement')");
        }
    }
}
