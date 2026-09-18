using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenewalPreparationEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductEvidenceFileVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BinderVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_ProductEvidenceFileVersion", x => x.Id);
                    table.UniqueConstraint("AK_ProductEvidenceFileVersion_Id_ProductId_ProductVersionId_BinderVersionId", x => new { x.Id, x.ProductId, x.ProductVersionId, x.BinderVersionId });
                    table.CheckConstraint("CK_ProductEvidenceFileVersion_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ProductEvidenceFileVersion_Hash", "LEN([Sha256])=64 AND [Sha256]=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',[Content]),2))");
                    table.CheckConstraint("CK_ProductEvidenceFileVersion_Media", "[ContentType] IN ('application/pdf','image/png','image/jpeg','text/plain')");
                    table.CheckConstraint("CK_ProductEvidenceFileVersion_Name", "LEN(TRIM([FileName]))>0 AND [FileName] NOT LIKE '%/%' AND [FileName] NOT LIKE '%\\%' AND [FileName] NOT LIKE '%:%'");
                    table.CheckConstraint("CK_ProductEvidenceFileVersion_Screening", "[ScreeningState]='accepted' AND [ScreeningMethod]='demo-signature-v1' AND [CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_ProductEvidenceFileVersion_Size", "[ByteLength] BETWEEN 1 AND 10485760 AND DATALENGTH([Content])=[ByteLength]");
                    table.ForeignKey(
                        name: "FK_ProductEvidenceFileVersion_BinderVersion_BinderVersionId_ProductId",
                        columns: x => new { x.BinderVersionId, x.ProductId },
                        principalTable: "BinderVersion",
                        principalColumns: new[] { "Id", "ProductId" });
                    table.ForeignKey(
                        name: "FK_ProductEvidenceFileVersion_ProductVersion_ProductVersionId_ProductId",
                        columns: x => new { x.ProductVersionId, x.ProductId },
                        principalTable: "ProductVersion",
                        principalColumns: new[] { "Id", "ProductId" });
                    table.ForeignKey(
                        name: "FK_ProductEvidenceFileVersion_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RenewalExperienceEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RenewalExperienceEvidence", x => x.Id);
                    table.UniqueConstraint("AK_RenewalExperienceEvidence_Id_DraftId", x => new { x.Id, x.DraftId });
                    table.CheckConstraint("CK_RenewalExperienceEvidence_Actor", "[CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_RenewalExperienceEvidence_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_RenewalExperienceEvidence_ServicingEvidenceFile_FileId_DraftId",
                        columns: x => new { x.FileId, x.DraftId },
                        principalTable: "ServicingEvidenceFile",
                        principalColumns: new[] { "Id", "DraftId" });
                    table.ForeignKey(
                        name: "FK_RenewalExperienceEvidence_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FairValueAssessmentVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BinderVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvidenceFileVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ValidTo = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FairValueAssessmentVersion", x => x.Id);
                    table.UniqueConstraint("AK_FairValueAssessmentVersion_Id_ProductId_ProductVersionId_BinderVersionId", x => new { x.Id, x.ProductId, x.ProductVersionId, x.BinderVersionId });
                    table.CheckConstraint("CK_FairValueAssessmentVersion_Approval", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[ApprovedBy] AND [ApprovedAt]=[CreatedAt] AND LEN(TRIM([Reason])) BETWEEN 10 AND 2000");
                    table.CheckConstraint("CK_FairValueAssessmentVersion_ApprovedAt_Utc", "DATEPART(TZOFFSET,[ApprovedAt]) = 0");
                    table.CheckConstraint("CK_FairValueAssessmentVersion_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_FairValueAssessmentVersion_Outcome", "[Outcome] IN ('pass','refer','fail')");
                    table.CheckConstraint("CK_FairValueAssessmentVersion_ValidFrom_Utc", "DATEPART(TZOFFSET,[ValidFrom]) = 0");
                    table.CheckConstraint("CK_FairValueAssessmentVersion_Validity", "[ValidFrom]<[ValidTo]");
                    table.CheckConstraint("CK_FairValueAssessmentVersion_ValidTo_Utc", "DATEPART(TZOFFSET,[ValidTo]) = 0");
                    table.ForeignKey(
                        name: "FK_FairValueAssessmentVersion_ProductEvidenceFileVersion_EvidenceFileVersionId_ProductId_ProductVersionId_BinderVersionId",
                        columns: x => new { x.EvidenceFileVersionId, x.ProductId, x.ProductVersionId, x.BinderVersionId },
                        principalTable: "ProductEvidenceFileVersion",
                        principalColumns: new[] { "Id", "ProductId", "ProductVersionId", "BinderVersionId" });
                    table.ForeignKey(
                        name: "FK_FairValueAssessmentVersion_User_ApprovedBy",
                        column: x => x.ApprovedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FairValueAssessmentVersion_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RenewalExperienceVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    ObservationStartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ObservationEndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ClaimCount = table.Column<int>(type: "int", nullable: false),
                    Paid = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Outstanding = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    EarnedPremium = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    SourceCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SourceReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EvidenceAssociationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RenewalExperienceVersion", x => x.Id);
                    table.UniqueConstraint("AK_RenewalExperienceVersion_Id_DraftId", x => new { x.Id, x.DraftId });
                    table.CheckConstraint("CK_RenewalExperienceVersion_Claims", "[ClaimCount] BETWEEN 0 AND 100000");
                    table.CheckConstraint("CK_RenewalExperienceVersion_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_RenewalExperienceVersion_EarnedPremium", "[EarnedPremium] BETWEEN 0 AND 9999999999999.99");
                    table.CheckConstraint("CK_RenewalExperienceVersion_Outstanding", "[Outstanding] BETWEEN 0 AND 9999999999999.99");
                    table.CheckConstraint("CK_RenewalExperienceVersion_Paid", "[Paid] BETWEEN 0 AND 9999999999999.99");
                    table.CheckConstraint("CK_RenewalExperienceVersion_Period", "[ObservationStartsOn]<[ObservationEndsOn]");
                    table.CheckConstraint("CK_RenewalExperienceVersion_Sequence", "[Sequence]>0");
                    table.CheckConstraint("CK_RenewalExperienceVersion_Source", "[SourceCode] IN ('insured','agency','administrator') AND LEN(TRIM([SourceReference])) BETWEEN 1 AND 200 AND [CreatedBy] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_RenewalExperienceVersion_RenewalExperienceEvidence_EvidenceAssociationId_DraftId",
                        columns: x => new { x.EvidenceAssociationId, x.DraftId },
                        principalTable: "RenewalExperienceEvidence",
                        principalColumns: new[] { "Id", "DraftId" });
                    table.ForeignKey(
                        name: "FK_RenewalExperienceVersion_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RenewalExperienceReview",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExperienceVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AuthorityVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorityGrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RenewalExperienceReview", x => x.Id);
                    table.CheckConstraint("CK_RenewalExperienceReview_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_RenewalExperienceReview_Outcome", "[Outcome] IN ('accepted','rejected')");
                    table.CheckConstraint("CK_RenewalExperienceReview_Provenance", "[Sequence]>0 AND [CreatedBy] IS NOT NULL AND LEN(TRIM([Reason])) BETWEEN 10 AND 2000");
                    table.ForeignKey(
                        name: "FK_RenewalExperienceReview_RenewalExperienceVersion_ExperienceVersionId_DraftId",
                        columns: x => new { x.ExperienceVersionId, x.DraftId },
                        principalTable: "RenewalExperienceVersion",
                        principalColumns: new[] { "Id", "DraftId" });
                    table.ForeignKey(
                        name: "FK_RenewalExperienceReview_UserAuthorityGrant_AuthorityGrantId_CreatedBy_AuthorityVersionId",
                        columns: x => new { x.AuthorityGrantId, x.CreatedBy, x.AuthorityVersionId },
                        principalTable: "UserAuthorityGrant",
                        principalColumns: new[] { "Id", "UserId", "AuthorityVersionId" });
                    table.ForeignKey(
                        name: "FK_RenewalExperienceReview_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FairValueAssessmentVersion_ApprovedBy",
                table: "FairValueAssessmentVersion",
                column: "ApprovedBy");

            migrationBuilder.CreateIndex(
                name: "IX_FairValueAssessmentVersion_CreatedBy",
                table: "FairValueAssessmentVersion",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_FairValueAssessmentVersion_EvidenceFileVersionId_ProductId_ProductVersionId_BinderVersionId",
                table: "FairValueAssessmentVersion",
                columns: new[] { "EvidenceFileVersionId", "ProductId", "ProductVersionId", "BinderVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_FairValueAssessmentVersion_ProductVersionId_BinderVersionId_ValidFrom",
                table: "FairValueAssessmentVersion",
                columns: new[] { "ProductVersionId", "BinderVersionId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductEvidenceFileVersion_BinderVersionId_ProductId",
                table: "ProductEvidenceFileVersion",
                columns: new[] { "BinderVersionId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductEvidenceFileVersion_CreatedBy",
                table: "ProductEvidenceFileVersion",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ProductEvidenceFileVersion_ProductVersionId_ProductId",
                table: "ProductEvidenceFileVersion",
                columns: new[] { "ProductVersionId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalExperienceEvidence_CreatedBy",
                table: "RenewalExperienceEvidence",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_RenewalExperienceEvidence_FileId_DraftId",
                table: "RenewalExperienceEvidence",
                columns: new[] { "FileId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalExperienceReview_AuthorityGrantId_CreatedBy_AuthorityVersionId",
                table: "RenewalExperienceReview",
                columns: new[] { "AuthorityGrantId", "CreatedBy", "AuthorityVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalExperienceReview_CreatedBy",
                table: "RenewalExperienceReview",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_RenewalExperienceReview_ExperienceVersionId_DraftId",
                table: "RenewalExperienceReview",
                columns: new[] { "ExperienceVersionId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalExperienceReview_ExperienceVersionId_Sequence",
                table: "RenewalExperienceReview",
                columns: new[] { "ExperienceVersionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RenewalExperienceVersion_CreatedBy",
                table: "RenewalExperienceVersion",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_RenewalExperienceVersion_DraftId_Sequence",
                table: "RenewalExperienceVersion",
                columns: new[] { "DraftId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RenewalExperienceVersion_EvidenceAssociationId_DraftId",
                table: "RenewalExperienceVersion",
                columns: new[] { "EvidenceAssociationId", "DraftId" });

            foreach (var table in new[] { "RenewalExperienceEvidence", "RenewalExperienceVersion", "RenewalExperienceReview", "ProductEvidenceFileVersion", "FairValueAssessmentVersion" })
                migrationBuilder.Sql($"CREATE TRIGGER [TR_{table}_Immutable] ON [{table}] AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51710, 'Renewal evidence and assessment history is immutable.', 1; END;");
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_RenewalExperienceEvidence_Owner ON RenewalExperienceEvidence AFTER INSERT AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingDraft d ON d.Id=i.DraftId
                        JOIN ServicingEvidenceFile f ON f.Id=i.FileId AND f.DraftId=i.DraftId
                        WHERE d.Kind<>'renewal' OR d.State<>'draft' OR i.CreatedAt<d.CreatedAt OR i.CreatedAt<f.CreatedAt)
                        THROW 51711, 'Experience evidence requires an active owned renewal draft and existing file.', 1;
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_RenewalExperienceVersion_Owner ON RenewalExperienceVersion AFTER INSERT AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingDraft d ON d.Id=i.DraftId
                        JOIN RenewalExperienceEvidence e ON e.Id=i.EvidenceAssociationId AND e.DraftId=i.DraftId
                        WHERE d.Kind<>'renewal' OR d.State<>'draft' OR i.CreatedAt<e.CreatedAt
                        OR i.ObservationEndsOn>CONVERT(date,i.CreatedAt AT TIME ZONE 'GMT Standard Time')
                        OR i.Sequence<>1+(SELECT COUNT(*) FROM RenewalExperienceVersion v WHERE v.DraftId=i.DraftId AND v.Sequence<i.Sequence)
                        OR EXISTS(SELECT 1 FROM RenewalExperienceVersion v WHERE v.DraftId=i.DraftId AND v.Sequence<i.Sequence AND v.CreatedAt>i.CreatedAt))
                        THROW 51712, 'Experience requires ordered observed facts on an active renewal draft.', 1;
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_RenewalExperienceReview_Authority ON RenewalExperienceReview AFTER INSERT AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS(SELECT 1 FROM inserted i JOIN RenewalExperienceVersion e ON e.Id=i.ExperienceVersionId AND e.DraftId=i.DraftId
                        JOIN ServicingDraft d ON d.Id=i.DraftId JOIN Policy p ON p.Id=d.PolicyId
                        JOIN AuthorityVersion a ON a.Id=i.AuthorityVersionId
                        JOIN UserAuthorityGrant g ON g.Id=i.AuthorityGrantId
                        JOIN [User] u ON u.Id=i.CreatedBy
                        WHERE d.Kind<>'renewal' OR d.State<>'draft' OR i.CreatedAt<e.CreatedAt
                        OR a.ProductId<>p.ProductId OR a.State<>'published' OR u.State<>'active'
                        OR g.RevokedAt IS NOT NULL OR g.EffectiveFrom>i.CreatedAt OR g.EffectiveTo<=i.CreatedAt
                        OR a.EffectiveFrom>i.CreatedAt OR a.EffectiveTo<=i.CreatedAt
                        OR EXISTS(SELECT 1 FROM RenewalExperienceVersion newer WHERE newer.DraftId=i.DraftId AND newer.Sequence>e.Sequence)
                        OR i.Sequence<>1+(SELECT COUNT(*) FROM RenewalExperienceReview r WHERE r.ExperienceVersionId=i.ExperienceVersionId AND r.Sequence<i.Sequence)
                        OR EXISTS(SELECT 1 FROM RenewalExperienceReview r WHERE r.ExperienceVersionId=i.ExperienceVersionId AND r.Sequence<i.Sequence AND r.CreatedAt>i.CreatedAt))
                        THROW 51713, 'Experience review requires the latest exact facts and current owned authority.', 1;
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_FairValueAssessmentVersion_Evidence ON FairValueAssessmentVersion AFTER INSERT AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS(SELECT 1 FROM inserted i JOIN ProductEvidenceFileVersion f ON f.Id=i.EvidenceFileVersionId
                        JOIN ProductVersion p ON p.Id=i.ProductVersionId JOIN BinderVersion b ON b.Id=i.BinderVersionId
                        JOIN [User] u ON u.Id=i.ApprovedBy
                        WHERE i.ApprovedAt<f.CreatedAt OR p.State<>'published' OR b.State<>'published' OR u.State<>'active'
                        OR p.EffectiveFrom>i.ValidFrom OR (p.EffectiveTo IS NOT NULL AND p.EffectiveTo<i.ValidTo)
                        OR b.EffectiveFrom>i.ValidFrom OR b.EffectiveTo<i.ValidTo)
                        THROW 51714, 'Fair value approval requires owned evidence and published coverage of its validity period.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FairValueAssessmentVersion");

            migrationBuilder.DropTable(
                name: "RenewalExperienceReview");

            migrationBuilder.DropTable(
                name: "ProductEvidenceFileVersion");

            migrationBuilder.DropTable(
                name: "RenewalExperienceVersion");

            migrationBuilder.DropTable(
                name: "RenewalExperienceEvidence");
        }
    }
}
