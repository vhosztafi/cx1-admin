using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgencyEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgencyEvidenceFile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ByteLength = table.Column<int>(type: "int", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScreeningState = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgencyEvidenceFile", x => x.Id);
                    table.UniqueConstraint("AK_AgencyEvidenceFile_Id_AgencyId", x => new { x.Id, x.AgencyId });
                    table.CheckConstraint("CK_AgencyEvidenceFile_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_AgencyEvidenceFile_Hash", "LEN([Sha256]) = 64 AND [Sha256] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_AgencyEvidenceFile_Length", "[ByteLength] BETWEEN 1 AND 10485760 AND DATALENGTH([Content]) = [ByteLength]");
                    table.CheckConstraint("CK_AgencyEvidenceFile_Screen", "[ScreeningState] IN ('pending','demo-cleared','rejected')");
                    table.CheckConstraint("CK_AgencyEvidenceFile_Type", "[ContentType] IN ('application/pdf','image/png','image/jpeg','text/plain')");
                    table.ForeignKey(
                        name: "FK_AgencyEvidenceFile_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyEvidenceFile_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AgencyEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    InputFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    InputSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RuleVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AttestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ResultCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgencyEvidence", x => x.Id);
                    table.UniqueConstraint("AK_AgencyEvidence_Id_AgencyId", x => new { x.Id, x.AgencyId });
                    table.CheckConstraint("CK_AgencyEvidence_Attestation", "[Kind] NOT IN ('toba','professional-indemnity','dpa','client-money') OR ([FileId] IS NOT NULL AND [AttestedBy] IS NOT NULL AND [Notes] IS NOT NULL AND LEN(TRIM([Notes])) > 0)");
                    table.CheckConstraint("CK_AgencyEvidence_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_AgencyEvidence_Fingerprint", "LEN([InputFingerprint]) = 64 AND [InputFingerprint] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_AgencyEvidence_InputSnapshot_Json", "ISJSON([InputSnapshot]) = 1");
                    table.CheckConstraint("CK_AgencyEvidence_Kind", "[Kind] IN ('fca','toba','professional-indemnity','financial-check','sanctions','ownership','dpa','client-money')");
                    table.CheckConstraint("CK_AgencyEvidence_SnapshotBounds", "DATALENGTH([InputSnapshot]) <= 131072 AND LEFT(LTRIM([InputSnapshot]),1) = '{'");
                    table.CheckConstraint("CK_AgencyEvidence_State", "[State] IN ('pending','verified','rejected','unavailable')");
                    table.CheckConstraint("CK_AgencyEvidence_Verified", "([State] = 'verified' AND [VerifiedAt] IS NOT NULL) OR ([State] <> 'verified' AND [VerifiedAt] IS NULL)");
                    table.CheckConstraint("CK_AgencyEvidence_VerifiedAt_Utc", "DATEPART(TZOFFSET,[VerifiedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_AgencyEvidence_AgencyEvidenceFile_FileId_AgencyId",
                        columns: x => new { x.FileId, x.AgencyId },
                        principalTable: "AgencyEvidenceFile",
                        principalColumns: new[] { "Id", "AgencyId" });
                    table.ForeignKey(
                        name: "FK_AgencyEvidence_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyEvidence_SettingVersion_RuleVersionId",
                        column: x => x.RuleVersionId,
                        principalTable: "SettingVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyEvidence_User_AttestedBy",
                        column: x => x.AttestedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyEvidence_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AgencyCheckAttempt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    InputFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RuleVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scenario = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResultCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgencyCheckAttempt", x => x.Id);
                    table.CheckConstraint("CK_AgencyCheckAttempt_CompletedAt_Utc", "DATEPART(TZOFFSET,[CompletedAt]) = 0");
                    table.CheckConstraint("CK_AgencyCheckAttempt_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_AgencyCheckAttempt_Kind", "[Kind] IN ('fca','financial-check','sanctions','ownership')");
                    table.CheckConstraint("CK_AgencyCheckAttempt_Scenario", "[Scenario] IN ('pass','refer','unavailable')");
                    table.CheckConstraint("CK_AgencyCheckAttempt_State", "[State] IN ('passed','refer','unavailable')");
                    table.ForeignKey(
                        name: "FK_AgencyCheckAttempt_AgencyEvidence_EvidenceId_AgencyId",
                        columns: x => new { x.EvidenceId, x.AgencyId },
                        principalTable: "AgencyEvidence",
                        principalColumns: new[] { "Id", "AgencyId" });
                    table.ForeignKey(
                        name: "FK_AgencyCheckAttempt_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyCheckAttempt_SettingVersion_RuleVersionId",
                        column: x => x.RuleVersionId,
                        principalTable: "SettingVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyCheckAttempt_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyCheckAttempt_AgencyId_Ordinal",
                table: "AgencyCheckAttempt",
                columns: new[] { "AgencyId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyCheckAttempt_CreatedBy",
                table: "AgencyCheckAttempt",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyCheckAttempt_EvidenceId_AgencyId",
                table: "AgencyCheckAttempt",
                columns: new[] { "EvidenceId", "AgencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyCheckAttempt_RuleVersionId",
                table: "AgencyCheckAttempt",
                column: "RuleVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyEvidence_AgencyId_Kind_Ordinal",
                table: "AgencyEvidence",
                columns: new[] { "AgencyId", "Kind", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyEvidence_AttestedBy",
                table: "AgencyEvidence",
                column: "AttestedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyEvidence_CreatedBy",
                table: "AgencyEvidence",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyEvidence_FileId_AgencyId",
                table: "AgencyEvidence",
                columns: new[] { "FileId", "AgencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyEvidence_RuleVersionId",
                table: "AgencyEvidence",
                column: "RuleVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyEvidenceFile_AgencyId",
                table: "AgencyEvidenceFile",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyEvidenceFile_CreatedBy",
                table: "AgencyEvidenceFile",
                column: "CreatedBy");
            foreach (var table in new[] { "AgencyEvidenceFile", "AgencyEvidence", "AgencyCheckAttempt" })
                migrationBuilder.Sql($"CREATE TRIGGER [TR_{table}_Immutable] ON [{table}] AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; THROW 51021, 'Agency evidence history is immutable.', 1; END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgencyCheckAttempt");

            migrationBuilder.DropTable(
                name: "AgencyEvidence");

            migrationBuilder.DropTable(
                name: "AgencyEvidenceFile");
        }
    }
}
