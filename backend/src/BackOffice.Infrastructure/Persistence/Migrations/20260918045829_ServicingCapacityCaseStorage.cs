using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServicingCapacityCaseStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServicingCapacityCase",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RatingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferralId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BinderVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RaisedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicingCapacityCase", x => x.Id);
                    table.UniqueConstraint("AK_ServicingCapacityCase_Id_CycleId_DraftId_RevisionId_RatingId", x => new { x.Id, x.CycleId, x.DraftId, x.RevisionId, x.RatingId });
                    table.CheckConstraint("CK_ServicingCapacityCase_Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[RaisedBy]");
                    table.CheckConstraint("CK_ServicingCapacityCase_CreatedAt_Utc", "DATEPART(TZOFFSET,[CreatedAt]) = 0");
                    table.CheckConstraint("CK_ServicingCapacityCase_Identity", "[Id]<>'00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_ServicingCapacityCase_Reason", "LEN(TRIM([Reason]))>=10");
                    table.CheckConstraint("CK_ServicingCapacityCase_State", "[State] IN ('draft','queued','sent','queried','approved','conditional','declined','failed','superseded')");
                    table.CheckConstraint("CK_ServicingCapacityCase_Time", "[UpdatedAt]>=[CreatedAt]");
                    table.CheckConstraint("CK_ServicingCapacityCase_UpdatedAt_Utc", "DATEPART(TZOFFSET,[UpdatedAt]) = 0");
                    table.ForeignKey(
                        name: "FK_ServicingCapacityCase_BinderVersion_BinderVersionId",
                        column: x => x.BinderVersionId,
                        principalTable: "BinderVersion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingCapacityCase_CapacityProvider_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "CapacityProvider",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingCapacityCase_ServicingReferral_ReferralId_CycleId_DraftId_RevisionId_RatingId",
                        columns: x => new { x.ReferralId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId },
                        principalTable: "ServicingReferral",
                        principalColumns: new[] { "Id", "CycleId", "DraftId", "RevisionId", "RatingId" });
                    table.ForeignKey(
                        name: "FK_ServicingCapacityCase_User_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServicingCapacityCase_User_RaisedBy",
                        column: x => x.RaisedBy,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacityCase_BinderVersionId",
                table: "ServicingCapacityCase",
                column: "BinderVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacityCase_CreatedBy",
                table: "ServicingCapacityCase",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacityCase_DraftId_CreatedAt_Id",
                table: "ServicingCapacityCase",
                columns: new[] { "DraftId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacityCase_ProviderId",
                table: "ServicingCapacityCase",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacityCase_RaisedBy",
                table: "ServicingCapacityCase",
                column: "RaisedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacityCase_ReferralId",
                table: "ServicingCapacityCase",
                column: "ReferralId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCapacityCase_ReferralId_CycleId_DraftId_RevisionId_RatingId",
                table: "ServicingCapacityCase",
                columns: new[] { "ReferralId", "CycleId", "DraftId", "RevisionId", "RatingId" });
            AddGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveGuards(migrationBuilder);
            migrationBuilder.DropTable(
                name: "ServicingCapacityCase");
        }
    }
}
