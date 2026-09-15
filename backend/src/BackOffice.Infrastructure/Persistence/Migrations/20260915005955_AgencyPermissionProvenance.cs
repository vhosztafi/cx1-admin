using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgencyPermissionProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgencyPermissionRequest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Permission = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
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
                    table.PrimaryKey("PK_AgencyPermissionRequest", x => x.Id);
                    table.UniqueConstraint("AK_AgencyPermissionRequest_Id_AgencyId_Permission", x => new { x.Id, x.AgencyId, x.Permission });
                    table.CheckConstraint("CK_AgencyPermissionRequest_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_AgencyPermissionRequest_DecidedAt_Utc", "DATEPART(TZOFFSET,[DecidedAt]) = 0");
                    table.CheckConstraint("CK_AgencyPermissionRequest_Decision", "([State]='pending' AND [DecisionBy] IS NULL AND [DecisionReason] IS NULL AND [DecidedAt] IS NULL) OR ([State] IN ('granted','rejected') AND [DecisionBy] IS NOT NULL AND [DecisionBy]<>[RequestedBy] AND [DecisionReason] IS NOT NULL AND LEN(TRIM([DecisionReason]))>0 AND [DecidedAt] IS NOT NULL AND [DecidedAt]>=[CreatedAt])");
                    table.CheckConstraint("CK_AgencyPermissionRequest_Permission", "[Permission] COLLATE Latin1_General_100_BIN2 = 'bordereau-download' AND DATALENGTH([Permission])=DATALENGTH(N'bordereau-download')");
                    table.CheckConstraint("CK_AgencyPermissionRequest_Requester", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[RequestedBy] AND LEN(TRIM([Reason]))>0");
                    table.CheckConstraint("CK_AgencyPermissionRequest_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_AgencyPermissionRequest_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyPermissionRequest_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyPermissionRequest_User_DecisionBy",
                        column: x => x.DecisionBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyPermissionRequest_User_RequestedBy",
                        column: x => x.RequestedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AgencyPermissionGrant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Permission = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    GrantedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevocationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgencyPermissionGrant", x => x.Id);
                    table.CheckConstraint("CK_AgencyPermissionGrant_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_AgencyPermissionGrant_GrantedAt_Utc", "DATEPART(TZOFFSET,[GrantedAt]) = 0");
                    table.CheckConstraint("CK_AgencyPermissionGrant_Provenance", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[GrantedBy] AND [CreatedAt]=[GrantedAt]");
                    table.CheckConstraint("CK_AgencyPermissionGrant_Revocation", "([RevokedAt] IS NULL AND [RevokedBy] IS NULL AND [RevocationReason] IS NULL) OR ([RevokedAt] IS NOT NULL AND [RevokedAt]>=[GrantedAt] AND [RevokedBy] IS NOT NULL AND [RevocationReason] IS NOT NULL AND LEN(TRIM([RevocationReason]))>0)");
                    table.CheckConstraint("CK_AgencyPermissionGrant_RevokedAt_Utc", "DATEPART(TZOFFSET,[RevokedAt]) = 0");
                    table.CheckConstraint("CK_AgencyPermissionGrant_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_AgencyPermissionGrant_AgencyPermissionRequest_RequestId_AgencyId_Permission",
                        columns: x => new { x.RequestId, x.AgencyId, x.Permission },
                        principalTable: "AgencyPermissionRequest",
                        principalColumns: new[] { "Id", "AgencyId", "Permission" });
                    table.ForeignKey(
                        name: "FK_AgencyPermissionGrant_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyPermissionGrant_User_GrantedBy",
                        column: x => x.GrantedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyPermissionGrant_User_RevokedBy",
                        column: x => x.RevokedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyPermissionGrant_AgencyId_Permission",
                table: "AgencyPermissionGrant",
                columns: new[] { "AgencyId", "Permission" },
                unique: true,
                filter: "[RevokedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyPermissionGrant_CreatedBy",
                table: "AgencyPermissionGrant",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyPermissionGrant_GrantedBy",
                table: "AgencyPermissionGrant",
                column: "GrantedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyPermissionGrant_RequestId",
                table: "AgencyPermissionGrant",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgencyPermissionGrant_RequestId_AgencyId_Permission",
                table: "AgencyPermissionGrant",
                columns: new[] { "RequestId", "AgencyId", "Permission" });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyPermissionGrant_RevokedBy",
                table: "AgencyPermissionGrant",
                column: "RevokedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyPermissionRequest_AgencyId_CreatedAt_Id",
                table: "AgencyPermissionRequest",
                columns: new[] { "AgencyId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyPermissionRequest_AgencyId_Permission",
                table: "AgencyPermissionRequest",
                columns: new[] { "AgencyId", "Permission" },
                unique: true,
                filter: "[State] = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyPermissionRequest_CreatedBy",
                table: "AgencyPermissionRequest",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyPermissionRequest_DecisionBy",
                table: "AgencyPermissionRequest",
                column: "DecisionBy");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyPermissionRequest_RequestedBy",
                table: "AgencyPermissionRequest",
                column: "RequestedBy");
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_AgencyPermissionRequest_Lifecycle ON AgencyPermissionRequest AFTER INSERT,UPDATE,DELETE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM deleted d WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.Id=d.Id))
                    THROW 51000,'Permission request history cannot be deleted.',1;
                  IF EXISTS(SELECT 1 FROM deleted) AND (UPDATE(AgencyId) OR UPDATE(Permission) OR UPDATE(RequestedBy) OR UPDATE(Reason) OR UPDATE(CreatedAt) OR UPDATE(CreatedBy))
                    THROW 51000,'Permission request provenance is immutable.',1;
                  IF EXISTS(SELECT 1 FROM deleted WHERE State<>'pending')
                    THROW 51000,'Permission decisions are terminal.',1;
                  IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(SELECT 1 FROM Agency a WITH(HOLDLOCK) WHERE a.Id=i.AgencyId AND a.State='active'))
                    THROW 51000,'Permission requests and decisions require an active agency.',1;
                  IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(SELECT 1 FROM deleted d WHERE d.Id=i.Id) AND
                    (i.State<>'pending' OR NOT EXISTS(
                      SELECT 1 FROM [User] u JOIN UserRole ur ON ur.UserId=u.Id JOIN Role r ON r.Id=ur.RoleId
                      WHERE u.Id=i.RequestedBy AND u.State='active' AND
                        ((u.AgencyId IS NULL AND r.Scope='internal' AND r.Code IN ('agency-admin','system-admin')) OR
                         (u.AgencyId=i.AgencyId AND r.Scope='agency' AND r.Code='broker-admin')))))
                    THROW 51000,'Permission requests require current authorized identity.',1;
                  IF EXISTS(SELECT 1 FROM inserted i WHERE i.State='pending' AND EXISTS(
                    SELECT 1 FROM AgencyPermissionGrant g WITH(UPDLOCK,HOLDLOCK) WHERE g.AgencyId=i.AgencyId AND g.Permission=i.Permission AND g.RevokedAt IS NULL))
                    THROW 51000,'This agency permission is already granted.',1;
                  IF EXISTS(SELECT 1 FROM inserted i WHERE i.State IN ('granted','rejected') AND NOT EXISTS(
                    SELECT 1 FROM [User] u JOIN UserRole ur ON ur.UserId=u.Id JOIN Role r ON r.Id=ur.RoleId
                    WHERE u.Id=i.DecisionBy AND u.State='active' AND u.AgencyId IS NULL AND r.Scope='internal' AND r.Code IN ('agency-admin','system-admin')))
                    THROW 51000,'Permission decisions require current internal administration.',1;
                  INSERT INTO AgencyPermissionGrant (Id,AgencyId,RequestId,Permission,GrantedBy,GrantedAt,CreatedBy,CreatedAt,UpdatedAt)
                    SELECT NEWID(),i.AgencyId,i.Id,i.Permission,i.DecisionBy,i.DecidedAt,i.DecisionBy,i.DecidedAt,i.DecidedAt
                    FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE d.State='pending' AND i.State='granted';
                END
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_AgencyPermissionGrant_Lifecycle ON AgencyPermissionGrant AFTER INSERT,UPDATE,DELETE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM deleted d WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.Id=d.Id))
                    THROW 51000,'Permission grants cannot be deleted.',1;
                  IF EXISTS(SELECT 1 FROM deleted) AND (UPDATE(AgencyId) OR UPDATE(RequestId) OR UPDATE(Permission) OR UPDATE(GrantedBy) OR UPDATE(GrantedAt) OR UPDATE(CreatedAt) OR UPDATE(CreatedBy))
                    THROW 51000,'Permission grant provenance is immutable.',1;
                  IF EXISTS(SELECT 1 FROM deleted WHERE RevokedAt IS NOT NULL)
                    THROW 51000,'Permission revocation is terminal.',1;
                  IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(SELECT 1 FROM deleted d WHERE d.Id=i.Id) AND
                    (i.RevokedAt IS NOT NULL OR NOT EXISTS(SELECT 1 FROM AgencyPermissionRequest r WHERE r.Id=i.RequestId AND r.AgencyId=i.AgencyId AND r.Permission=i.Permission AND r.State='granted' AND r.DecisionBy=i.GrantedBy AND r.DecidedAt=i.GrantedAt)))
                    THROW 51000,'Permission grants require their exact approved request.',1;
                  IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE i.RevokedAt IS NULL OR NOT EXISTS(
                    SELECT 1 FROM [User] u JOIN UserRole ur ON ur.UserId=u.Id JOIN Role r ON r.Id=ur.RoleId
                    WHERE u.Id=i.RevokedBy AND u.State='active' AND u.AgencyId IS NULL AND r.Scope='internal' AND r.Code IN ('agency-admin','system-admin')))
                    THROW 51000,'Permission revocation requires current internal administration.',1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgencyPermissionGrant");

            migrationBuilder.DropTable(
                name: "AgencyPermissionRequest");
        }
    }
}
