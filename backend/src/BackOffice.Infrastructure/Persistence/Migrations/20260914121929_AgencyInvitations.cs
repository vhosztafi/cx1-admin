using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgencyInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AgencyId",
                table: "User",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AgencyInvitation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TokenHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgencyInvitation", x => x.Id);
                    table.CheckConstraint("CK_AgencyInvitation_AcceptedAt_Utc", "DATEPART(TZOFFSET,[AcceptedAt]) = 0");
                    table.CheckConstraint("CK_AgencyInvitation_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_AgencyInvitation_ExpiresAt_Utc", "DATEPART(TZOFFSET,[ExpiresAt]) = 0");
                    table.CheckConstraint("CK_AgencyInvitation_IssuedAt_Utc", "DATEPART(TZOFFSET,[IssuedAt]) = 0");
                    table.CheckConstraint("CK_AgencyInvitation_IssuedBundle", "([TokenHash] IS NULL AND [IssuedAt] IS NULL AND [ExpiresAt] IS NULL AND [NotificationId] IS NULL) OR ([TokenHash] IS NOT NULL AND [IssuedAt] IS NOT NULL AND [ExpiresAt] IS NOT NULL AND [NotificationId] IS NOT NULL AND [ExpiresAt]=DATEADD(day,14,[IssuedAt]))");
                    table.CheckConstraint("CK_AgencyInvitation_Lifecycle", "([State]='staged' AND [TokenHash] IS NULL AND [AcceptedAt] IS NULL AND [RevokedAt] IS NULL) OR ([State] IN ('pending','expired') AND [TokenHash] IS NOT NULL AND [AcceptedAt] IS NULL AND [RevokedAt] IS NULL) OR ([State]='accepted' AND [TokenHash] IS NOT NULL AND [AcceptedAt] IS NOT NULL AND [AcceptedAt]>=[IssuedAt] AND [AcceptedAt]<=[ExpiresAt] AND [RevokedAt] IS NULL) OR ([State]='revoked' AND [AcceptedAt] IS NULL AND [RevokedAt] IS NOT NULL)");
                    table.CheckConstraint("CK_AgencyInvitation_RevokedAt_Utc", "DATEPART(TZOFFSET,[RevokedAt]) = 0");
                    table.CheckConstraint("CK_AgencyInvitation_State", "[State] IN ('staged','pending','accepted','expired','revoked')");
                    table.CheckConstraint("CK_AgencyInvitation_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_AgencyInvitation_AgencyNotification_NotificationId_AgencyId",
                        columns: x => new { x.NotificationId, x.AgencyId },
                        principalTable: "AgencyNotification",
                        principalColumns: new[] { "Id", "AgencyId" });
                    table.ForeignKey(
                        name: "FK_AgencyInvitation_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyInvitation_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyInvitation_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_User_AgencyId",
                table: "User",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_User_Id_AgencyId",
                table: "User",
                columns: new[] { "Id", "AgencyId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_User_AgencyTeam",
                table: "User",
                sql: "[AgencyId] IS NULL OR [TeamId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyInvitation_AgencyId_CreatedAt_Id",
                table: "AgencyInvitation",
                columns: new[] { "AgencyId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyInvitation_CreatedBy",
                table: "AgencyInvitation",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyInvitation_NotificationId_AgencyId",
                table: "AgencyInvitation",
                columns: new[] { "NotificationId", "AgencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyInvitation_PendingUser",
                table: "AgencyInvitation",
                columns: new[] { "UserId", "State" },
                unique: true,
                filter: "[State] = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyInvitation_StagedUser",
                table: "AgencyInvitation",
                column: "UserId",
                unique: true,
                filter: "[State] = 'staged'");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyInvitation_TokenHash",
                table: "AgencyInvitation",
                column: "TokenHash",
                unique: true,
                filter: "[TokenHash] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_User_Agency_AgencyId",
                table: "User",
                column: "AgencyId",
                principalTable: "Agency",
                principalColumn: "Id");
            migrationBuilder.AddForeignKey(name: "FK_AgencyInvitation_User_Agency", table: "AgencyInvitation", columns: new[] {"UserId","AgencyId"}, principalTable: "User", principalColumns: new[] {"Id","AgencyId"}, onDelete: ReferentialAction.NoAction);
            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_User_AgencyBoundary] ON [User] AFTER INSERT,UPDATE AS
                BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE ISNULL(i.AgencyId,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.AgencyId,'00000000-0000-0000-0000-000000000000') OR (d.AgencyId IS NOT NULL AND (i.Email<>d.Email OR i.NormalizedEmail<>d.NormalizedEmail))) THROW 51000,'Agency identity cannot be reassigned.',1;
                IF EXISTS (SELECT 1 FROM inserted i WHERE i.AgencyId IS NOT NULL AND i.State='active' AND (SELECT COUNT(*) FROM [UserRole] ur JOIN [Role] r ON r.Id=ur.RoleId WHERE ur.UserId=i.Id AND r.Scope='agency' AND r.Code IN ('broker-admin','broker-user','broker-readonly'))<>1) THROW 51000,'An active agency user requires one broker role.',1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_UserRole_AgencyBoundary] ON [UserRole] AFTER INSERT,UPDATE,DELETE AS
                BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM [User] u WITH (UPDLOCK,HOLDLOCK) WHERE u.Id IN (SELECT UserId FROM inserted UNION SELECT UserId FROM deleted) AND (
                EXISTS (SELECT 1 FROM [UserRole] ur JOIN [Role] r ON r.Id=ur.RoleId WHERE ur.UserId=u.Id AND ((u.AgencyId IS NULL AND r.Scope<>'internal') OR (u.AgencyId IS NOT NULL AND (r.Scope<>'agency' OR r.Code NOT IN ('broker-admin','broker-user','broker-readonly')))))
                OR (u.AgencyId IS NOT NULL AND (SELECT COUNT(*) FROM [UserRole] ur WHERE ur.UserId=u.Id)>1)
                OR (u.AgencyId IS NOT NULL AND (u.State='active' OR EXISTS(SELECT 1 FROM [AgencyInvitation] ai WHERE ai.UserId=u.Id AND ai.State IN ('staged','pending'))) AND NOT EXISTS(SELECT 1 FROM [UserRole] ur WHERE ur.UserId=u.Id)))) THROW 51000,'Agency and internal role scopes cannot be mixed.',1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_Role_AssignedIdentity] ON [Role] AFTER UPDATE AS
                BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE (i.Scope<>d.Scope OR i.Code<>d.Code) AND (d.Scope='agency' OR i.Scope='agency' OR EXISTS(SELECT 1 FROM [UserRole] ur WHERE ur.RoleId=i.Id))) THROW 51000,'Assigned role identity is immutable.',1;
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_AgencyInvitation_Lifecycle] ON [AgencyInvitation] AFTER INSERT,UPDATE,DELETE AS
                BEGIN SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL) THROW 51000,'Invitation history cannot be deleted.',1;
                IF EXISTS (SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id WHERE d.Id IS NULL AND i.State<>'staged') THROW 51000,'An invitation must start staged.',1;
                IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON i.Id=d.Id WHERE (d.State='staged' AND (i.State NOT IN ('staged','pending','revoked') OR (i.State='revoked' AND i.TokenHash IS NOT NULL))) OR i.UserId<>d.UserId OR i.AgencyId<>d.AgencyId OR d.State IN ('accepted','expired','revoked') OR (d.State='pending' AND (i.TokenHash IS NULL OR i.IssuedAt IS NULL OR i.ExpiresAt IS NULL OR i.NotificationId IS NULL OR i.State NOT IN ('pending','accepted','expired','revoked') OR i.TokenHash<>d.TokenHash OR i.IssuedAt<>d.IssuedAt OR i.ExpiresAt<>d.ExpiresAt OR i.NotificationId<>d.NotificationId))) THROW 51000,'Invitation identity or terminal history is immutable.',1;
                IF EXISTS (SELECT 1 FROM inserted i WHERE (SELECT COUNT(*) FROM [UserRole] ur JOIN [Role] r ON r.Id=ur.RoleId WHERE ur.UserId=i.UserId AND r.Scope='agency' AND r.Code IN ('broker-admin','broker-user','broker-readonly'))<>1) THROW 51000,'Invitation requires one broker role.',1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [TR_User_AgencyBoundary]; DROP TRIGGER [TR_UserRole_AgencyBoundary]; DROP TRIGGER [TR_Role_AssignedIdentity];");
            migrationBuilder.DropForeignKey(
                name: "FK_User_Agency_AgencyId",
                table: "User");

            migrationBuilder.DropTable(
                name: "AgencyInvitation");

            migrationBuilder.DropIndex(
                name: "IX_User_AgencyId",
                table: "User");

            migrationBuilder.DropIndex(
                name: "IX_User_Id_AgencyId",
                table: "User");

            migrationBuilder.DropCheckConstraint(
                name: "CK_User_AgencyTeam",
                table: "User");

            migrationBuilder.DropColumn(
                name: "AgencyId",
                table: "User");
        }
    }
}
