using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceBordereauSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinanceBordereauSubmission",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    RequestHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    State = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProviderState = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ProviderOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProviderEventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceBordereauSubmission", x => x.Id);
                    table.CheckConstraint("CK_FinanceBordereauSubmission_Applied", "([State] IN ('submitted','rejected') AND [AppliedAt] IS NOT NULL AND [ProviderState] IS NOT NULL) OR ([State] NOT IN ('submitted','rejected') AND [AppliedAt] IS NULL)");
                    table.CheckConstraint("CK_FinanceBordereauSubmission_AppliedAt_Utc", "DATEPART(TZOFFSET,[AppliedAt]) = 0");
                    table.CheckConstraint("CK_FinanceBordereauSubmission_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_FinanceBordereauSubmission_Provider", "([ProviderState] IS NULL AND [ProviderOperationId] IS NULL AND [ProviderEventId] IS NULL) OR ([ProviderState] IN ('accepted','rejected') AND [ProviderOperationId] IS NOT NULL AND [ProviderEventId] IS NOT NULL)");
                    table.CheckConstraint("CK_FinanceBordereauSubmission_State", "[State] IN ('queued','uncertain','acknowledged','submitted','rejected','failed')");
                    table.CheckConstraint("CK_FinanceBordereauSubmission_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_FinanceBordereauSubmission_DemoProviderOperation_ProviderOperationId",
                        column: x => x.ProviderOperationId,
                        principalTable: "DemoProviderOperation",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceBordereauSubmission_FinanceBordereauBatch_BatchId",
                        column: x => x.BatchId,
                        principalTable: "FinanceBordereauBatch",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceBordereauSubmission_FinanceBordereauVersion_VersionId",
                        column: x => x.VersionId,
                        principalTable: "FinanceBordereauVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceBordereauSubmission_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceBordereauSubmission_SettingVersion_ScenarioVersionId",
                        column: x => x.ScenarioVersionId,
                        principalTable: "SettingVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceBordereauSubmission_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauSubmission_BatchId",
                table: "FinanceBordereauSubmission",
                column: "BatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauSubmission_CreatedBy",
                table: "FinanceBordereauSubmission",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauSubmission_OperationKey",
                table: "FinanceBordereauSubmission",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauSubmission_ProviderOperationId",
                table: "FinanceBordereauSubmission",
                column: "ProviderOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauSubmission_ScenarioVersionId",
                table: "FinanceBordereauSubmission",
                column: "ScenarioVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauSubmission_VersionId",
                table: "FinanceBordereauSubmission",
                column: "VersionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauSubmission_WorkId",
                table: "FinanceBordereauSubmission",
                column: "WorkId",
                unique: true);
            migrationBuilder.Sql("""
                IF NOT EXISTS(SELECT 1 FROM SettingVersion WHERE Scope=N'finance-bordereau-submission-demo' AND Version=1)
                  INSERT SettingVersion(Id,Scope,Version,EffectiveFrom,[Values],CreatedAt,CreatedBy)
                  VALUES('907b4b61-c8c7-436f-bbf0-e39ba15d2010',N'finance-bordereau-submission-demo',1,
                    '2026-09-01T00:00:00+00:00',N'{"scenario":"success"}',SYSUTCDATETIME(),NULL);
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_FinanceBordereauSubmission_Guard ON FinanceBordereauSubmission
                AFTER INSERT,UPDATE,DELETE AS BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL)
                    THROW 52510,'Bordereau submission is retained.',1;
                  IF EXISTS(SELECT 1 FROM inserted s
                    LEFT JOIN FinanceBordereauBatch b ON b.Id=s.BatchId
                    LEFT JOIN FinanceBordereauVersion v ON v.Id=s.VersionId AND v.BatchId=s.BatchId
                    LEFT JOIN OutboxWork w ON w.Id=s.WorkId
                    LEFT JOIN SettingVersion scenario ON scenario.Id=s.ScenarioVersionId
                    WHERE b.Id IS NULL OR v.Id IS NULL OR v.State<>'valid' OR v.ContentHash IS NULL OR
                      s.ContentHash<>v.ContentHash OR v.ValidationJson<>N'[]' OR
                      w.Id IS NULL OR w.Kind<>N'finance-bordereau-submit' OR
                      w.SubjectRecordId<>s.Id OR w.OperationKey COLLATE Latin1_General_100_BIN2<>s.OperationKey OR
                      w.ScenarioVersionId<>s.ScenarioVersionId OR
                      scenario.Scope<>N'finance-bordereau-submission-demo' OR
                      (s.State IN (N'queued',N'uncertain',N'acknowledged') AND b.CurrentVersionId<>v.Id))
                    THROW 52511,'Submission must pin one exact validated current version and durable work.',1;
                  IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id
                    WHERE i.BatchId<>d.BatchId OR i.VersionId<>d.VersionId OR
                      i.ContentHash<>d.ContentHash OR i.RequestHash<>d.RequestHash OR
                      i.WorkId<>d.WorkId OR i.OperationKey<>d.OperationKey OR
                      i.ScenarioVersionId<>d.ScenarioVersionId OR i.CreatedBy<>d.CreatedBy OR
                      i.CreatedAt<>d.CreatedAt OR
                      d.State IN (N'submitted',N'rejected') OR
                      (d.State=N'queued' AND i.State NOT IN (N'queued',N'uncertain',N'submitted',N'rejected',N'failed')) OR
                      (d.State=N'uncertain' AND i.State NOT IN (N'uncertain',N'submitted',N'rejected',N'failed')))
                    THROW 52512,'Submitted identity and terminal outcome are immutable.',1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_FinanceBordereauBatch_SubmissionHold ON FinanceBordereauBatch AFTER UPDATE AS BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id
                    JOIN FinanceBordereauSubmission s ON s.BatchId=i.Id
                    WHERE i.CurrentVersionId<>d.CurrentVersionId AND s.State NOT IN (N'submitted',N'rejected')
                      AND NOT EXISTS(SELECT 1 FROM DemoProviderOperation op WHERE op.Kind=N'finance-bordereau-submit'
                        AND op.OperationKey COLLATE Latin1_General_100_BIN2=s.OperationKey AND op.ScenarioVersionId=s.ScenarioVersionId
                        AND op.RequestHash=s.RequestHash AND op.Result IS NOT NULL))
                    THROW 52513,'Pending insurer submission pins the current version.',1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_FinanceBordereauBatch_SubmissionHold");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_FinanceBordereauSubmission_Guard");
            migrationBuilder.DropTable(
                name: "FinanceBordereauSubmission");
        }
    }
}
