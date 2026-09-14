using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgencyStateProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgencyStateRequest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RequestedState = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BaseVersion = table.Column<byte[]>(type: "binary(8)", nullable: false),
                    ProposedInputFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DecisionBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecisionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgencyStateRequest", x => x.Id);
                    table.CheckConstraint("CK_AgencyStateRequest_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_AgencyStateRequest_DecidedAt_Utc", "DATEPART(TZOFFSET,[DecidedAt]) = 0");
                    table.CheckConstraint("CK_AgencyStateRequest_Decision", "([State]='pending' AND [DecisionBy] IS NULL AND [DecisionReason] IS NULL AND [DecidedAt] IS NULL) OR ([State] IN ('applied','rejected') AND [DecisionBy] IS NOT NULL AND [DecisionBy]<>[RequestedBy] AND [DecisionReason] IS NOT NULL AND LEN(TRIM([DecisionReason]))>0 AND [DecidedAt] IS NOT NULL AND [DecidedAt]>=[CreatedAt]) OR ([State]='stale' AND [DecisionBy] IS NULL AND [DecisionReason] IS NOT NULL AND LEN(TRIM([DecisionReason]))>0 AND [DecidedAt] IS NOT NULL AND [DecidedAt]>=[CreatedAt])");
                    table.CheckConstraint("CK_AgencyStateRequest_Fingerprint", "LEN([ProposedInputFingerprint])=64 AND [ProposedInputFingerprint] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_AgencyStateRequest_KindState", "([Kind] IN ('activation','reactivation') AND [RequestedState]='active') OR ([Kind]='suspension' AND [RequestedState]='suspended')");
                    table.CheckConstraint("CK_AgencyStateRequest_Requester", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[RequestedBy] AND LEN(TRIM([RequestReason]))>0");
                    table.CheckConstraint("CK_AgencyStateRequest_State", "[State] IN ('pending','applied','rejected','stale')");
                    table.CheckConstraint("CK_AgencyStateRequest_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_AgencyStateRequest_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyStateRequest_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyStateRequest_User_DecisionBy",
                        column: x => x.DecisionBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyStateRequest_User_RequestedBy",
                        column: x => x.RequestedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyStateRequest_AgencyId_CreatedAt_Id",
                table: "AgencyStateRequest",
                columns: new[] { "AgencyId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyStateRequest_AgencyId_Kind",
                table: "AgencyStateRequest",
                columns: new[] { "AgencyId", "Kind" },
                unique: true,
                filter: "[State] = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyStateRequest_CreatedBy",
                table: "AgencyStateRequest",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyStateRequest_DecisionBy",
                table: "AgencyStateRequest",
                column: "DecisionBy");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyStateRequest_RequestedBy",
                table: "AgencyStateRequest",
                column: "RequestedBy");
            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_AgencyStateRequest_Lifecycle] ON [AgencyStateRequest] AFTER INSERT,UPDATE,DELETE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL) THROW 51000,'Agency proposal history cannot be deleted.',1;
                  IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id WHERE d.Id IS NULL AND i.State<>'pending') THROW 51000,'Agency proposals must start pending.',1;
                  IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE d.State<>'pending' OR EXISTS(
                    SELECT i.AgencyId,i.Kind COLLATE Latin1_General_100_BIN2,i.RequestedState COLLATE Latin1_General_100_BIN2,i.BaseVersion,i.ProposedInputFingerprint COLLATE Latin1_General_100_BIN2,i.RequestedBy,i.RequestReason COLLATE Latin1_General_100_BIN2,i.CreatedAt,i.CreatedBy
                    EXCEPT SELECT d.AgencyId,d.Kind COLLATE Latin1_General_100_BIN2,d.RequestedState COLLATE Latin1_General_100_BIN2,d.BaseVersion,d.ProposedInputFingerprint COLLATE Latin1_General_100_BIN2,d.RequestedBy,d.RequestReason COLLATE Latin1_General_100_BIN2,d.CreatedAt,d.CreatedBy)) THROW 51000,'Agency proposal content and terminal decisions are immutable.',1;
                  IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id WHERE d.Id IS NULL AND NOT EXISTS(
                    SELECT 1 FROM [User] u JOIN UserRole ur ON ur.UserId=u.Id JOIN Role r ON r.Id=ur.RoleId WHERE u.Id=i.RequestedBy AND u.State='active' AND u.AgencyId IS NULL AND r.Scope='internal' AND r.Code IN ('agency-admin','system-admin'))) THROW 51000,'Agency proposals require current internal administration.',1;
                  IF EXISTS(SELECT 1 FROM inserted i WHERE i.State IN ('applied','rejected') AND NOT EXISTS(
                    SELECT 1 FROM [User] u JOIN UserRole ur ON ur.UserId=u.Id JOIN Role r ON r.Id=ur.RoleId WHERE u.Id=i.DecisionBy AND u.State='active' AND u.AgencyId IS NULL AND r.Scope='internal' AND r.Code IN ('agency-admin','system-admin'))) THROW 51000,'Agency decisions require current internal administration.',1;
                END
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgencyStateRequest");
        }
    }
}
