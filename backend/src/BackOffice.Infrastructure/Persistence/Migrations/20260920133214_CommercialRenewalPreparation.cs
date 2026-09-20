using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommercialRenewalPreparation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CommercialRevisionId",
                table: "RenewalExperienceVersion",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommercialSubjectsJson",
                table: "RenewalExperienceVersion",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RenewalExperienceVersion_CommercialRevisionId_DraftId",
                table: "RenewalExperienceVersion",
                columns: new[] { "CommercialRevisionId", "DraftId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_RenewalExperienceVersion_CommercialSubjects",
                table: "RenewalExperienceVersion",
                sql: "([CommercialRevisionId] IS NULL AND [CommercialSubjectsJson] IS NULL) OR ([CommercialRevisionId] IS NOT NULL AND [CommercialSubjectsJson] IS NOT NULL AND ISJSON([CommercialSubjectsJson],OBJECT)=1 AND DATALENGTH([CommercialSubjectsJson])<=262144)");

            migrationBuilder.AddForeignKey(
                name: "FK_RenewalExperienceVersion_ServicingRevision_CommercialRevisionId_DraftId",
                table: "RenewalExperienceVersion",
                columns: new[] { "CommercialRevisionId", "DraftId" },
                principalTable: "ServicingRevision",
                principalColumns: new[] { "Id", "DraftId" });
            SetCommercialRenewalGuards(migrationBuilder,true);
            AddCommercialExperienceGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS(SELECT 1 FROM ServicingDraft d JOIN Policy p ON p.Id=d.PolicyId JOIN Product product ON product.Id=p.ProductId
                    WHERE d.Kind='renewal' AND product.Code='commercial-combined')
                  OR EXISTS(SELECT 1 FROM RenewalLapseEvent l JOIN PolicyTerm t ON t.Id=l.TermId JOIN Product product ON product.Id=t.ProductId
                    WHERE product.Code='commercial-combined')
                  THROW 51953,'Retained commercial renewal history cannot be downgraded.',1;
                """);
            migrationBuilder.Sql("DROP TRIGGER TR_ServicingCycle_CommercialRenewalExperience; DROP TRIGGER TR_RenewalExperienceReview_Commercial; DROP TRIGGER TR_RenewalExperienceVersion_Commercial;");
            SetCommercialRenewalGuards(migrationBuilder,false);
            migrationBuilder.DropForeignKey(
                name: "FK_RenewalExperienceVersion_ServicingRevision_CommercialRevisionId_DraftId",
                table: "RenewalExperienceVersion");

            migrationBuilder.DropIndex(
                name: "IX_RenewalExperienceVersion_CommercialRevisionId_DraftId",
                table: "RenewalExperienceVersion");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RenewalExperienceVersion_CommercialSubjects",
                table: "RenewalExperienceVersion");

            migrationBuilder.DropColumn(
                name: "CommercialRevisionId",
                table: "RenewalExperienceVersion");

            migrationBuilder.DropColumn(
                name: "CommercialSubjectsJson",
                table: "RenewalExperienceVersion");
        }
    }
}
