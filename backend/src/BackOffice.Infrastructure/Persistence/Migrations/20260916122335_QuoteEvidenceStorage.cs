using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuoteEvidenceStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuoteEvidenceFile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
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
                    table.PrimaryKey("PK_QuoteEvidenceFile", x => x.Id);
                    table.UniqueConstraint("AK_QuoteEvidenceFile_Id_QuoteId", x => new { x.Id, x.QuoteId });
                    table.CheckConstraint("CK_QuoteEvidenceFile_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_QuoteEvidenceFile_Hash", "LEN([Sha256])=64 AND [Sha256]=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',[Content]),2))");
                    table.CheckConstraint("CK_QuoteEvidenceFile_Media", "[ContentType] IN ('application/pdf','image/png','image/jpeg','text/plain')");
                    table.CheckConstraint("CK_QuoteEvidenceFile_Name", "LEN(TRIM([FileName]))>0 AND [FileName] NOT LIKE '%/%' AND [FileName] NOT LIKE '%\\%' AND [FileName] NOT LIKE '%:%'");
                    table.CheckConstraint("CK_QuoteEvidenceFile_Screening", "[ScreeningState]='accepted' AND [ScreeningMethod]='demo-signature-v1' AND [CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_QuoteEvidenceFile_Size", "[ByteLength] BETWEEN 1 AND 10485760 AND DATALENGTH([Content])=[ByteLength]");
                    table.ForeignKey(
                        name: "FK_QuoteEvidenceFile_Quote_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "Quote",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteEvidenceFile_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuoteCaptureEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequirementCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RiskItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InputFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteCaptureEvidence", x => x.Id);
                    table.UniqueConstraint("AK_QuoteCaptureEvidence_Id_QuoteId", x => new { x.Id, x.QuoteId });
                    table.CheckConstraint("CK_QuoteCaptureEvidence_Attestation", "LEN(TRIM([Reason]))>0 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId]");
                    table.CheckConstraint("CK_QuoteCaptureEvidence_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_QuoteCaptureEvidence_Fingerprint", "LEN([InputFingerprint])=64 AND [InputFingerprint] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_QuoteCaptureEvidence_Target", "([RequirementCode] IN ('motor-trader-proof','no-claims-proof') AND [RiskItemId] IS NULL) OR ([RequirementCode] IN ('photocard-both-sides','driving-record') AND [RiskItemId] IS NOT NULL AND [RiskItemId]<>'00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("CK_QuoteCaptureEvidence_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_QuoteCaptureEvidence_QuoteEvidenceFile_FileId_QuoteId",
                        columns: x => new { x.FileId, x.QuoteId },
                        principalTable: "QuoteEvidenceFile",
                        principalColumns: new[] { "Id", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_QuoteCaptureEvidence_QuoteRevision_RevisionId_QuoteId",
                        columns: x => new { x.RevisionId, x.QuoteId },
                        principalTable: "QuoteRevision",
                        principalColumns: new[] { "Id", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_QuoteCaptureEvidence_User_ActorId",
                        column: x => x.ActorId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteCaptureEvidence_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QuoteEvidenceWithdrawal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteEvidenceWithdrawal", x => x.Id);
                    table.CheckConstraint("CK_QuoteEvidenceWithdrawal_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_QuoteEvidenceWithdrawal_Reason", "LEN(TRIM([Reason]))>0 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId]");
                    table.ForeignKey(
                        name: "FK_QuoteEvidenceWithdrawal_QuoteCaptureEvidence_EvidenceId_QuoteId",
                        columns: x => new { x.EvidenceId, x.QuoteId },
                        principalTable: "QuoteCaptureEvidence",
                        principalColumns: new[] { "Id", "QuoteId" });
                    table.ForeignKey(
                        name: "FK_QuoteEvidenceWithdrawal_User_ActorId",
                        column: x => x.ActorId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QuoteEvidenceWithdrawal_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteCaptureEvidence_ActorId",
                table: "QuoteCaptureEvidence",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteCaptureEvidence_CreatedBy",
                table: "QuoteCaptureEvidence",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteCaptureEvidence_FileId_QuoteId",
                table: "QuoteCaptureEvidence",
                columns: new[] { "FileId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteCaptureEvidence_QuoteId_CreatedAt_Id",
                table: "QuoteCaptureEvidence",
                columns: new[] { "QuoteId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteCaptureEvidence_RevisionId_QuoteId",
                table: "QuoteCaptureEvidence",
                columns: new[] { "RevisionId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteEvidenceFile_CreatedBy",
                table: "QuoteEvidenceFile",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteEvidenceFile_QuoteId_CreatedAt_Id",
                table: "QuoteEvidenceFile",
                columns: new[] { "QuoteId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteEvidenceWithdrawal_ActorId",
                table: "QuoteEvidenceWithdrawal",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteEvidenceWithdrawal_CreatedBy",
                table: "QuoteEvidenceWithdrawal",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteEvidenceWithdrawal_EvidenceId",
                table: "QuoteEvidenceWithdrawal",
                column: "EvidenceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteEvidenceWithdrawal_EvidenceId_QuoteId",
                table: "QuoteEvidenceWithdrawal",
                columns: new[] { "EvidenceId", "QuoteId" });
            foreach (var table in new[] { "QuoteEvidenceFile", "QuoteCaptureEvidence", "QuoteEvidenceWithdrawal" })
                migrationBuilder.Sql($"CREATE TRIGGER TR_{table}_AppendOnly ON [{table}] AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51080, 'Quote evidence history is append-only.', 1; END;");
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_QuoteCaptureEvidence_Target ON QuoteCaptureEvidence AFTER INSERT AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted i JOIN QuoteRevision r ON r.Id=i.RevisionId AND r.QuoteId=i.QuoteId
                    WHERE i.RiskItemId IS NOT NULL AND (SELECT COUNT(*) FROM OPENJSON(r.ProposalJson,'$.risk.drivers')
                      WITH(Id uniqueidentifier '$.id') d WHERE d.Id=i.RiskItemId)<>1)
                    THROW 51081, 'Evidence target must be an actual unique source driver.', 1;
                  IF EXISTS(SELECT 1 FROM inserted i JOIN QuoteEvidenceFile f ON f.Id=i.FileId AND f.QuoteId=i.QuoteId
                    WHERE f.ScreeningState<>'accepted' OR i.CreatedAt<f.CreatedAt)
                    THROW 51082, 'Evidence requires an earlier owned screened file.', 1;
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_QuoteEvidenceWithdrawal_Time ON QuoteEvidenceWithdrawal AFTER INSERT AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted i JOIN QuoteCaptureEvidence e ON e.Id=i.EvidenceId AND e.QuoteId=i.QuoteId WHERE i.CreatedAt<e.CreatedAt)
                    THROW 51083, 'Withdrawal cannot precede the attestation.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_QuoteEvidenceWithdrawal_Time; DROP TRIGGER IF EXISTS TR_QuoteCaptureEvidence_Target; DROP TRIGGER IF EXISTS TR_QuoteEvidenceWithdrawal_AppendOnly; DROP TRIGGER IF EXISTS TR_QuoteCaptureEvidence_AppendOnly; DROP TRIGGER IF EXISTS TR_QuoteEvidenceFile_AppendOnly;");

            migrationBuilder.DropTable(
                name: "QuoteEvidenceWithdrawal");

            migrationBuilder.DropTable(
                name: "QuoteCaptureEvidence");

            migrationBuilder.DropTable(
                name: "QuoteEvidenceFile");
        }
    }
}
