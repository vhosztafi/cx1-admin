using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingEvidenceFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServicingEvidenceFile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ByteLength = table.Column<int>(type: "int", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ScreeningState = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ScreeningMethod = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicingEvidenceFile", x => x.Id);
                    table.UniqueConstraint("AK_ServicingEvidenceFile_Id_DraftId", x => new { x.Id, x.DraftId });
                    table.CheckConstraint("CK_ServicingEvidenceFile_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ServicingEvidenceFile_Hash", "LEN([Sha256])=64 AND [Sha256]=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',[Content]),2))");
                    table.CheckConstraint("CK_ServicingEvidenceFile_Media", "[ContentType] IN ('application/pdf','image/png','image/jpeg','text/plain')");
                    table.CheckConstraint("CK_ServicingEvidenceFile_Name", "LEN(TRIM([FileName]))>0 AND [FileName] NOT LIKE '%/%' AND [FileName] NOT LIKE '%\\%' AND [FileName] NOT LIKE '%:%'");
                    table.CheckConstraint("CK_ServicingEvidenceFile_Screening", "[ScreeningState]='accepted' AND [ScreeningMethod]='demo-signature-v1' AND [CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_ServicingEvidenceFile_Size", "[ByteLength] BETWEEN 1 AND 10485760 AND DATALENGTH([Content])=[ByteLength]");
                    table.ForeignKey(
                        name: "FK_ServicingEvidenceFile_ServicingDraft_DraftId",
                        column: x => x.DraftId,
                        principalTable: "ServicingDraft",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingEvidenceFile_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingEvidenceFile_CreatedBy",
                table: "ServicingEvidenceFile",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingEvidenceFile_DraftId_CreatedAt_Id",
                table: "ServicingEvidenceFile",
                columns: new[] { "DraftId", "CreatedAt", "Id" });
            migrationBuilder.Sql("CREATE TRIGGER TR_ServicingEvidenceFile_AppendOnly ON ServicingEvidenceFile AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51310, 'Servicing evidence files are append-only.', 1; END;");
            migrationBuilder.Sql("CREATE TRIGGER TR_ServicingEvidenceFile_Time ON ServicingEvidenceFile AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingDraft d ON d.Id=i.DraftId WHERE i.CreatedAt<d.CreatedAt) THROW 51311, 'Servicing evidence cannot precede its draft.', 1; END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ServicingEvidenceFile_Time; DROP TRIGGER IF EXISTS TR_ServicingEvidenceFile_AppendOnly;");
            migrationBuilder.DropTable(
                name: "ServicingEvidenceFile");
        }
    }
}
