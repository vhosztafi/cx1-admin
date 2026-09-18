using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class ServicingCapacityResponseEvidence
{
    private static void AddResponseEvidenceGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_ServicingEvidenceAssociation_CapacitySource ON ServicingEvidenceAssociation AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i WHERE i.CapacitySubmissionId IS NOT NULL AND NOT EXISTS(
                SELECT 1 FROM ServicingCapacitySubmission s
                JOIN ServicingCapacityCase k WITH(UPDLOCK,HOLDLOCK) ON k.Id=s.CaseId
                WHERE s.Id=i.CapacitySubmissionId AND k.CurrentSubmissionId=s.Id AND k.State NOT IN ('draft','superseded')
                    AND i.CreatedAt>=s.SubmittedAt))
                THROW 51460,'Capacity response proof requires the exact current active submission.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingEvidenceAssociation_CapacityHistory ON ServicingEvidenceAssociation AFTER UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT Id,CapacitySubmissionId FROM deleted EXCEPT SELECT Id,CapacitySubmissionId FROM inserted)
                THROW 51461,'Capacity response proof cannot move between submissions.',1;
            END;
            """);
    }
}
