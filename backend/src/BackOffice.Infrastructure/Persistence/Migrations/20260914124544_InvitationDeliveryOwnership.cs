using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvitationDeliveryOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InvitationId",
                table: "AgencyNotification",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AgencyInvitation_Id_AgencyId",
                table: "AgencyInvitation",
                columns: new[] { "Id", "AgencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AgencyNotification_InvitationId_AgencyId",
                table: "AgencyNotification",
                columns: new[] { "InvitationId", "AgencyId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AgencyNotification_Invitation",
                table: "AgencyNotification",
                sql: "([Purpose]='agency-invitation' AND [InvitationId] IS NOT NULL) OR ([Purpose]='agency-activated' AND [InvitationId] IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_AgencyNotification_AgencyInvitation_InvitationId_AgencyId",
                table: "AgencyNotification",
                columns: new[] { "InvitationId", "AgencyId" },
                principalTable: "AgencyInvitation",
                principalColumns: new[] { "Id", "AgencyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgencyNotification_AgencyInvitation_InvitationId_AgencyId",
                table: "AgencyNotification");

            migrationBuilder.DropIndex(
                name: "IX_AgencyNotification_InvitationId_AgencyId",
                table: "AgencyNotification");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AgencyNotification_Invitation",
                table: "AgencyNotification");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AgencyInvitation_Id_AgencyId",
                table: "AgencyInvitation");

            migrationBuilder.DropColumn(
                name: "InvitationId",
                table: "AgencyNotification");
        }
    }
}
