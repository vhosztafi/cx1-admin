using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenewalPreparationVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RenewalPreparationVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseTermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    TermMonths = table.Column<int>(type: "int", nullable: false),
                    EndUtcOffsetMinutes = table.Column<int>(type: "int", nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TermIntentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BinderVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyTermsVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleSettingVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FairValueAssessmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RenewalPreparationVersion", x => x.Id);
                    table.UniqueConstraint("AK_RenewalPreparationVersion_Id_DraftId", x => new { x.Id, x.DraftId });
                    table.CheckConstraint("CK_RenewalPreparationVersion_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_RenewalPreparationVersion_EndsAt_Utc", "DATEPART(TZOFFSET,[EndsAt]) = 0");
                    table.CheckConstraint("CK_RenewalPreparationVersion_Intent", "ISJSON([TermIntentJson],OBJECT)=1 AND DATALENGTH([TermIntentJson])<=8192");
                    table.CheckConstraint("CK_RenewalPreparationVersion_Sequence", "[Sequence]>0 AND [CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_RenewalPreparationVersion_StartsAt_Utc", "DATEPART(TZOFFSET,[StartsAt]) = 0");
                    table.CheckConstraint("CK_RenewalPreparationVersion_Term", "[TermMonths] BETWEEN 1 AND 12 AND [StartsAt]<[EndsAt] AND ([EndUtcOffsetMinutes] IS NULL OR [EndUtcOffsetMinutes] IN (0,60))");
                    table.ForeignKey(
                        name: "FK_RenewalPreparationVersion_AgencyTermsVersion_AgencyTermsVersionId",
                        column: x => x.AgencyTermsVersionId,
                        principalTable: "AgencyTermsVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RenewalPreparationVersion_BinderVersion_BinderVersionId_ProductId",
                        columns: x => new { x.BinderVersionId, x.ProductId },
                        principalTable: "BinderVersion",
                        principalColumns: new[] { "Id", "ProductId" });
                    table.ForeignKey(
                        name: "FK_RenewalPreparationVersion_FairValueAssessmentVersion_FairValueAssessmentId_ProductId_ProductVersionId_BinderVersionId",
                        columns: x => new { x.FairValueAssessmentId, x.ProductId, x.ProductVersionId, x.BinderVersionId },
                        principalTable: "FairValueAssessmentVersion",
                        principalColumns: new[] { "Id", "ProductId", "ProductVersionId", "BinderVersionId" });
                    table.ForeignKey(
                        name: "FK_RenewalPreparationVersion_Policy_PolicyId_ProductId",
                        columns: x => new { x.PolicyId, x.ProductId },
                        principalTable: "Policy",
                        principalColumns: new[] { "Id", "ProductId" });
                    table.ForeignKey(
                        name: "FK_RenewalPreparationVersion_ProductVersion_ProductVersionId_ProductId",
                        columns: x => new { x.ProductVersionId, x.ProductId },
                        principalTable: "ProductVersion",
                        principalColumns: new[] { "Id", "ProductId" });
                    table.ForeignKey(
                        name: "FK_RenewalPreparationVersion_ServicingDraft_DraftId_PolicyId_BaseTermId_BaseVersionId",
                        columns: x => new { x.DraftId, x.PolicyId, x.BaseTermId, x.BaseVersionId },
                        principalTable: "ServicingDraft",
                        principalColumns: new[] { "Id", "PolicyId", "BaseTermId", "BaseVersionId" });
                    table.ForeignKey(
                        name: "FK_RenewalPreparationVersion_SettingVersion_RuleSettingVersionId",
                        column: x => x.RuleSettingVersionId,
                        principalTable: "SettingVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RenewalPreparationVersion_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalPreparationVersion_AgencyTermsVersionId",
                table: "RenewalPreparationVersion",
                column: "AgencyTermsVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RenewalPreparationVersion_BinderVersionId_ProductId",
                table: "RenewalPreparationVersion",
                columns: new[] { "BinderVersionId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalPreparationVersion_CreatedBy",
                table: "RenewalPreparationVersion",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_RenewalPreparationVersion_DraftId_PolicyId_BaseTermId_BaseVersionId",
                table: "RenewalPreparationVersion",
                columns: new[] { "DraftId", "PolicyId", "BaseTermId", "BaseVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalPreparationVersion_DraftId_Sequence",
                table: "RenewalPreparationVersion",
                columns: new[] { "DraftId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RenewalPreparationVersion_FairValueAssessmentId_ProductId_ProductVersionId_BinderVersionId",
                table: "RenewalPreparationVersion",
                columns: new[] { "FairValueAssessmentId", "ProductId", "ProductVersionId", "BinderVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalPreparationVersion_PolicyId_ProductId",
                table: "RenewalPreparationVersion",
                columns: new[] { "PolicyId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalPreparationVersion_ProductVersionId_ProductId",
                table: "RenewalPreparationVersion",
                columns: new[] { "ProductVersionId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalPreparationVersion_RuleSettingVersionId",
                table: "RenewalPreparationVersion",
                column: "RuleSettingVersionId");
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_RenewalPreparationVersion_Immutable ON RenewalPreparationVersion AFTER UPDATE,DELETE AS
                BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51720,'Renewal preparation history is immutable.',1; END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_RenewalPreparationVersion_Source ON RenewalPreparationVersion AFTER INSERT AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted i
                    JOIN ServicingDraft d ON d.Id=i.DraftId JOIN Policy p ON p.Id=i.PolicyId JOIN PolicyTerm t ON t.Id=i.BaseTermId
                    JOIN ProductVersion pv ON pv.Id=i.ProductVersionId JOIN BinderVersion b ON b.Id=i.BinderVersionId
                    JOIN AgencyTermsVersion a ON a.Id=i.AgencyTermsVersionId JOIN SettingVersion s ON s.Id=i.RuleSettingVersionId
                    WHERE d.Kind<>'renewal' OR d.State<>'draft' OR i.CreatedAt<d.CreatedAt OR i.StartsAt<>t.EndsAt
                      OR a.AgencyId<>p.AgencyId OR a.EffectiveFrom>CONVERT(date,i.StartsAt AT TIME ZONE 'GMT Standard Time')
                      OR pv.State<>'published' OR b.State<>'published' OR pv.EffectiveFrom>i.StartsAt OR pv.EffectiveTo<i.EndsAt
                      OR b.EffectiveFrom>i.StartsAt OR b.EffectiveTo<i.EndsAt
                      OR s.Scope<>'renewal-preparation' OR s.EffectiveFrom>i.CreatedAt
                      OR EXISTS(SELECT 1 FROM SettingVersion newer WHERE newer.Scope=s.Scope AND newer.EffectiveFrom<=i.CreatedAt AND newer.Version>s.Version)
                      OR NOT EXISTS(SELECT 1 FROM OPENJSON(s.[Values],'$.allowedTermMonths') m WHERE TRY_CONVERT(int,m.[value])=i.TermMonths)
                      OR i.Sequence<>1+(SELECT COUNT(*) FROM RenewalPreparationVersion r WHERE r.DraftId=i.DraftId AND r.Sequence<i.Sequence)
                      OR EXISTS(SELECT 1 FROM RenewalPreparationVersion r WHERE r.DraftId=i.DraftId AND r.Sequence<i.Sequence AND r.CreatedAt>i.CreatedAt)
                      OR DATEADD(month,i.TermMonths,CONVERT(datetime2,i.StartsAt AT TIME ZONE 'GMT Standard Time'))<>CONVERT(datetime2,i.EndsAt AT TIME ZONE 'GMT Standard Time')
                      OR COALESCE(JSON_VALUE(i.TermIntentJson,'$.kind'),'')<>CASE WHEN i.TermMonths=12 THEN 'annual' ELSE 'short-period' END
                      OR COALESCE(JSON_VALUE(i.TermIntentJson,'$.timeZone'),'')<>'Europe/London'
                      OR COALESCE(JSON_VALUE(i.TermIntentJson,'$.localStartDate'),'')<>CONVERT(char(10),i.StartsAt AT TIME ZONE 'GMT Standard Time',23)
                      OR COALESCE(JSON_VALUE(i.TermIntentJson,'$.localStartTime'),'')<>CONVERT(char(5),CONVERT(time,i.StartsAt AT TIME ZONE 'GMT Standard Time'),108)
                      OR COALESCE(TRY_CONVERT(int,JSON_VALUE(i.TermIntentJson,'$.utcOffsetMinutes')),-999)<>DATEPART(tzoffset,i.StartsAt AT TIME ZONE 'GMT Standard Time')
                      OR (i.EndUtcOffsetMinutes IS NOT NULL AND i.EndUtcOffsetMinutes<>DATEPART(tzoffset,i.EndsAt AT TIME ZONE 'GMT Standard Time'))
                      OR (i.TermMonths<>12 AND (COALESCE(JSON_VALUE(i.TermIntentJson,'$.localEndDate'),'')<>CONVERT(char(10),i.EndsAt AT TIME ZONE 'GMT Standard Time',23)
                        OR COALESCE(JSON_VALUE(i.TermIntentJson,'$.localEndTime'),'')<>CONVERT(char(5),CONVERT(time,i.EndsAt AT TIME ZONE 'GMT Standard Time'),108)))
                      OR EXISTS(SELECT 1 FROM PolicyTerm other WHERE other.PolicyId=i.PolicyId AND other.Id<>i.BaseTermId AND other.StartsAt<i.EndsAt AND i.StartsAt<other.EndsAt)
                      OR NOT EXISTS(SELECT 1 FROM PolicyVersion v JOIN PolicyTransaction pt ON pt.Id=v.TransactionId
                        WHERE v.Id=i.BaseVersionId AND pt.Kind IN ('new-business','adjustment','renewal') AND v.EffectiveAt<t.EndsAt AND v.ProcessedAt<=i.CreatedAt AND pt.ProcessedAt<=i.CreatedAt)
                      OR i.BaseVersionId<>(SELECT TOP(1) v.Id FROM PolicyVersion v JOIN PolicyTransaction pt ON pt.Id=v.TransactionId
                        WHERE v.TermId=i.BaseTermId AND v.PolicyId=i.PolicyId AND v.EffectiveAt<t.EndsAt AND v.ProcessedAt<=i.CreatedAt AND pt.ProcessedAt<=i.CreatedAt
                        AND pt.Kind IN ('new-business','adjustment','renewal','cancellation') ORDER BY v.EffectiveAt DESC,pt.Sequence DESC,v.SliceOrdinal DESC,v.Id))
                    THROW 51721,'Renewal preparation requires current owned configuration and the known uncancelled term-end risk.',1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RenewalPreparationVersion");
        }
    }
}
