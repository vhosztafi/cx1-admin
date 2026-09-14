using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgencyNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgencyNotification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ProtectedPayload = table.Column<string>(type: "nvarchar(max)", maxLength: 30000, nullable: false),
                    ContentHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgencyNotification", x => x.Id);
                    table.UniqueConstraint("AK_AgencyNotification_Id_AgencyId", x => new { x.Id, x.AgencyId });
                    table.CheckConstraint("CK_AgencyNotification_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_AgencyNotification_Payload", "LEN([ProtectedPayload]) > 0");
                    table.CheckConstraint("CK_AgencyNotification_Purpose", "[Purpose] IN ('agency-activated','agency-invitation')");
                    table.ForeignKey(
                        name: "FK_AgencyNotification_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyNotification_OutboxWork_WorkId",
                        column: x => x.WorkId,
                        principalTable: "OutboxWork",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyNotification_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AgencyNotificationReceipt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Accepted = table.Column<bool>(type: "bit", nullable: false),
                    ResultCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgencyNotificationReceipt", x => x.Id);
                    table.CheckConstraint("CK_AgencyNotificationReceipt_CompletedAt_Utc", "DATEPART(TZOFFSET,[CompletedAt]) = 0");
                    table.CheckConstraint("CK_AgencyNotificationReceipt_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_AgencyNotificationReceipt_Outcome", "([Accepted]=1 AND [ResultCode]='demo-delivered') OR ([Accepted]=0 AND [ResultCode]='demo-rejected')");
                    table.ForeignKey(
                        name: "FK_AgencyNotificationReceipt_AgencyNotification_NotificationId_AgencyId",
                        columns: x => new { x.NotificationId, x.AgencyId },
                        principalTable: "AgencyNotification",
                        principalColumns: new[] { "Id", "AgencyId" });
                    table.ForeignKey(
                        name: "FK_AgencyNotificationReceipt_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyNotification_AgencyId_CreatedAt_Id",
                table: "AgencyNotification",
                columns: new[] { "AgencyId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyNotification_CreatedBy",
                table: "AgencyNotification",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyNotification_WorkId",
                table: "AgencyNotification",
                column: "WorkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgencyNotificationReceipt_CreatedBy",
                table: "AgencyNotificationReceipt",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyNotificationReceipt_NotificationId",
                table: "AgencyNotificationReceipt",
                column: "NotificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgencyNotificationReceipt_NotificationId_AgencyId",
                table: "AgencyNotificationReceipt",
                columns: new[] { "NotificationId", "AgencyId" });
            migrationBuilder.Sql("CREATE TRIGGER [TR_AgencyNotification_Immutable] ON [AgencyNotification] AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; THROW 51000, 'Agency notification history is immutable.', 1; END");
            migrationBuilder.Sql("CREATE TRIGGER [TR_AgencyNotificationReceipt_Immutable] ON [AgencyNotificationReceipt] AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; THROW 51000, 'Agency notification history is immutable.', 1; END");
            migrationBuilder.Sql("CREATE TRIGGER [TR_AgencyNotification_Ownership] ON [AgencyNotification] AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM inserted i JOIN [OutboxWork] w ON w.Id=i.WorkId WHERE w.Kind<>'agency-notification' OR w.SubjectRecordId IS NULL OR w.SubjectRecordId<>i.AgencyId OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(w.Payload,'$.notificationId')) IS NULL OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(w.Payload,'$.notificationId'))<>i.Id) THROW 51000, 'Agency notification job ownership is invalid.', 1; END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgencyNotificationReceipt");

            migrationBuilder.DropTable(
                name: "AgencyNotification");
        }
    }
}
