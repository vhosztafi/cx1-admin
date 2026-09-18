using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

internal static class ServicingAccountingPeriodGuards
{
    internal static void Up(MigrationBuilder migration) => migration.Sql("""
        CREATE TRIGGER TR_AccountingPeriod_Range ON AccountingPeriod AFTER INSERT,UPDATE,DELETE AS
        BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id
            WHERE i.Id IS NULL OR i.StartsOn<>d.StartsOn OR i.EndsOn<>d.EndsOn
              OR i.CreatedAt<>d.CreatedAt OR ISNULL(i.CreatedBy,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.CreatedBy,'00000000-0000-0000-0000-000000000000'))
            THROW 51510,'Accounting period boundaries and identity are immutable.',1;
          IF EXISTS(SELECT 1 FROM inserted i JOIN AccountingPeriod p WITH(UPDLOCK,HOLDLOCK)
            ON p.Id<>i.Id AND p.StartsOn<i.EndsOn AND i.StartsOn<p.EndsOn)
            THROW 51510,'Accounting periods cannot overlap.',1;
        END
        """);
    internal static void Down(MigrationBuilder migration) => migration.Sql("DROP TRIGGER IF EXISTS TR_AccountingPeriod_Range;");
}
