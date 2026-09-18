using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

internal static class ServicingTermsEvidenceGuards
{
    internal static void Up(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_ServicingEvidenceAssociation_TermsSource ON ServicingEvidenceAssociation AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i WHERE i.TermsVersionId IS NOT NULL AND NOT EXISTS(
              SELECT 1 FROM ServicingTermsVersion t JOIN ServicingCycle c WITH(UPDLOCK,HOLDLOCK) ON c.Id=t.CycleId
              WHERE t.Id=i.TermsVersionId AND c.CurrentTermsVersionId=t.Id AND i.CreatedAt>=t.PreparedAt))
              THROW 51503,'Terms evidence requires the exact current prepared contract.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingEvidenceAssociation_TermsHistory ON ServicingEvidenceAssociation AFTER UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT Id,TermsVersionId FROM deleted EXCEPT SELECT Id,TermsVersionId FROM inserted)
              THROW 51504,'Terms evidence cannot move between contracts.',1;
            END;
            """);
    }

    internal static void Down(MigrationBuilder migration)
    {
        migration.Sql("DROP TRIGGER IF EXISTS TR_ServicingEvidenceAssociation_TermsSource;");
        migration.Sql("DROP TRIGGER IF EXISTS TR_ServicingEvidenceAssociation_TermsHistory;");
    }
}
