using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductValidityGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_ProductVersion_PublishedValidity]
                ON [ProductVersion] AFTER INSERT, UPDATE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1 FROM inserted i
                        JOIN [ProductVersion] p WITH (UPDLOCK, HOLDLOCK)
                          ON p.ProductId = i.ProductId AND p.Id <> i.Id
                        WHERE i.State = 'published' AND p.State = 'published'
                          AND (p.EffectiveTo IS NULL OR i.EffectiveFrom < p.EffectiveTo)
                          AND (i.EffectiveTo IS NULL OR p.EffectiveFrom < i.EffectiveTo)
                    ) THROW 51001, 'Published product validity ranges overlap.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [TR_ProductVersion_PublishedValidity];");
        }
    }
}
