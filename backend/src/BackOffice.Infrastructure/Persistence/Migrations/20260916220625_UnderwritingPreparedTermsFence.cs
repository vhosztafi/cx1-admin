using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnderwritingPreparedTermsFence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_UnderwritingEvidenceAssociation_PreparedTermsOwner",
                table: "UnderwritingEvidenceAssociation",
                sql: "[TermsVersionId] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_UnderwritingEvidenceAssociation_PreparedTermsOwner",
                table: "UnderwritingEvidenceAssociation");
        }
    }
}
