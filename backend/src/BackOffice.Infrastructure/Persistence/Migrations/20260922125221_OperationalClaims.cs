using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperationalClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClaimsAdministrator",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimsAdministrator", x => x.Id);
                    table.CheckConstraint("CK_ClaimsAdministrator_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ClaimsAdministrator_State", "[State] IN ('active','inactive')");
                    table.CheckConstraint("CK_ClaimsAdministrator_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_ClaimsAdministrator_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ClaimsHandoff",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResolutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdministratorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OutcomeCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProviderReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimsHandoff", x => x.Id);
                    table.CheckConstraint("CK_ClaimsHandoff_CompletedAt_Utc", "DATEPART(TZOFFSET,[CompletedAt]) = 0");
                    table.CheckConstraint("CK_ClaimsHandoff_Content", "ISJSON([RequestJson])=1 AND LEN([RequestHash])=64 AND [CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_ClaimsHandoff_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ClaimsHandoff_State", "[State] IN ('queued','acknowledged','rejected','failed','superseded')");
                    table.CheckConstraint("CK_ClaimsHandoff_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_ClaimsHandoff_ClaimsAdministrator_AdministratorId",
                        column: x => x.AdministratorId,
                        principalTable: "ClaimsAdministrator",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClaimsHandoff_IncidentOccurrenceResolution_ResolutionId",
                        column: x => x.ResolutionId,
                        principalTable: "IncidentOccurrenceResolution",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClaimsHandoff_IncidentRevision_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "IncidentRevision",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClaimsHandoff_Incident_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incident",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClaimsHandoff_PolicyVersion_SourceVersionId",
                        column: x => x.SourceVersionId,
                        principalTable: "PolicyVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClaimsHandoff_Policy_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policy",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClaimsHandoff_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ClaimsRequest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HandoffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimsRequest", x => x.Id);
                    table.CheckConstraint("CK_ClaimsRequest_Content", "ISJSON([PayloadJson])=1 AND LEN([PayloadHash])=64 AND [CreatedBy] IS NOT NULL AND [Purpose] IN ('handoff','refresh','contact')");
                    table.CheckConstraint("CK_ClaimsRequest_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_ClaimsRequest_ClaimsHandoff_HandoffId",
                        column: x => x.HandoffId,
                        principalTable: "ClaimsHandoff",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClaimsRequest_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClaimsRequest_SettingVersion_ScenarioVersionId",
                        column: x => x.ScenarioVersionId,
                        principalTable: "SettingVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClaimsRequest_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ClaimsSummary",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HandoffId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderEventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SummaryJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AsOf = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimsSummary", x => x.Id);
                    table.CheckConstraint("CK_ClaimsSummary_AsOf_Utc", "DATEPART(TZOFFSET,[AsOf]) = 0");
                    table.CheckConstraint("CK_ClaimsSummary_Content", "ISJSON([SummaryJson])=1 AND LEN([ContentHash])=64 AND [AsOf]<=[ReceivedAt] AND [ReceivedAt]=[CreatedAt]");
                    table.CheckConstraint("CK_ClaimsSummary_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ClaimsSummary_ReceivedAt_Utc", "DATEPART(TZOFFSET,[ReceivedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_ClaimsSummary_ClaimsHandoff_HandoffId",
                        column: x => x.HandoffId,
                        principalTable: "ClaimsHandoff",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClaimsSummary_ClaimsRequest_RequestId",
                        column: x => x.RequestId,
                        principalTable: "ClaimsRequest",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClaimsSummary_DemoProviderOperation_ProviderOperationId",
                        column: x => x.ProviderOperationId,
                        principalTable: "DemoProviderOperation",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClaimsSummary_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimsAdministrator_Code",
                table: "ClaimsAdministrator",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClaimsAdministrator_CreatedBy",
                table: "ClaimsAdministrator",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimsHandoff_AdministratorId",
                table: "ClaimsHandoff",
                column: "AdministratorId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimsHandoff_CreatedBy",
                table: "ClaimsHandoff",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimsHandoff_IncidentId_CreatedAt_Id",
                table: "ClaimsHandoff",
                columns: new[] { "IncidentId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimsHandoff_PolicyId",
                table: "ClaimsHandoff",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimsHandoff_ResolutionId",
                table: "ClaimsHandoff",
                column: "ResolutionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimsHandoff_RevisionId",
                table: "ClaimsHandoff",
                column: "RevisionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClaimsHandoff_SourceVersionId",
                table: "ClaimsHandoff",
                column: "SourceVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimsRequest_CreatedBy",
                table: "ClaimsRequest",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimsRequest_HandoffId_Purpose",
                table: "ClaimsRequest",
                columns: new[] { "HandoffId", "Purpose" },
                unique: true,
                filter: "[Purpose]='handoff'");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimsRequest_ScenarioVersionId",
                table: "ClaimsRequest",
                column: "ScenarioVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimsRequest_WorkId",
                table: "ClaimsRequest",
                column: "WorkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClaimsSummary_CreatedBy",
                table: "ClaimsSummary",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimsSummary_HandoffId_AsOf_ReceivedAt_Id",
                table: "ClaimsSummary",
                columns: new[] { "HandoffId", "AsOf", "ReceivedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimsSummary_ProviderEventId",
                table: "ClaimsSummary",
                column: "ProviderEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClaimsSummary_ProviderOperationId",
                table: "ClaimsSummary",
                column: "ProviderOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimsSummary_RequestId",
                table: "ClaimsSummary",
                column: "RequestId");
            AddClaimsGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM ClaimsHandoff) THROW 52000,'Cannot remove retained claims history.',1;");
            migrationBuilder.Sql("DROP TRIGGER TR_Incident_ClaimsPin; DROP TRIGGER TR_IncidentRevision_ClaimsPin;");
            migrationBuilder.DropTable(
                name: "ClaimsSummary");

            migrationBuilder.DropTable(
                name: "ClaimsRequest");

            migrationBuilder.DropTable(
                name: "ClaimsHandoff");

            migrationBuilder.DropTable(
                name: "ClaimsAdministrator");
        }
    }
}
