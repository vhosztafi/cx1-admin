using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgencyFollowUpObligations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgencyFollowUp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActivationRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Purpose = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgencyFollowUp", x => x.Id);
                    table.CheckConstraint("CK_AgencyFollowUp_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_AgencyFollowUp_Creator", "[CreatedBy] IS NOT NULL");
                    table.CheckConstraint("CK_AgencyFollowUp_Source", "([Purpose]='pi-expiry' AND [EvidenceId] IS NOT NULL AND [ActivationRequestId] IS NULL) OR ([Purpose]='quarter-review' AND [EvidenceId] IS NULL AND [ActivationRequestId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_AgencyFollowUp_AgencyEvidence_EvidenceId_AgencyId",
                        columns: x => new { x.EvidenceId, x.AgencyId },
                        principalTable: "AgencyEvidence",
                        principalColumns: new[] { "Id", "AgencyId" });
                    table.ForeignKey(
                        name: "FK_AgencyFollowUp_AgencyStateRequest_ActivationRequestId_AgencyId",
                        columns: x => new { x.ActivationRequestId, x.AgencyId },
                        principalTable: "AgencyStateRequest",
                        principalColumns: new[] { "Id", "AgencyId" });
                    table.ForeignKey(
                        name: "FK_AgencyFollowUp_Agency_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agency",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AgencyFollowUp_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyFollowUp_ActivationRequestId_AgencyId",
                table: "AgencyFollowUp",
                columns: new[] { "ActivationRequestId", "AgencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyFollowUp_AgencyId_ActivationRequestId_Purpose_DueOn",
                table: "AgencyFollowUp",
                columns: new[] { "AgencyId", "ActivationRequestId", "Purpose", "DueOn" },
                unique: true,
                filter: "[ActivationRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyFollowUp_AgencyId_EvidenceId_Purpose_DueOn",
                table: "AgencyFollowUp",
                columns: new[] { "AgencyId", "EvidenceId", "Purpose", "DueOn" },
                unique: true,
                filter: "[EvidenceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyFollowUp_CreatedBy",
                table: "AgencyFollowUp",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AgencyFollowUp_DueOn_AgencyId_Id",
                table: "AgencyFollowUp",
                columns: new[] { "DueOn", "AgencyId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyFollowUp_EvidenceId_AgencyId",
                table: "AgencyFollowUp",
                columns: new[] { "EvidenceId", "AgencyId" });
            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_AgencyFollowUp_Immutable] ON [AgencyFollowUp] AFTER INSERT,UPDATE,DELETE AS
                BEGIN
                  SET NOCOUNT ON;
                  IF EXISTS(SELECT 1 FROM deleted) THROW 51000,'Agency follow-up provenance cannot be changed or deleted.',1;
                  IF EXISTS(SELECT 1 FROM inserted i WHERE NOT EXISTS(
                    SELECT 1 FROM [User] u JOIN UserRole ur ON ur.UserId=u.Id JOIN Role r ON r.Id=ur.RoleId
                    WHERE u.Id=i.CreatedBy AND u.State='active' AND u.AgencyId IS NULL AND r.Scope='internal' AND r.Code IN ('agency-admin','system-admin')))
                    THROW 51000,'Agency follow-ups require current internal administration.',1;
                  IF EXISTS(SELECT 1 FROM inserted i WHERE i.Purpose='pi-expiry' AND NOT EXISTS(
                    SELECT 1 FROM AgencyEvidence e WHERE e.Id=i.EvidenceId AND e.AgencyId=i.AgencyId AND e.Kind='professional-indemnity' AND e.State='verified' AND e.ExpiresOn=i.DueOn AND e.VerifiedAt<=i.CreatedAt))
                    THROW 51000,'PI follow-ups require verified matching expiry evidence.',1;
                  IF EXISTS(SELECT 1 FROM inserted i WHERE i.Purpose='quarter-review' AND NOT EXISTS(
                    SELECT 1 FROM AgencyStateRequest r WHERE r.Id=i.ActivationRequestId AND r.AgencyId=i.AgencyId AND r.Kind='activation' AND r.State='applied' AND r.DecisionBy=i.CreatedBy AND r.DecidedAt<=i.CreatedAt AND i.DueOn>=CONVERT(date,r.DecidedAt)))
                    THROW 51000,'Quarter reviews require their applied activation decision.',1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgencyFollowUp");
        }
    }
}
