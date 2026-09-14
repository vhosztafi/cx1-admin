using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgencyTermsProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_AgencyStateRequest_Id_AgencyId",
                table: "AgencyStateRequest",
                columns: new[] { "Id", "AgencyId" });

            migrationBuilder.CreateTable(
                name: "AgencyTermsRequest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseVersion = table.Column<byte[]>(type: "binary(8)", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ProposedSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_AgencyTermsRequest", x => x.Id);
                    table.UniqueConstraint("AK_AgencyTermsRequest_Id_AgencyId", x => new { x.Id, x.AgencyId });
                    table.CheckConstraint("CK_AgencyTermsRequest_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_AgencyTermsRequest_DecidedAt_Utc", "DATEPART(TZOFFSET,[DecidedAt]) = 0");
                    table.CheckConstraint("CK_AgencyTermsRequest_Decision", "([State]='pending' AND [DecisionBy] IS NULL AND [DecisionReason] IS NULL AND [DecidedAt] IS NULL) OR ([State] IN ('applied','rejected') AND [DecisionBy] IS NOT NULL AND [DecisionBy]<>[RequestedBy] AND [DecisionReason] IS NOT NULL AND LEN(TRIM([DecisionReason]))>0 AND [DecidedAt] IS NOT NULL AND [DecidedAt]>=[CreatedAt]) OR ([State]='stale' AND [DecisionBy] IS NULL AND [DecisionReason] IS NOT NULL AND LEN(TRIM([DecisionReason]))>0 AND [DecidedAt] IS NOT NULL AND [DecidedAt]>=[CreatedAt])");
                    table.CheckConstraint("CK_AgencyTermsRequest_Fingerprint", "LEN([ProposedInputFingerprint])=64 AND [ProposedInputFingerprint] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
                    table.CheckConstraint("CK_AgencyTermsRequest_ProposedSnapshot_Json", "ISJSON([ProposedSnapshot]) = 1");
                    table.CheckConstraint("CK_AgencyTermsRequest_Requester", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[RequestedBy] AND LEN(TRIM([RequestReason]))>0");
                    table.CheckConstraint("CK_AgencyTermsRequest_SnapshotBounds", "DATALENGTH([ProposedSnapshot])<=131072 AND LEFT(LTRIM([ProposedSnapshot]),1)='{' AND JSON_VALUE([ProposedSnapshot],'$.effectiveFrom') IS NOT NULL AND TRY_CONVERT(date,JSON_VALUE([ProposedSnapshot],'$.effectiveFrom'),23) IS NOT NULL AND TRY_CONVERT(date,JSON_VALUE([ProposedSnapshot],'$.effectiveFrom'),23)=[EffectiveFrom]");
                    table.CheckConstraint("CK_AgencyTermsRequest_State", "[State] IN ('pending','applied','rejected','stale')");
                    table.CheckConstraint("CK_AgencyTermsRequest_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_AgencyTermsRequest_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyTermsRequest_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyTermsRequest_User_DecisionBy",
                        column: x => x.DecisionBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyTermsRequest_User_RequestedBy",
                        column: x => x.RequestedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AgencyTermsVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ApprovedStateRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedTermsRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Snapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgencyTermsVersion", x => x.Id);
                    table.CheckConstraint("CK_AgencyTermsVersion_Approval", "([Version]=1 AND [ApprovedStateRequestId] IS NOT NULL AND [ApprovedTermsRequestId] IS NULL) OR ([Version]>1 AND [ApprovedStateRequestId] IS NULL AND [ApprovedTermsRequestId] IS NOT NULL)");
                    table.CheckConstraint("CK_AgencyTermsVersion_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_AgencyTermsVersion_Snapshot_Json", "ISJSON([Snapshot]) = 1");
                    table.CheckConstraint("CK_AgencyTermsVersion_SnapshotBounds", "DATALENGTH([Snapshot])<=131072 AND LEFT(LTRIM([Snapshot]),1)='{' AND JSON_VALUE([Snapshot],'$.effectiveFrom') IS NOT NULL AND TRY_CONVERT(date,JSON_VALUE([Snapshot],'$.effectiveFrom'),23) IS NOT NULL AND TRY_CONVERT(date,JSON_VALUE([Snapshot],'$.effectiveFrom'),23)=[EffectiveFrom]");
                    table.ForeignKey(
                        name: "FK_AgencyTermsVersion_AgencyStateRequest_ApprovedStateRequestId_AgencyId",
                        columns: x => new { x.ApprovedStateRequestId, x.AgencyId },
                        principalTable: "AgencyStateRequest",
                        principalColumns: new[] { "Id", "AgencyId" });
                    table.ForeignKey(
                        name: "FK_AgencyTermsVersion_AgencyTermsRequest_ApprovedTermsRequestId_AgencyId",
                        columns: x => new { x.ApprovedTermsRequestId, x.AgencyId },
                        principalTable: "AgencyTermsRequest",
                        principalColumns: new[] { "Id", "AgencyId" });
                    table.ForeignKey(
                        name: "FK_AgencyTermsVersion_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyTermsVersion_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyTermsRequest_AgencyId",
                table: "AgencyTermsRequest",
                column: "AgencyId",
                unique: true,
                filter: "[State] = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyTermsRequest_AgencyId_CreatedAt_Id",
                table: "AgencyTermsRequest",
                columns: new[] { "AgencyId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyTermsRequest_CreatedBy",
                table: "AgencyTermsRequest",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyTermsRequest_DecisionBy",
                table: "AgencyTermsRequest",
                column: "DecisionBy");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyTermsRequest_RequestedBy",
                table: "AgencyTermsRequest",
                column: "RequestedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyTermsVersion_AgencyId_EffectiveFrom",
                table: "AgencyTermsVersion",
                columns: new[] { "AgencyId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgencyTermsVersion_AgencyId_Version",
                table: "AgencyTermsVersion",
                columns: new[] { "AgencyId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgencyTermsVersion_ApprovedStateRequestId",
                table: "AgencyTermsVersion",
                column: "ApprovedStateRequestId",
                unique: true,
                filter: "[ApprovedStateRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyTermsVersion_ApprovedStateRequestId_AgencyId",
                table: "AgencyTermsVersion",
                columns: new[] { "ApprovedStateRequestId", "AgencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyTermsVersion_ApprovedTermsRequestId",
                table: "AgencyTermsVersion",
                column: "ApprovedTermsRequestId",
                unique: true,
                filter: "[ApprovedTermsRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyTermsVersion_ApprovedTermsRequestId_AgencyId",
                table: "AgencyTermsVersion",
                columns: new[] { "ApprovedTermsRequestId", "AgencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyTermsVersion_CreatedBy",
                table: "AgencyTermsVersion",
                column: "CreatedBy");
            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_AgencyTermsRequest_Lifecycle] ON [AgencyTermsRequest] AFTER INSERT,UPDATE,DELETE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL) THROW 51000,'Agency proposal history cannot be deleted.',1;
                  IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id WHERE d.Id IS NULL AND i.State<>'pending') THROW 51000,'Agency proposals must start pending.',1;
                  IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE d.State<>'pending' OR EXISTS(
                    SELECT i.AgencyId,i.EffectiveFrom,i.ProposedSnapshot COLLATE Latin1_General_100_BIN2,i.BaseVersion,i.ProposedInputFingerprint COLLATE Latin1_General_100_BIN2,i.RequestedBy,i.RequestReason COLLATE Latin1_General_100_BIN2,i.CreatedAt,i.CreatedBy
                    EXCEPT SELECT d.AgencyId,d.EffectiveFrom,d.ProposedSnapshot COLLATE Latin1_General_100_BIN2,d.BaseVersion,d.ProposedInputFingerprint COLLATE Latin1_General_100_BIN2,d.RequestedBy,d.RequestReason COLLATE Latin1_General_100_BIN2,d.CreatedAt,d.CreatedBy)) THROW 51000,'Agency proposal content and terminal decisions are immutable.',1;
                  IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id WHERE d.Id IS NULL AND NOT EXISTS(
                    SELECT 1 FROM [User] u JOIN UserRole ur ON ur.UserId=u.Id JOIN Role r ON r.Id=ur.RoleId WHERE u.Id=i.RequestedBy AND u.State='active' AND u.AgencyId IS NULL AND r.Scope='internal' AND r.Code IN ('agency-admin','system-admin'))) THROW 51000,'Agency proposals require current internal administration.',1;
                  IF EXISTS(SELECT 1 FROM inserted i WHERE i.State IN ('applied','rejected') AND NOT EXISTS(
                    SELECT 1 FROM [User] u JOIN UserRole ur ON ur.UserId=u.Id JOIN Role r ON r.Id=ur.RoleId WHERE u.Id=i.DecisionBy AND u.State='active' AND u.AgencyId IS NULL AND r.Scope='internal' AND r.Code IN ('agency-admin','system-admin'))) THROW 51000,'Agency decisions require current internal administration.',1;
                END
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_AgencyTermsVersion_Immutable] ON [AgencyTermsVersion] AFTER INSERT,UPDATE,DELETE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM deleted) THROW 51000,'Published agency terms cannot be changed or deleted.',1;
                  DECLARE @locked int;
                  SELECT @locked=COUNT(*) FROM Agency a WITH(UPDLOCK,HOLDLOCK) JOIN inserted i ON i.AgencyId=a.Id;
                  IF EXISTS(SELECT 1 FROM inserted i WHERE i.ApprovedStateRequestId IS NOT NULL AND NOT EXISTS(
                    SELECT 1 FROM AgencyStateRequest r WHERE r.Id=i.ApprovedStateRequestId AND r.AgencyId=i.AgencyId AND r.Kind='activation' AND r.State='applied' AND r.DecisionBy=i.CreatedBy AND r.DecidedAt<=i.CreatedAt)) THROW 51000,'Initial terms require their applied activation decision.',1;
                  IF EXISTS(SELECT 1 FROM inserted i WHERE i.ApprovedTermsRequestId IS NOT NULL AND NOT EXISTS(
                    SELECT 1 FROM AgencyTermsRequest r WHERE r.Id=i.ApprovedTermsRequestId AND r.AgencyId=i.AgencyId AND r.State='applied' AND r.DecisionBy=i.CreatedBy AND r.DecidedAt<=i.CreatedAt AND r.EffectiveFrom=i.EffectiveFrom AND r.ProposedSnapshot COLLATE Latin1_General_100_BIN2=i.Snapshot COLLATE Latin1_General_100_BIN2)) THROW 51000,'Published terms must equal the applied proposal.',1;
                  IF EXISTS(SELECT 1 FROM inserted i WHERE EXISTS(SELECT 1 FROM AgencyTermsVersion v WHERE v.AgencyId=i.AgencyId AND v.Version>i.Version AND NOT EXISTS(SELECT 1 FROM inserted n WHERE n.Id=v.Id)) OR (i.Version>1 AND NOT EXISTS(SELECT 1 FROM AgencyTermsVersion p WHERE p.AgencyId=i.AgencyId AND p.Version=i.Version-1 AND p.EffectiveFrom<i.EffectiveFrom))) THROW 51000,'Terms versions must append sequentially with later effective dates.',1;
                END
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgencyTermsVersion");

            migrationBuilder.DropTable(
                name: "AgencyTermsRequest");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AgencyStateRequest_Id_AgencyId",
                table: "AgencyStateRequest");
        }
    }
}
