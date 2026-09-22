using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaskDocumentAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskDocumentAttachment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorLabel = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RemovedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RemovalReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskDocumentAttachment", x => x.Id);
                    table.CheckConstraint("CK_TaskDocumentAttachment_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_TaskDocumentAttachment_Creator", "[CreatedBy] IS NOT NULL AND LEN(TRIM([AuthorLabel]))>0 AND LEN(TRIM([Reason]))>0");
                    table.CheckConstraint("CK_TaskDocumentAttachment_Removal", "([RemovedAt] IS NULL AND [RemovedBy] IS NULL AND [RemovalReason] IS NULL) OR ([RemovedAt] IS NOT NULL AND [RemovedBy] IS NOT NULL AND [RemovalReason] IS NOT NULL AND LEN(TRIM([RemovalReason]))>0 AND [RemovedAt]>=[CreatedAt] AND DATEPART(TZOFFSET,[RemovedAt])=0)");
                    table.CheckConstraint("CK_TaskDocumentAttachment_RemovedAt_Utc", "DATEPART(TZOFFSET,[RemovedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_TaskDocumentAttachment_DocumentVersion_DocumentVersionId",
                        column: x => x.DocumentVersionId,
                        principalTable: "DocumentVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TaskDocumentAttachment_OperationalTask_TaskId",
                        column: x => x.TaskId,
                        principalTable: "OperationalTask",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TaskDocumentAttachment_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TaskDocumentAttachment_User_RemovedBy",
                        column: x => x.RemovedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskDocumentAttachment_CreatedBy",
                table: "TaskDocumentAttachment",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_TaskDocumentAttachment_DocumentVersionId",
                table: "TaskDocumentAttachment",
                column: "DocumentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskDocumentAttachment_RemovedBy",
                table: "TaskDocumentAttachment",
                column: "RemovedBy");

            migrationBuilder.CreateIndex(
                name: "IX_TaskDocumentAttachment_TaskId_DocumentVersionId",
                table: "TaskDocumentAttachment",
                columns: new[] { "TaskId", "DocumentVersionId" },
                unique: true,
                filter: "[RemovedAt] IS NULL");
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_TaskDocumentAttachment_OriginalSubject ON TaskDocumentAttachment AFTER INSERT AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM inserted i JOIN OperationalTask t ON t.Id=i.TaskId
                    JOIN DocumentVersion v ON v.Id=i.DocumentVersionId JOIN Document d ON d.Id=v.DocumentId
                    WHERE t.SubjectId<>d.SubjectId)
                    THROW 52001,'Task evidence must belong to the original task subject.',1;
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_TaskDocumentAttachment_RetainedIdentity ON TaskDocumentAttachment AFTER UPDATE,DELETE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id
                    WHERE i.Id IS NULL OR d.RemovedAt IS NOT NULL OR i.RemovedAt IS NULL)
                    THROW 52000,'Task attachment history is retained.',1;
                  IF UPDATE(Id) OR UPDATE(TaskId) OR UPDATE(DocumentVersionId) OR UPDATE(CreatedBy) OR UPDATE(CreatedAt) OR UPDATE(AuthorLabel) OR UPDATE(Reason)
                    THROW 52000,'Task attachment identity is immutable.',1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS(SELECT 1 FROM TaskDocumentAttachment)
                  THROW 52000,'Cannot remove retained task attachment history.',1;
                """);
            migrationBuilder.DropTable(
                name: "TaskDocumentAttachment");
        }
    }
}
