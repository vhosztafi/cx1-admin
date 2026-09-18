using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenewalInvitationTemplates : Migration
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
                sql: "[Kind] IN ('quote-terms','servicing-terms','renewal-invitation','policy-schedule','policy-certificate','policy-statement')");
            AddInvitationGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RemoveInvitationGuards(migrationBuilder);
            migrationBuilder.DropCheckConstraint(
                name: "CK_TemplateVersion_Kind",
                table: "TemplateVersion");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TemplateVersion_Kind",
                table: "TemplateVersion",
                sql: "[Kind] IN ('quote-terms','servicing-terms','policy-schedule','policy-certificate','policy-statement')");
        }
    }
}
