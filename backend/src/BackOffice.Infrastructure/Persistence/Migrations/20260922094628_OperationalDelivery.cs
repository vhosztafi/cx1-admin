using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperationalDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperationalMessageVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalMessageVersion", x => x.Id);
                    table.CheckConstraint("CK_OperationalMessageVersion_Content", "[CreatedBy] IS NOT NULL AND LEN([ContentHash])=64");
                    table.CheckConstraint("CK_OperationalMessageVersion_ContentJson_Json", "ISJSON([ContentJson]) = 1");
                    table.CheckConstraint("CK_OperationalMessageVersion_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_OperationalMessageVersion_OperationalMessageDraft_MessageId",
                        column: x => x.MessageId,
                        principalTable: "OperationalMessageDraft",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalMessageVersion_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OperationalDelivery",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelationshipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResendOfId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OutcomeCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProviderOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalDelivery", x => x.Id);
                    table.CheckConstraint("CK_OperationalDelivery_CompletedAt_Utc", "DATEPART(TZOFFSET,[CompletedAt]) = 0");
                    table.CheckConstraint("CK_OperationalDelivery_Content", "[CreatedBy] IS NOT NULL AND LEN([ContentHash])=64 AND ([ResendOfId] IS NULL OR [ResendOfId]<>[Id])");
                    table.CheckConstraint("CK_OperationalDelivery_ContentJson_Json", "ISJSON([ContentJson]) = 1");
                    table.CheckConstraint("CK_OperationalDelivery_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_OperationalDelivery_State", "[State] IN ('queued','delivered','failed','superseded')");
                    table.CheckConstraint("CK_OperationalDelivery_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_OperationalDelivery_AdapterAttempt_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "AdapterAttempt",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalDelivery_ClientAgencyRelationship_RelationshipId",
                        column: x => x.RelationshipId,
                        principalTable: "ClientAgencyRelationship",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalDelivery_DemoProviderOperation_ProviderOperationId",
                        column: x => x.ProviderOperationId,
                        principalTable: "DemoProviderOperation",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalDelivery_OperationalDelivery_ResendOfId",
                        column: x => x.ResendOfId,
                        principalTable: "OperationalDelivery",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalDelivery_OperationalMessageVersion_MessageVersionId",
                        column: x => x.MessageVersionId,
                        principalTable: "OperationalMessageVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalDelivery_OperationalSubject_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "OperationalSubject",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalDelivery_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalDelivery_SettingVersion_ScenarioVersionId",
                        column: x => x.ScenarioVersionId,
                        principalTable: "SettingVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalDelivery_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OperationalDeliveryAttachment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OriginalName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    MediaType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalDeliveryAttachment", x => x.Id);
                    table.CheckConstraint("CK_OperationalDeliveryAttachment_Content", "[CreatedBy] IS NOT NULL AND LEN([ContentHash])=64 AND [Length]>0");
                    table.CheckConstraint("CK_OperationalDeliveryAttachment_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_OperationalDeliveryAttachment_DocumentVersion_DocumentVersionId",
                        column: x => x.DocumentVersionId,
                        principalTable: "DocumentVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalDeliveryAttachment_FileObject_FileObjectId",
                        column: x => x.FileObjectId,
                        principalTable: "FileObject",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalDeliveryAttachment_OperationalDelivery_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "OperationalDelivery",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalDeliveryAttachment_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OperationalDeliveryRecipient",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeliveryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalDeliveryRecipient", x => x.Id);
                    table.CheckConstraint("CK_OperationalDeliveryRecipient_Content", "[CreatedBy] IS NOT NULL AND LEN(TRIM([Name]))>0 AND LEN(TRIM([Email]))>0");
                    table.CheckConstraint("CK_OperationalDeliveryRecipient_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_OperationalDeliveryRecipient_Contact_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contact",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalDeliveryRecipient_OperationalDelivery_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "OperationalDelivery",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OperationalDeliveryRecipient_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalDelivery_AttemptId",
                table: "OperationalDelivery",
                column: "AttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalDelivery_CreatedBy",
                table: "OperationalDelivery",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalDelivery_MessageVersionId",
                table: "OperationalDelivery",
                column: "MessageVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalDelivery_ProviderOperationId",
                table: "OperationalDelivery",
                column: "ProviderOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalDelivery_RelationshipId",
                table: "OperationalDelivery",
                column: "RelationshipId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalDelivery_ResendOfId",
                table: "OperationalDelivery",
                column: "ResendOfId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalDelivery_ScenarioVersionId",
                table: "OperationalDelivery",
                column: "ScenarioVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalDelivery_SubjectId_CreatedAt_Id",
                table: "OperationalDelivery",
                columns: new[] { "SubjectId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalDelivery_WorkId",
                table: "OperationalDelivery",
                column: "WorkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationalDeliveryAttachment_CreatedBy",
                table: "OperationalDeliveryAttachment",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalDeliveryAttachment_DeliveryId_DocumentVersionId",
                table: "OperationalDeliveryAttachment",
                columns: new[] { "DeliveryId", "DocumentVersionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationalDeliveryAttachment_DocumentVersionId",
                table: "OperationalDeliveryAttachment",
                column: "DocumentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalDeliveryAttachment_FileObjectId",
                table: "OperationalDeliveryAttachment",
                column: "FileObjectId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalDeliveryRecipient_ContactId",
                table: "OperationalDeliveryRecipient",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalDeliveryRecipient_CreatedBy",
                table: "OperationalDeliveryRecipient",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalDeliveryRecipient_DeliveryId_ContactId",
                table: "OperationalDeliveryRecipient",
                columns: new[] { "DeliveryId", "ContactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationalMessageVersion_CreatedBy",
                table: "OperationalMessageVersion",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_OperationalMessageVersion_MessageId",
                table: "OperationalMessageVersion",
                column: "MessageId",
                unique: true);
            AddDeliveryGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM OperationalDelivery) OR EXISTS(SELECT 1 FROM OperationalMessageVersion) THROW 52000,'Cannot remove retained delivery history.',1;");
            migrationBuilder.DropTable(
                name: "OperationalDeliveryAttachment");

            migrationBuilder.DropTable(
                name: "OperationalDeliveryRecipient");

            migrationBuilder.DropTable(
                name: "OperationalDelivery");

            migrationBuilder.DropTable(
                name: "OperationalMessageVersion");
        }
    }
}
