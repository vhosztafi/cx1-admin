using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperationalTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperationalSubject",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ServicingDraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalSubject", x => x.Id);
                    table.CheckConstraint("CK_OperationalSubject_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_OperationalSubject_Creator", "[CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_OperationalSubject_Parent", "([Kind]='agency' AND [AgencyId] IS NOT NULL AND [RelationshipId] IS NULL AND [QuoteId] IS NULL AND [PolicyId] IS NULL AND [ServicingDraftId] IS NULL) OR ([Kind]='relationship' AND [RelationshipId] IS NOT NULL AND [AgencyId] IS NULL AND [QuoteId] IS NULL AND [PolicyId] IS NULL AND [ServicingDraftId] IS NULL) OR ([Kind]='quote' AND [QuoteId] IS NOT NULL AND [AgencyId] IS NULL AND [RelationshipId] IS NULL AND [PolicyId] IS NULL AND [ServicingDraftId] IS NULL) OR ([Kind]='policy' AND [PolicyId] IS NOT NULL AND [AgencyId] IS NULL AND [RelationshipId] IS NULL AND [QuoteId] IS NULL AND [ServicingDraftId] IS NULL) OR ([Kind]='servicing-draft' AND [ServicingDraftId] IS NOT NULL AND [AgencyId] IS NULL AND [RelationshipId] IS NULL AND [QuoteId] IS NULL AND [PolicyId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_OperationalSubject_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalSubject_ClientAgencyRelationship_RelationshipId",
                        column: x => x.RelationshipId,
                        principalTable: "ClientAgencyRelationship",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalSubject_Policy_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policy",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalSubject_Quote_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "Quote",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalSubject_ServicingDraft_ServicingDraftId",
                        column: x => x.ServicingDraftId,
                        principalTable: "ServicingDraft",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalSubject_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OperationalTask",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TypeCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    State = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    EventSequence = table.Column<int>(type: "int", nullable: false),
                    CompletionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SourceChanged = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalTask", x => x.Id);
                    table.CheckConstraint("CK_OperationalTask_Assignment", "[OwnerId] IS NULL OR [TeamId] IS NULL");
                    table.CheckConstraint("CK_OperationalTask_Completion", "[State] NOT IN ('completed','cancelled') OR ([CompletionReason] IS NOT NULL AND LEN(LTRIM(RTRIM([CompletionReason])))>0)");
                    table.CheckConstraint("CK_OperationalTask_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_OperationalTask_Creator", "[CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_OperationalTask_Priority", "[Priority] IN ('low','normal','high','urgent')");
                    table.CheckConstraint("CK_OperationalTask_Sequence", "[EventSequence]>=1");
                    table.CheckConstraint("CK_OperationalTask_State", "[State] IN ('open','in-progress','awaiting-information','blocked','completed','cancelled')");
                    table.CheckConstraint("CK_OperationalTask_Title", "LEN(LTRIM(RTRIM([Title])))>0");
                    table.CheckConstraint("CK_OperationalTask_Type", "[TypeCode] IN ('servicing','underwriting-referral','authority-referral','renewal','data-exception','agency-onboarding','complaint','underwriting')");
                    table.CheckConstraint("CK_OperationalTask_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_OperationalTask_OperationalSubject_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "OperationalSubject",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalTask_Team_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Team",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalTask_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalTask_User_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OperationalTaskChecklist",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    Completed = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalTaskChecklist", x => x.Id);
                    table.CheckConstraint("CK_OperationalTaskChecklist_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_OperationalTaskChecklist_Creator", "[CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_OperationalTaskChecklist_Ordinal", "[Ordinal]>=0");
                    table.CheckConstraint("CK_OperationalTaskChecklist_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_OperationalTaskChecklist_OperationalTask_TaskId",
                        column: x => x.TaskId,
                        principalTable: "OperationalTask",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalTaskChecklist_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OperationalTaskComment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    AuthorLabel = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalTaskComment", x => x.Id);
                    table.CheckConstraint("CK_OperationalTaskComment_Body", "LEN(LTRIM(RTRIM([Body])))>0");
                    table.CheckConstraint("CK_OperationalTaskComment_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_OperationalTaskComment_Creator", "[CreatedBy] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_OperationalTaskComment_OperationalTask_TaskId",
                        column: x => x.TaskId,
                        principalTable: "OperationalTask",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalTaskComment_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OperationalTaskEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ActorLabel = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalTaskEvent", x => x.Id);
                    table.CheckConstraint("CK_OperationalTaskEvent_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_OperationalTaskEvent_Creator", "[CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_OperationalTaskEvent_Sequence", "[Sequence]>0");
                    table.CheckConstraint("CK_OperationalTaskEvent_Snapshot", "ISJSON([SnapshotJson],OBJECT)=1");
                    table.ForeignKey(
                        name: "FK_OperationalTaskEvent_OperationalTask_TaskId",
                        column: x => x.TaskId,
                        principalTable: "OperationalTask",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalTaskEvent_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalSubject_AgencyId",
                table: "OperationalSubject",
                column: "AgencyId",
                unique: true,
                filter: "[AgencyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalSubject_CreatedBy",
                table: "OperationalSubject",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalSubject_PolicyId",
                table: "OperationalSubject",
                column: "PolicyId",
                unique: true,
                filter: "[PolicyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalSubject_QuoteId",
                table: "OperationalSubject",
                column: "QuoteId",
                unique: true,
                filter: "[QuoteId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalSubject_RelationshipId",
                table: "OperationalSubject",
                column: "RelationshipId",
                unique: true,
                filter: "[RelationshipId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalSubject_ServicingDraftId",
                table: "OperationalSubject",
                column: "ServicingDraftId",
                unique: true,
                filter: "[ServicingDraftId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalTask_CreatedBy_CreatedAt_Id",
                table: "OperationalTask",
                columns: new[] { "CreatedBy", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalTask_OwnerId_State_DueOn_Id",
                table: "OperationalTask",
                columns: new[] { "OwnerId", "State", "DueOn", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalTask_Reference",
                table: "OperationalTask",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationalTask_SubjectId_State_Id",
                table: "OperationalTask",
                columns: new[] { "SubjectId", "State", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalTask_TeamId_State_DueOn_Id",
                table: "OperationalTask",
                columns: new[] { "TeamId", "State", "DueOn", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalTaskChecklist_CreatedBy",
                table: "OperationalTaskChecklist",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalTaskChecklist_TaskId_Ordinal",
                table: "OperationalTaskChecklist",
                columns: new[] { "TaskId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationalTaskComment_CreatedBy",
                table: "OperationalTaskComment",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalTaskComment_TaskId_CreatedAt_Id",
                table: "OperationalTaskComment",
                columns: new[] { "TaskId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalTaskEvent_CreatedBy",
                table: "OperationalTaskEvent",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalTaskEvent_TaskId_Sequence",
                table: "OperationalTaskEvent",
                columns: new[] { "TaskId", "Sequence" },
                unique: true);
            AddOperationalGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM OperationalSubject) THROW 51983,'Operational history cannot be discarded by downgrade.',1;");
            migrationBuilder.DropTable(
                name: "OperationalTaskChecklist");

            migrationBuilder.DropTable(
                name: "OperationalTaskComment");

            migrationBuilder.DropTable(
                name: "OperationalTaskEvent");

            migrationBuilder.DropTable(
                name: "OperationalTask");

            migrationBuilder.DropTable(
                name: "OperationalSubject");
        }
    }
}
