using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperationalDocumentTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TemplateVersion_Kind",
                table: "TemplateVersion");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TemplateVersion_Kind",
                table: "TemplateVersion",
                sql: "[Kind] IN ('quote-terms','servicing-terms','renewal-invitation','policy-schedule','policy-certificate','policy-statement','endorsement','cancellation-notice')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [TemplateVersion] WHERE [Kind] IN ('endorsement','cancellation-notice')) THROW 51000, 'Operational document templates exist; downgrade would invalidate retained templates.', 1;");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TemplateVersion_Kind",
                table: "TemplateVersion");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TemplateVersion_Kind",
                table: "TemplateVersion",
                sql: "[Kind] IN ('quote-terms','servicing-terms','renewal-invitation','policy-schedule','policy-certificate','policy-statement')");
        }
    }
}
