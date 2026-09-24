using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceBordereauVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinanceBordereauBatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountingPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceBordereauBatch", x => x.Id);
                    table.CheckConstraint("CK_FinanceBordereauBatch_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_FinanceBordereauBatch_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_FinanceBordereauBatch_AccountingPeriod_AccountingPeriodId",
                        column: x => x.AccountingPeriodId,
                        principalTable: "AccountingPeriod",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceBordereauBatch_CapacityProvider_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "CapacityProvider",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceBordereauBatch_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FinanceBordereauVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    ParentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceCutoff = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SourceHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    MembersHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ValidationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    ContentHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceBordereauVersion", x => x.Id);
                    table.CheckConstraint("CK_FinanceBordereauVersion_Content", "([State]='valid' AND [ContentBytes] IS NOT NULL AND [ContentHash]=HASHBYTES('SHA2_256',[ContentBytes])) OR ([State]<>'valid' AND [ContentBytes] IS NULL AND [ContentHash] IS NULL)");
                    table.CheckConstraint("CK_FinanceBordereauVersion_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_FinanceBordereauVersion_SourceCutoff_Utc", "DATEPART(TZOFFSET,[SourceCutoff]) = 0");
                    table.CheckConstraint("CK_FinanceBordereauVersion_State", "[State] IN ('unvalidated','invalid','valid') AND [Number]>0");
                    table.CheckConstraint("CK_FinanceBordereauVersion_ValidationJson_Json", "ISJSON([ValidationJson]) = 1");
                    table.ForeignKey(
                        name: "FK_FinanceBordereauVersion_FinanceBordereauBatch_BatchId",
                        column: x => x.BatchId,
                        principalTable: "FinanceBordereauBatch",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceBordereauVersion_FinanceBordereauVersion_ParentVersionId",
                        column: x => x.ParentVersionId,
                        principalTable: "FinanceBordereauVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceBordereauVersion_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FinanceBordereauMember",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceJournalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyTermsVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Premium = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    Tax = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    Commission = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    NetDue = table.Column<decimal>(type: "decimal(15,2)", precision: 15, scale: 2, nullable: false),
                    PolicyReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProviderProductCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AgencyReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CorrectionActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrectionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CorrectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExclusionActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExclusionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ExcludedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceBordereauMember", x => x.Id);
                    table.CheckConstraint("CK_FinanceBordereauMember_CorrectedAt_Utc", "DATEPART(TZOFFSET,[CorrectedAt]) = 0");
                    table.CheckConstraint("CK_FinanceBordereauMember_Correction", "([CorrectionActorId] IS NULL AND [CorrectionReason] IS NULL AND [CorrectedAt] IS NULL) OR ([CorrectionActorId] IS NOT NULL AND LEN(TRIM([CorrectionReason])) BETWEEN 10 AND 1000 AND [CorrectedAt] IS NOT NULL)");
                    table.CheckConstraint("CK_FinanceBordereauMember_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_FinanceBordereauMember_Currency", "[Currency]='GBP'");
                    table.CheckConstraint("CK_FinanceBordereauMember_ExcludedAt_Utc", "DATEPART(TZOFFSET,[ExcludedAt]) = 0");
                    table.CheckConstraint("CK_FinanceBordereauMember_Exclusion", "([ExclusionActorId] IS NULL AND [ExclusionReason] IS NULL AND [ExcludedAt] IS NULL) OR ([ExclusionActorId] IS NOT NULL AND LEN(TRIM([ExclusionReason])) BETWEEN 10 AND 1000 AND [ExcludedAt] IS NOT NULL)");
                    table.CheckConstraint("CK_FinanceBordereauMember_PostedAt_Utc", "DATEPART(TZOFFSET,[PostedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_FinanceBordereauMember_AgencyTermsVersion_AgencyTermsVersionId",
                        column: x => x.AgencyTermsVersionId,
                        principalTable: "AgencyTermsVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceBordereauMember_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceBordereauMember_FinanceBordereauVersion_VersionId",
                        column: x => x.VersionId,
                        principalTable: "FinanceBordereauVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceBordereauMember_Journal_SourceJournalId",
                        column: x => x.SourceJournalId,
                        principalTable: "Journal",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceBordereauMember_PolicyTransaction_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "PolicyTransaction",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceBordereauMember_Policy_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policy",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceBordereauMember_ProductVersion_ProductVersionId",
                        column: x => x.ProductVersionId,
                        principalTable: "ProductVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceBordereauMember_User_CorrectionActorId",
                        column: x => x.CorrectionActorId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceBordereauMember_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinanceBordereauMember_User_ExclusionActorId",
                        column: x => x.ExclusionActorId,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauBatch_AccountingPeriodId",
                table: "FinanceBordereauBatch",
                column: "AccountingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauBatch_CreatedBy",
                table: "FinanceBordereauBatch",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauBatch_ProviderId_AccountingPeriodId_CreatedAt",
                table: "FinanceBordereauBatch",
                columns: new[] { "ProviderId", "AccountingPeriodId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauMember_AgencyId",
                table: "FinanceBordereauMember",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauMember_AgencyTermsVersionId",
                table: "FinanceBordereauMember",
                column: "AgencyTermsVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauMember_CorrectionActorId",
                table: "FinanceBordereauMember",
                column: "CorrectionActorId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauMember_CreatedBy",
                table: "FinanceBordereauMember",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauMember_ExclusionActorId",
                table: "FinanceBordereauMember",
                column: "ExclusionActorId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauMember_PolicyId",
                table: "FinanceBordereauMember",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauMember_ProductVersionId",
                table: "FinanceBordereauMember",
                column: "ProductVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauMember_SourceJournalId",
                table: "FinanceBordereauMember",
                column: "SourceJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauMember_TransactionId",
                table: "FinanceBordereauMember",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauMember_VersionId_SourceJournalId",
                table: "FinanceBordereauMember",
                columns: new[] { "VersionId", "SourceJournalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauVersion_BatchId_Number",
                table: "FinanceBordereauVersion",
                columns: new[] { "BatchId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauVersion_CreatedBy",
                table: "FinanceBordereauVersion",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceBordereauVersion_ParentVersionId",
                table: "FinanceBordereauVersion",
                column: "ParentVersionId");

            migrationBuilder.Sql("""
                CREATE TRIGGER TR_FinanceBordereauBatch_Insert ON FinanceBordereauBatch AFTER INSERT AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM inserted WHERE CurrentVersionId<>'00000000-0000-0000-0000-000000000000')
                        THROW 52090, 'Bordereau head must start empty in its transaction.', 1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_FinanceBordereauBatch_Scope ON FinanceBordereauBatch AFTER UPDATE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON i.Id=d.Id
                        WHERE i.ProviderId<>d.ProviderId OR i.AccountingPeriodId<>d.AccountingPeriodId
                           OR i.CreatedAt<>d.CreatedAt OR ISNULL(i.CreatedBy,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.CreatedBy,'00000000-0000-0000-0000-000000000000'))
                        THROW 52091, 'Bordereau scope is immutable.', 1;
                    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON i.Id=d.Id
                        LEFT JOIN FinanceBordereauVersion v ON v.Id=i.CurrentVersionId
                        LEFT JOIN FinanceBordereauVersion p ON p.Id=d.CurrentVersionId
                        WHERE i.CurrentVersionId<>d.CurrentVersionId AND
                          (v.Id IS NULL OR v.BatchId<>i.Id OR
                           (d.CurrentVersionId='00000000-0000-0000-0000-000000000000' AND (v.Number<>1 OR v.ParentVersionId IS NOT NULL)) OR
                           (d.CurrentVersionId<>'00000000-0000-0000-0000-000000000000' AND
                            (p.Id IS NULL OR v.ParentVersionId<>p.Id OR v.Number<>p.Number+1 OR
                             EXISTS (SELECT SourceJournalId FROM FinanceBordereauMember WHERE VersionId=p.Id
                                     EXCEPT SELECT SourceJournalId FROM FinanceBordereauMember WHERE VersionId=v.Id) OR
                             EXISTS (SELECT SourceJournalId FROM FinanceBordereauMember WHERE VersionId=v.Id
                                     EXCEPT SELECT SourceJournalId FROM FinanceBordereauMember WHERE VersionId=p.Id)))))
                        THROW 52096, 'Bordereau head must advance to a complete child version.', 1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_FinanceBordereauVersion_Immutable ON FinanceBordereauVersion AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 52092, 'Bordereau version is immutable.', 1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_FinanceBordereauVersion_Insert ON FinanceBordereauVersion AFTER INSERT AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM inserted i LEFT JOIN FinanceBordereauVersion p ON p.Id=i.ParentVersionId
                        WHERE (i.Number=1 AND i.ParentVersionId IS NOT NULL) OR
                              (i.Number>1 AND (p.Id IS NULL OR p.BatchId<>i.BatchId OR p.Number+1<>i.Number OR
                                 p.SourceCutoff<>i.SourceCutoff OR p.SourceHash<>i.SourceHash OR p.SchemaVersion<>i.SchemaVersion)) OR
                              (i.State='valid' AND i.ValidationJson<>'[]'))
                        THROW 52093, 'Bordereau version ancestry or validation is invalid.', 1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_FinanceBordereauMember_Immutable ON FinanceBordereauMember AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 52094, 'Bordereau member is immutable.', 1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_FinanceBordereauMember_Source ON FinanceBordereauMember AFTER INSERT AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM inserted m
                        JOIN FinanceBordereauVersion v ON v.Id=m.VersionId
                        JOIN FinanceBordereauBatch b ON b.Id=v.BatchId
                        JOIN AccountingPeriod ap ON ap.Id=b.AccountingPeriodId
                        JOIN Journal j ON j.Id=m.SourceJournalId
                        JOIN IssueFinancialObligation o ON o.Id=j.ObligationId
                        JOIN PolicyTerm t ON t.Id=o.TermId
                        JOIN ProductVersion pv ON pv.Id=m.ProductVersionId
                        LEFT JOIN FinanceBordereauMember pm ON pm.VersionId=v.ParentVersionId AND pm.SourceJournalId=m.SourceJournalId
                        WHERE (v.Number=1 AND (j.PostedAt IS NULL OR j.PostedAt>v.SourceCutoff OR o.ProviderId<>b.ProviderId OR pv.ProviderId<>b.ProviderId
                           OR m.PolicyId<>o.PolicyId OR m.TransactionId<>o.TransactionId OR m.AgencyId<>o.AgencyId
                           OR m.AgencyTermsVersionId<>o.AgencyTermsVersionId OR m.ProductVersionId<>t.ProductVersionId
                           OR m.PostedAt<>j.PostedAt OR m.PostingDate<>ISNULL(j.PostingDate,CONVERT(date,j.PostedAt AT TIME ZONE 'GMT Standard Time'))
                           OR m.PostingDate<ap.StartsOn OR m.PostingDate>=ap.EndsOn OR m.Currency<>o.Currency
                           OR m.Premium<>o.Premium OR m.Tax<>o.Tax OR m.Fee<>o.Fee OR m.Commission<>o.Commission OR m.NetDue<>o.NetDue))
                           OR (v.Number>1 AND (pm.Id IS NULL OR m.PolicyId<>pm.PolicyId OR m.TransactionId<>pm.TransactionId
                           OR m.AgencyId<>pm.AgencyId OR m.ProductVersionId<>pm.ProductVersionId OR m.AgencyTermsVersionId<>pm.AgencyTermsVersionId
                           OR m.PostedAt<>pm.PostedAt OR m.PostingDate<>pm.PostingDate OR m.Currency<>pm.Currency
                           OR m.Premium<>pm.Premium OR m.Tax<>pm.Tax OR m.Fee<>pm.Fee OR m.Commission<>pm.Commission OR m.NetDue<>pm.NetDue)))
                        THROW 52095, 'Bordereau member differs from posted source or pinned period.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_FinanceBordereauMember_Source");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_FinanceBordereauMember_Immutable");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_FinanceBordereauVersion_Insert");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_FinanceBordereauVersion_Immutable");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_FinanceBordereauBatch_Scope");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_FinanceBordereauBatch_Insert");
            migrationBuilder.DropTable(
                name: "FinanceBordereauMember");

            migrationBuilder.DropTable(
                name: "FinanceBordereauVersion");

            migrationBuilder.DropTable(
                name: "FinanceBordereauBatch");
        }
    }
}
