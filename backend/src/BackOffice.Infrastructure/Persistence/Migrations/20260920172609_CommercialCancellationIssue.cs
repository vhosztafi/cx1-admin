using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommercialCancellationIssue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ConfigureGuards(migrationBuilder,true);
            AddReleaseGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS(SELECT 1 FROM ServicingDraft d JOIN Policy p ON p.Id=d.PolicyId JOIN Product product ON product.Id=p.ProductId
                  WHERE d.Kind='cancellation' AND product.Code='commercial-combined')
                  THROW 51963,'Retained commercial cancellation history cannot be downgraded.',1;
                DROP TRIGGER TR_ServicingDraft_CommercialCancellationIssued;
                DROP TRIGGER TR_CommercialExposureDecision_Cancellation;
                """);
            ConfigureGuards(migrationBuilder,false);
        }
    }
}
