using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuoteMatchOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientId",
                table: "QuoteRevision",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "RelationshipId",
                table: "QuoteRevision",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "QuoteId",
                table: "MatchSubmission",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Quote_Id_AgencyId",
                table: "Quote",
                columns: new[] { "Id", "AgencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRevision_RelationshipId_ClientId_AgencyId",
                table: "QuoteRevision",
                columns: new[] { "RelationshipId", "ClientId", "AgencyId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchSubmission_QuoteId",
                table: "MatchSubmission",
                column: "QuoteId",
                unique: true,
                filter: "[QuoteId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MatchSubmission_QuoteId_AgencyId",
                table: "MatchSubmission",
                columns: new[] { "QuoteId", "AgencyId" });

            migrationBuilder.AddForeignKey(
                name: "FK_MatchSubmission_Quote_QuoteId_AgencyId",
                table: "MatchSubmission",
                columns: new[] { "QuoteId", "AgencyId" },
                principalTable: "Quote",
                principalColumns: new[] { "Id", "AgencyId" });

            // Before this migration quotes cannot be reassociated. Backfill their
            // unchanged ownership inside the migration transaction, then restore
            // the append-only guard before enabling any runtime reassociation.
            migrationBuilder.Sql("DISABLE TRIGGER [TR_QuoteRevision_AppendOnly] ON [QuoteRevision]; UPDATE r SET ClientId=q.ClientId,RelationshipId=q.RelationshipId FROM QuoteRevision r JOIN Quote q ON q.Id=r.QuoteId; ENABLE TRIGGER [TR_QuoteRevision_AppendOnly] ON [QuoteRevision];");

            migrationBuilder.AddForeignKey(
                name: "FK_QuoteRevision_ClientAgencyRelationship_RelationshipId_ClientId_AgencyId",
                table: "QuoteRevision",
                columns: new[] { "RelationshipId", "ClientId", "AgencyId" },
                principalTable: "ClientAgencyRelationship",
                principalColumns: new[] { "Id", "ClientId", "AgencyId" });
            migrationBuilder.Sql("CREATE TRIGGER [TR_QuoteRevision_Ownership] ON [QuoteRevision] AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted i JOIN Quote q ON q.Id=i.QuoteId WHERE i.ClientId<>q.ClientId OR i.RelationshipId<>q.RelationshipId) THROW 51083, 'Revision ownership must match the current quote at save time.', 1; END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [TR_QuoteRevision_Ownership];");
            migrationBuilder.DropForeignKey(
                name: "FK_MatchSubmission_Quote_QuoteId_AgencyId",
                table: "MatchSubmission");

            migrationBuilder.DropForeignKey(
                name: "FK_QuoteRevision_ClientAgencyRelationship_RelationshipId_ClientId_AgencyId",
                table: "QuoteRevision");

            migrationBuilder.DropIndex(
                name: "IX_QuoteRevision_RelationshipId_ClientId_AgencyId",
                table: "QuoteRevision");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Quote_Id_AgencyId",
                table: "Quote");

            migrationBuilder.DropIndex(
                name: "IX_MatchSubmission_QuoteId",
                table: "MatchSubmission");

            migrationBuilder.DropIndex(
                name: "IX_MatchSubmission_QuoteId_AgencyId",
                table: "MatchSubmission");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "QuoteRevision");

            migrationBuilder.DropColumn(
                name: "RelationshipId",
                table: "QuoteRevision");

            migrationBuilder.DropColumn(
                name: "QuoteId",
                table: "MatchSubmission");
        }
    }
}
