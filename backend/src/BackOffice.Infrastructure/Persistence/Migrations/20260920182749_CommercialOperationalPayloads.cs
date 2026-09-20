using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommercialOperationalPayloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AddDocumentGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM PolicyDocumentRequest WHERE JSON_VALUE(PayloadJson,'$.commercial.format')='commercial-document-1') THROW 51971,'Commercial document payload history cannot be downgraded.',1; DROP TRIGGER TR_PolicyDocumentRequest_CommercialPayload;");
        }
    }
}
