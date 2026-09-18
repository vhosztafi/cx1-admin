using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenewalRatingSourcePins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RenewalExperienceReviewId",
                table: "ServicingCycle",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RenewalExperienceVersionId",
                table: "ServicingCycle",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RenewalPreparationVersionId",
                table: "ServicingCycle",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_RenewalExperienceReview_Id_ExperienceVersionId_DraftId",
                table: "RenewalExperienceReview",
                columns: new[] { "Id", "ExperienceVersionId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCycle_RenewalExperienceReviewId_RenewalExperienceVersionId_DraftId",
                table: "ServicingCycle",
                columns: new[] { "RenewalExperienceReviewId", "RenewalExperienceVersionId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCycle_RenewalExperienceVersionId_DraftId",
                table: "ServicingCycle",
                columns: new[] { "RenewalExperienceVersionId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicingCycle_RenewalPreparationVersionId_DraftId",
                table: "ServicingCycle",
                columns: new[] { "RenewalPreparationVersionId", "DraftId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServicingCycle_RenewalExperienceOwner",
                table: "ServicingCycle",
                sql: "[RenewalExperienceReviewId] IS NULL OR [RenewalExperienceVersionId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_ServicingCycle_RenewalExperienceReview_RenewalExperienceReviewId_RenewalExperienceVersionId_DraftId",
                table: "ServicingCycle",
                columns: new[] { "RenewalExperienceReviewId", "RenewalExperienceVersionId", "DraftId" },
                principalTable: "RenewalExperienceReview",
                principalColumns: new[] { "Id", "ExperienceVersionId", "DraftId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ServicingCycle_RenewalExperienceVersion_RenewalExperienceVersionId_DraftId",
                table: "ServicingCycle",
                columns: new[] { "RenewalExperienceVersionId", "DraftId" },
                principalTable: "RenewalExperienceVersion",
                principalColumns: new[] { "Id", "DraftId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ServicingCycle_RenewalPreparationVersion_RenewalPreparationVersionId_DraftId",
                table: "ServicingCycle",
                columns: new[] { "RenewalPreparationVersionId", "DraftId" },
                principalTable: "RenewalPreparationVersion",
                principalColumns: new[] { "Id", "DraftId" });
            AddRenewalRatingGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveRenewalRatingGuards(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_ServicingCycle_RenewalExperienceReview_RenewalExperienceReviewId_RenewalExperienceVersionId_DraftId",
                table: "ServicingCycle");

            migrationBuilder.DropForeignKey(
                name: "FK_ServicingCycle_RenewalExperienceVersion_RenewalExperienceVersionId_DraftId",
                table: "ServicingCycle");

            migrationBuilder.DropForeignKey(
                name: "FK_ServicingCycle_RenewalPreparationVersion_RenewalPreparationVersionId_DraftId",
                table: "ServicingCycle");

            migrationBuilder.DropIndex(
                name: "IX_ServicingCycle_RenewalExperienceReviewId_RenewalExperienceVersionId_DraftId",
                table: "ServicingCycle");

            migrationBuilder.DropIndex(
                name: "IX_ServicingCycle_RenewalExperienceVersionId_DraftId",
                table: "ServicingCycle");

            migrationBuilder.DropIndex(
                name: "IX_ServicingCycle_RenewalPreparationVersionId_DraftId",
                table: "ServicingCycle");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServicingCycle_RenewalExperienceOwner",
                table: "ServicingCycle");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_RenewalExperienceReview_Id_ExperienceVersionId_DraftId",
                table: "RenewalExperienceReview");

            migrationBuilder.DropColumn(
                name: "RenewalExperienceReviewId",
                table: "ServicingCycle");

            migrationBuilder.DropColumn(
                name: "RenewalExperienceVersionId",
                table: "ServicingCycle");

            migrationBuilder.DropColumn(
                name: "RenewalPreparationVersionId",
                table: "ServicingCycle");
        }
    }
}
