using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class RenewalIssueGraph
{
    private static void AddRenewalIssueGuards(MigrationBuilder migration)
    {
        migration.Sql(PreviousDecisionSource.Replace("CREATE TRIGGER","CREATE OR ALTER TRIGGER",StringComparison.Ordinal)
            .Replace("d.Kind<>'adjustment'","d.Kind NOT IN ('adjustment','renewal')",StringComparison.Ordinal)
            .Replace("pt.CurrentVersionId<>i.BaseVersionId","(d.Kind='adjustment' AND pt.CurrentVersionId<>i.BaseVersionId)",StringComparison.Ordinal)
            .Replace("g.EffectiveTo<pt.EndsAt","g.EffectiveTo<CASE WHEN d.Kind='renewal' THEN TRY_CONVERT(datetimeoffset,JSON_VALUE(c.InputJson,'$.term.endsAt')) ELSE pt.EndsAt END",StringComparison.Ordinal)
            .Replace("av.EffectiveTo<pt.EndsAt","av.EffectiveTo<CASE WHEN d.Kind='renewal' THEN TRY_CONVERT(datetimeoffset,JSON_VALUE(c.InputJson,'$.term.endsAt')) ELSE pt.EndsAt END",StringComparison.Ordinal));
        migration.Sql(PreviousTransactionDecision.Replace("CREATE TRIGGER","CREATE OR ALTER TRIGGER",StringComparison.Ordinal)
            .Replace("t.Kind<>'adjustment' OR t.TermId<>d.BaseTermId","t.Kind NOT IN ('adjustment','renewal') OR (t.Kind='adjustment' AND t.TermId<>d.BaseTermId) OR (t.Kind='renewal' AND t.TermId=d.BaseTermId)",StringComparison.Ordinal));
        migration.Sql(PreviousTransactionSource.Replace("i.Kind<>'adjustment' OR c.BaseTermId<>t.Id","i.Kind NOT IN ('adjustment','renewal') OR (i.Kind='adjustment' AND c.BaseTermId<>t.Id)",StringComparison.Ordinal)
            .Replace("t.CurrentVersionId<>c.BaseVersionId","(i.Kind='adjustment' AND t.CurrentVersionId<>c.BaseVersionId)",StringComparison.Ordinal));
        migration.Sql(PreviousPostingMovement.Replace("CREATE VIEW","CREATE OR ALTER VIEW",StringComparison.Ordinal)
            .Replace("o.Purpose='adjustment'","o.Purpose IN ('adjustment','renewal')",StringComparison.Ordinal));
        migration.Sql(PreviousObligationSource.Replace("t.Kind<>'adjustment'","t.Kind NOT IN ('adjustment','renewal')",StringComparison.Ordinal));
        migration.Sql("""
            CREATE TRIGGER TR_ServicingIssueDecision_Renewal ON ServicingIssueDecision AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingDraft d ON d.Id=i.DraftId JOIN ServicingCycle c ON c.Id=i.CycleId
              JOIN PolicyTerm old ON old.Id=i.BaseTermId LEFT JOIN RenewalPreparationVersion p ON p.Id=c.RenewalPreparationVersionId
              WHERE d.Kind='renewal' AND (p.Id IS NULL OR p.BaseVersionId<>i.BaseVersionId OR p.StartsAt<>i.EffectiveAt OR p.StartsAt<>old.EndsAt
                OR i.CreatedAt>p.StartsAt OR p.EndsAt<=p.StartsAt
                OR NOT EXISTS(SELECT 1 FROM PolicyVersion v JOIN PolicyTransaction t ON t.Id=v.TransactionId
                    WHERE v.Id=i.BaseVersionId AND t.Kind IN ('new-business','adjustment','renewal'))
                OR i.BaseVersionId<>(SELECT TOP(1) v.Id FROM PolicyVersion v JOIN PolicyTransaction t ON t.Id=v.TransactionId
                    WHERE v.TermId=old.Id AND v.EffectiveAt<old.EndsAt AND v.ProcessedAt<=i.CreatedAt
                    ORDER BY v.EffectiveAt DESC,t.Sequence DESC,v.SliceOrdinal DESC,v.Id)
                OR EXISTS(SELECT 1 FROM PolicyTerm future WITH(UPDLOCK,HOLDLOCK) WHERE future.PolicyId=i.PolicyId AND future.Id<>old.Id
                    AND future.StartsAt<p.EndsAt AND p.StartsAt<future.EndsAt)))
              THROW 51750,'Renewal issue requires the current strict term-end risk and a supported nonoverlapping new term.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_PolicyTransaction_RenewalTerm ON PolicyTransaction AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.ServicingCycleId
              JOIN PolicyTerm target ON target.Id=i.TermId JOIN PolicyTerm old ON old.Id=c.BaseTermId
              LEFT JOIN RenewalPreparationVersion p ON p.Id=c.RenewalPreparationVersionId
              WHERE i.Kind='renewal' AND (p.Id IS NULL OR target.Id=old.Id OR target.StartsAt<>old.EndsAt
                OR target.StartsAt<>p.StartsAt OR target.EndsAt<>p.EndsAt OR target.ProductVersionId<>p.ProductVersionId
                OR CONVERT(varbinary(max),target.LocalTermIntentJson)<>CONVERT(varbinary(max),p.TermIntentJson)
                OR target.Number<>old.Number+1 OR i.Sequence<>1 OR i.ProcessedAt>target.StartsAt
                OR EXISTS(SELECT 1 FROM PolicyTerm other WITH(UPDLOCK,HOLDLOCK) WHERE other.PolicyId=target.PolicyId AND other.Id<>target.Id
                    AND other.StartsAt<target.EndsAt AND target.StartsAt<other.EndsAt)))
              THROW 51751,'Renewal transaction requires its exact next prepared term without overlap or backfilled cover.',1;
            END;
            """);
    }

    private static void RemoveRenewalIssueGuards(MigrationBuilder migration)
    {
        migration.Sql("IF EXISTS(SELECT 1 FROM PolicyTransaction WHERE Kind='renewal') THROW 51752,'Issued renewal history cannot be downgraded.',1;");
        migration.Sql("DROP TRIGGER IF EXISTS TR_ServicingIssueDecision_Renewal; DROP TRIGGER IF EXISTS TR_PolicyTransaction_RenewalTerm;");
        migration.Sql(PreviousDecisionSource.Replace("CREATE TRIGGER","CREATE OR ALTER TRIGGER",StringComparison.Ordinal));
        migration.Sql(PreviousTransactionDecision.Replace("CREATE TRIGGER","CREATE OR ALTER TRIGGER",StringComparison.Ordinal));
        migration.Sql(PreviousTransactionSource);
        migration.Sql(PreviousPostingMovement.Replace("CREATE VIEW","CREATE OR ALTER VIEW",StringComparison.Ordinal));
        migration.Sql(PreviousObligationSource);
    }
}
