using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperationalCommunication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InternalNote",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    AuthorLabel = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternalNote", x => x.Id);
                    table.CheckConstraint("CK_InternalNote_Content", "[CreatedBy] IS NOT NULL AND LEN(TRIM([Body]))>0 AND LEN(TRIM([AuthorLabel]))>0");
                    table.CheckConstraint("CK_InternalNote_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_InternalNote_OperationalSubject_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "OperationalSubject",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InternalNote_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OperationalThread",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Visibility = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    AuthorLabel = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalThread", x => x.Id);
                    table.CheckConstraint("CK_OperationalThread_Audience", "([Visibility]='internal' AND [RelationshipId] IS NULL) OR ([Visibility]='agency' AND [RelationshipId] IS NOT NULL)");
                    table.CheckConstraint("CK_OperationalThread_Content", "[CreatedBy] IS NOT NULL AND LEN(TRIM([Subject]))>0 AND LEN(TRIM([AuthorLabel]))>0");
                    table.CheckConstraint("CK_OperationalThread_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_OperationalThread_ClientAgencyRelationship_RelationshipId",
                        column: x => x.RelationshipId,
                        principalTable: "ClientAgencyRelationship",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalThread_OperationalSubject_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "OperationalSubject",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalThread_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OperationalMessageDraft",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ThreadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    AuthorLabel = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalMessageDraft", x => x.Id);
                    table.CheckConstraint("CK_OperationalMessageDraft_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_OperationalMessageDraft_Creator", "[CreatedBy] IS NOT NULL AND LEN(TRIM([AuthorLabel]))>0");
                    table.CheckConstraint("CK_OperationalMessageDraft_State", "[State] IN ('draft','queued','sent','failed','superseded')");
                    table.CheckConstraint("CK_OperationalMessageDraft_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_OperationalMessageDraft_OperationalThread_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "OperationalThread",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalMessageDraft_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MessageDraftAttachment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageDraftAttachment", x => x.Id);
                    table.CheckConstraint("CK_MessageDraftAttachment_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_MessageDraftAttachment_Creator", "[CreatedBy] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_MessageDraftAttachment_DocumentVersion_DocumentVersionId",
                        column: x => x.DocumentVersionId,
                        principalTable: "DocumentVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MessageDraftAttachment_OperationalMessageDraft_MessageId",
                        column: x => x.MessageId,
                        principalTable: "OperationalMessageDraft",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MessageDraftAttachment_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MessageDraftRecipient",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageDraftRecipient", x => x.Id);
                    table.CheckConstraint("CK_MessageDraftRecipient_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_MessageDraftRecipient_Creator", "[CreatedBy] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_MessageDraftRecipient_Contact_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contact",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MessageDraftRecipient_OperationalMessageDraft_MessageId",
                        column: x => x.MessageId,
                        principalTable: "OperationalMessageDraft",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MessageDraftRecipient_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_InternalNote_CreatedBy",
                table: "InternalNote",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_InternalNote_SubjectId_CreatedAt_Id",
                table: "InternalNote",
                columns: new[] { "SubjectId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageDraftAttachment_CreatedBy",
                table: "MessageDraftAttachment",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_MessageDraftAttachment_DocumentVersionId",
                table: "MessageDraftAttachment",
                column: "DocumentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageDraftAttachment_MessageId_DocumentVersionId",
                table: "MessageDraftAttachment",
                columns: new[] { "MessageId", "DocumentVersionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MessageDraftRecipient_ContactId",
                table: "MessageDraftRecipient",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageDraftRecipient_CreatedBy",
                table: "MessageDraftRecipient",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_MessageDraftRecipient_MessageId_ContactId",
                table: "MessageDraftRecipient",
                columns: new[] { "MessageId", "ContactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationalMessageDraft_CreatedBy",
                table: "OperationalMessageDraft",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalMessageDraft_ThreadId_CreatedAt_Id",
                table: "OperationalMessageDraft",
                columns: new[] { "ThreadId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalThread_CreatedBy",
                table: "OperationalThread",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalThread_RelationshipId",
                table: "OperationalThread",
                column: "RelationshipId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalThread_SubjectId_Visibility_CreatedAt_Id",
                table: "OperationalThread",
                columns: new[] { "SubjectId", "Visibility", "CreatedAt", "Id" });
            AddCommunicationGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM InternalNote) OR EXISTS(SELECT 1 FROM OperationalThread) THROW 52000,'Cannot remove retained communication history.',1;");
            migrationBuilder.DropTable(
                name: "InternalNote");

            migrationBuilder.DropTable(
                name: "MessageDraftAttachment");

            migrationBuilder.DropTable(
                name: "MessageDraftRecipient");

            migrationBuilder.DropTable(
                name: "OperationalMessageDraft");

            migrationBuilder.DropTable(
                name: "OperationalThread");
        }
    }
}
