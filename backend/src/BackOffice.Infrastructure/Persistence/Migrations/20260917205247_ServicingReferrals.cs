using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingReferrals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_UserAuthorityGrant_Id_UserId_AuthorityVersionId",
                table: "UserAuthorityGrant",
                columns: new[] { "Id", "UserId", "AuthorityVersionId" });

            migrationBuilder.CreateTable(
                name: "ServicingReferral",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    RuleCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Dimension = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    RiskItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TargetKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequiredAuthorityJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AssignedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LatestDecisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicingReferral", x => x.Id);
                    table.UniqueConstraint("AK_ServicingReferral_Id_CycleId_DraftId_RevisionId_RatingId", x => new { x.Id, x.CycleId, x.DraftId, x.RevisionId, x.RatingId });
                    table.CheckConstraint("CK_ServicingReferral_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ServicingReferral_RequiredAuthorityJson", "ISJSON([RequiredAuthorityJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[RequiredAuthorityJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
                    table.CheckConstraint("CK_ServicingReferral_Sequence", "[Sequence]>0");
                    table.CheckConstraint("CK_ServicingReferral_State", "[State] IN ('open','approved','conditional','queried','declined','superseded') AND ([State] IN ('open','superseded') OR [LatestDecisionId] IS NOT NULL)");
                    table.CheckConstraint("CK_ServicingReferral_Target", "([RiskItemId] IS NULL AND [TargetKey]='00000000-0000-0000-0000-000000000000') OR ([RiskItemId] IS NOT NULL AND [RiskItemId]<>'00000000-0000-0000-0000-000000000000' AND [TargetKey]=[RiskItemId])");
                    table.CheckConstraint("CK_ServicingReferral_Text", "LEN(TRIM([RuleCode]))>0 AND LEN(TRIM([Dimension]))>0 AND LEN(TRIM([Reason]))>=10 AND [CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_ServicingReferral_Triggers", "JSON_QUERY([RequiredAuthorityJson],'$.triggers[0]') IS NOT NULL");
                    table.CheckConstraint("CK_ServicingReferral_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_ServicingReferral_ServicingRatingResult_RatingId_CycleId_DraftId_RevisionId",
                        columns: x => new { x.RatingId, x.CycleId, x.DraftId, x.RevisionId },
                        principalTable: "ServicingRatingResult",
                        principalColumns: new[] { "Id", "CycleId", "DraftId", "RevisionId" });
                    table.ForeignKey(
                        name: "FK_ServicingReferral_User_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingReferral_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ServicingReferralDecision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferralId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Question = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorityVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConditionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicingReferralDecision", x => x.Id);
                    table.UniqueConstraint("AK_ServicingReferralDecision_Id_ReferralId_CycleId_DraftId_RevisionId_RatingId", x => new { x.Id, x.ReferralId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId });
                    table.CheckConstraint("CK_ServicingReferralDecision_Conditions", "ISJSON([ConditionsJson],ARRAY)=1 AND DATALENGTH([ConditionsJson])<=131072 AND (([Outcome] IN ('approve-with-conditions','query') AND JSON_QUERY([ConditionsJson],'$[0]') IS NOT NULL) OR ([Outcome] NOT IN ('approve-with-conditions','query') AND [ConditionsJson]='[]'))");
                    table.CheckConstraint("CK_ServicingReferralDecision_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ServicingReferralDecision_DecidedAt_Utc", "DATEPART(TZOFFSET,[DecidedAt]) = 0");
                    table.CheckConstraint("CK_ServicingReferralDecision_Outcome", "[Outcome] IN ('approve','approve-with-conditions','query','decline','reopen')");
                    table.CheckConstraint("CK_ServicingReferralDecision_Question", "([Outcome]='query' AND [Question] IS NOT NULL AND LEN(TRIM([Question]))>=10) OR ([Outcome]<>'query' AND [Question] IS NULL)");
                    table.CheckConstraint("CK_ServicingReferralDecision_Reason", "LEN(TRIM([Reason]))>=10 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId] AND [DecidedAt]>=[CreatedAt]");
                    table.CheckConstraint("CK_ServicingReferralDecision_Sequence", "[Sequence]>0");
                    table.ForeignKey(
                        name: "FK_ServicingReferralDecision_ServicingReferral_ReferralId_CycleId_DraftId_RevisionId_RatingId",
                        columns: x => new { x.ReferralId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId },
                        principalTable: "ServicingReferral",
                        principalColumns: new[] { "Id", "CycleId", "DraftId", "RevisionId", "RatingId" });
                    table.ForeignKey(
                        name: "FK_ServicingReferralDecision_UserAuthorityGrant_GrantId_ActorId_AuthorityVersionId",
                        columns: x => new { x.GrantId, x.ActorId, x.AuthorityVersionId },
                        principalTable: "UserAuthorityGrant",
                        principalColumns: new[] { "Id", "UserId", "AuthorityVersionId" });
                    table.ForeignKey(
                        name: "FK_ServicingReferralDecision_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingReferral_AssignedUserId",
                table: "ServicingReferral",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingReferral_CreatedBy",
                table: "ServicingReferral",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingReferral_CycleId_RuleCode_Dimension_TargetKey",
                table: "ServicingReferral",
                columns: new[] { "CycleId", "RuleCode", "Dimension", "TargetKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicingReferral_CycleId_Sequence",
                table: "ServicingReferral",
                columns: new[] { "CycleId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicingReferral_LatestDecisionId_Id_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingReferral",
                columns: new[] { "LatestDecisionId", "Id", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingReferral_RatingId_CycleId_DraftId_RevisionId",
                table: "ServicingReferral",
                columns: new[] { "RatingId", "CycleId", "DraftId", "RevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingReferral_State_AssignedUserId_DraftId",
                table: "ServicingReferral",
                columns: new[] { "State", "AssignedUserId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingReferralDecision_CreatedBy",
                table: "ServicingReferralDecision",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingReferralDecision_GrantId_ActorId_AuthorityVersionId",
                table: "ServicingReferralDecision",
                columns: new[] { "GrantId", "ActorId", "AuthorityVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingReferralDecision_ReferralId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingReferralDecision",
                columns: new[] { "ReferralId", "CycleId", "DraftId", "RevisionId", "RatingId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingReferralDecision_ReferralId_Sequence",
                table: "ServicingReferralDecision",
                columns: new[] { "ReferralId", "Sequence" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ServicingReferral_ServicingReferralDecision_LatestDecisionId_Id_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingReferral",
                columns: new[] { "LatestDecisionId", "Id", "CycleId", "DraftId", "RevisionId", "RatingId" },
                principalTable: "ServicingReferralDecision",
                principalColumns: new[] { "Id", "ReferralId", "CycleId", "DraftId", "RevisionId", "RatingId" });
            AddReferralGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ServicingReferral_Source; DROP TRIGGER IF EXISTS TR_ServicingReferral_History; DROP TRIGGER IF EXISTS TR_ServicingReferralDecision_Source; DROP TRIGGER IF EXISTS TR_ServicingReferralDecision_AppendOnly;");
            migrationBuilder.DropForeignKey(
                name: "FK_ServicingReferral_ServicingReferralDecision_LatestDecisionId_Id_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingReferral");

            migrationBuilder.DropTable(
                name: "ServicingReferralDecision");

            migrationBuilder.DropTable(
                name: "ServicingReferral");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_UserAuthorityGrant_Id_UserId_AuthorityVersionId",
                table: "UserAuthorityGrant");
        }
    }
}
