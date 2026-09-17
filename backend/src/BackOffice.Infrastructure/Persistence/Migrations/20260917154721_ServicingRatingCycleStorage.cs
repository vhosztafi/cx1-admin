using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingRatingCycleStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrentCycleId",
                table: "ServicingDraft",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServicingCycle",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseTermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyTermsVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingRuleVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BinderVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorityVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuntimeVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InputHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    InputJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SupersededAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SupersededReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicingCycle", x => x.Id);
                    table.UniqueConstraint("AK_ServicingCycle_Id_DraftId_PolicyId", x => new { x.Id, x.DraftId, x.PolicyId });
                    table.UniqueConstraint("AK_ServicingCycle_Id_DraftId_RevisionId", x => new { x.Id, x.DraftId, x.RevisionId });
                    table.UniqueConstraint("AK_ServicingCycle_Id_DraftId_RevisionId_WorkId_RatingRuleVersionId_InputHash", x => new { x.Id, x.DraftId, x.RevisionId, x.WorkId, x.RatingRuleVersionId, x.InputHash });
                    table.CheckConstraint("CK_ServicingCycle_Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[RequestedBy]");
                    table.CheckConstraint("CK_ServicingCycle_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ServicingCycle_InputJson", "ISJSON([InputJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[InputJson] COLLATE Latin1_General_100_BIN2_UTF8))<=8388608");
                    table.CheckConstraint("CK_ServicingCycle_Sequence", "[Sequence]>0");
                    table.CheckConstraint("CK_ServicingCycle_State", "[State] IN ('rating-pending','rated','failed','superseded')");
                    table.CheckConstraint("CK_ServicingCycle_Superseded", "([State]='superseded' AND [SupersededAt] IS NOT NULL AND [SupersededAt]>=[CreatedAt] AND [SupersededReason] IS NOT NULL AND LEN(TRIM([SupersededReason]))>0) OR ([State]<>'superseded' AND [SupersededAt] IS NULL AND [SupersededReason] IS NULL)");
                    table.CheckConstraint("CK_ServicingCycle_SupersededAt_Utc", "DATEPART(TZOFFSET,[SupersededAt]) = 0");
                    table.CheckConstraint("CK_ServicingCycle_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_ServicingCycle_AgencyTermsVersion_AgencyTermsVersionId",
                        column: x => x.AgencyTermsVersionId,
                        principalTable: "AgencyTermsVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingCycle_AuthorityVersion_AuthorityVersionId_ProductVersionId_BinderVersionId",
                        columns: x => new { x.AuthorityVersionId, x.ProductVersionId, x.BinderVersionId },
                        principalTable: "AuthorityVersion",
                        principalColumns: new[] { "Id", "ProductVersionId", "BinderVersionId" });
                    table.ForeignKey(
                        name: "FK_ServicingCycle_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingCycle_Policy_PolicyId_ProductId",
                        columns: x => new { x.PolicyId, x.ProductId },
                        principalTable: "Policy",
                        principalColumns: new[] { "Id", "ProductId" });
                    table.ForeignKey(
                        name: "FK_ServicingCycle_ProductVersion_ProductVersionId_ProductId",
                        columns: x => new { x.ProductVersionId, x.ProductId },
                        principalTable: "ProductVersion",
                        principalColumns: new[] { "Id", "ProductId" });
                    table.ForeignKey(
                        name: "FK_ServicingCycle_RatingRuleVersion_RatingRuleVersionId_ProductId",
                        columns: x => new { x.RatingRuleVersionId, x.ProductId },
                        principalTable: "RatingRuleVersion",
                        principalColumns: new[] { "Id", "ProductId" });
                    table.ForeignKey(
                        name: "FK_ServicingCycle_ServicingDraft_DraftId_PolicyId_BaseTermId_BaseVersionId",
                        columns: x => new { x.DraftId, x.PolicyId, x.BaseTermId, x.BaseVersionId },
                        principalTable: "ServicingDraft",
                        principalColumns: new[] { "Id", "PolicyId", "BaseTermId", "BaseVersionId" });
                    table.ForeignKey(
                        name: "FK_ServicingCycle_ServicingRevision_RevisionId_DraftId",
                        columns: x => new { x.RevisionId, x.DraftId },
                        principalTable: "ServicingRevision",
                        principalColumns: new[] { "Id", "DraftId" });
                    table.ForeignKey(
                        name: "FK_ServicingCycle_SettingVersion_RuntimeVersionId",
                        column: x => x.RuntimeVersionId,
                        principalTable: "SettingVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingCycle_SettingVersion_ScenarioVersionId",
                        column: x => x.ScenarioVersionId,
                        principalTable: "SettingVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingCycle_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingCycle_User_RequestedBy",
                        column: x => x.RequestedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingDraft_CurrentCycleId_Id_PolicyId",
                table: "ServicingDraft",
                columns: new[] { "CurrentCycleId", "Id", "PolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCycle_AgencyTermsVersionId",
                table: "ServicingCycle",
                column: "AgencyTermsVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCycle_AuthorityVersionId_ProductVersionId_BinderVersionId",
                table: "ServicingCycle",
                columns: new[] { "AuthorityVersionId", "ProductVersionId", "BinderVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCycle_CreatedBy",
                table: "ServicingCycle",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCycle_DraftId_PolicyId_BaseTermId_BaseVersionId",
                table: "ServicingCycle",
                columns: new[] { "DraftId", "PolicyId", "BaseTermId", "BaseVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCycle_DraftId_Sequence",
                table: "ServicingCycle",
                columns: new[] { "DraftId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCycle_PolicyId_ProductId",
                table: "ServicingCycle",
                columns: new[] { "PolicyId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCycle_ProductVersionId_ProductId",
                table: "ServicingCycle",
                columns: new[] { "ProductVersionId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCycle_RatingRuleVersionId_ProductId",
                table: "ServicingCycle",
                columns: new[] { "RatingRuleVersionId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCycle_RequestedBy",
                table: "ServicingCycle",
                column: "RequestedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCycle_RevisionId_DraftId",
                table: "ServicingCycle",
                columns: new[] { "RevisionId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCycle_RuntimeVersionId",
                table: "ServicingCycle",
                column: "RuntimeVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCycle_ScenarioVersionId",
                table: "ServicingCycle",
                column: "ScenarioVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCycle_WorkId",
                table: "ServicingCycle",
                column: "WorkId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ServicingDraft_ServicingCycle_CurrentCycleId_Id_PolicyId",
                table: "ServicingDraft",
                columns: new[] { "CurrentCycleId", "Id", "PolicyId" },
                principalTable: "ServicingCycle",
                principalColumns: new[] { "Id", "DraftId", "PolicyId" });
            AddCycleGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ServicingDraft_Cycle;");
            migrationBuilder.DropForeignKey(
                name: "FK_ServicingDraft_ServicingCycle_CurrentCycleId_Id_PolicyId",
                table: "ServicingDraft");

            migrationBuilder.DropTable(
                name: "ServicingCycle");

            migrationBuilder.DropIndex(
                name: "IX_ServicingDraft_CurrentCycleId_Id_PolicyId",
                table: "ServicingDraft");

            migrationBuilder.DropColumn(
                name: "CurrentCycleId",
                table: "ServicingDraft");
        }
    }
}
