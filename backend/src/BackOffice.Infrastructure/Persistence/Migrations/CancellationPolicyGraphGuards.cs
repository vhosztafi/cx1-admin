using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.RegularExpressions;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class CancellationPolicyGraph
{
    private static string Alter(string sql)=>Regex.Replace(sql,@"^\s*CREATE\s+(TRIGGER|VIEW)","CREATE OR ALTER $1",RegexOptions.IgnoreCase);
    private static string ReplaceOnce(string sql,string from,string to)
    {
        if(sql.Split(from,StringSplitOptions.None).Length!=2)throw new InvalidOperationException("Expected one exact legacy SQL boundary: "+from);
        return sql.Replace(from,to,StringComparison.Ordinal);
    }
    private static void AddGraphGuards(MigrationBuilder migration)
    {
        migration.Sql(ReplaceOnce(Alter(Before_TR_PolicyTransaction_Source),"i.Kind<>'new-business' AND","i.Kind IN ('adjustment','renewal') AND"));
        migration.Sql("""
            CREATE TRIGGER TR_PolicyTransaction_Cancellation ON PolicyTransaction AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted t JOIN CancellationIssueDecision d ON d.Id=t.CancellationIssueDecisionId
              JOIN ServicingDraft draft WITH(UPDLOCK,HOLDLOCK) ON draft.Id=d.DraftId
              JOIN PolicyTerm term WITH(UPDLOCK,HOLDLOCK) ON term.Id=d.BaseTermId
              WHERE t.Kind<>'cancellation' OR t.TermId<>d.BaseTermId OR t.ProcessedAt<>d.CreatedAt OR t.EffectiveAt<>d.EffectiveAt
                OR t.CreatedBy<>d.ActorId OR CONVERT(varbinary(max),t.Reason)<>CONVERT(varbinary(max),d.Reason)
                OR draft.State<>'draft' OR draft.CurrentRevisionId IS NULL OR draft.CurrentRevisionId<>d.RevisionId
                OR term.CurrentVersionId IS NULL OR term.CurrentVersionId<>d.BaseVersionId
                OR t.OperationKey<>'cancellation-issue/'+LOWER(REPLACE(CONVERT(varchar(36),d.DraftId),'-',''))
                OR t.Sequence<>1+COALESCE((SELECT MAX(old.Sequence) FROM PolicyTransaction old WHERE old.TermId=t.TermId AND old.Id<>t.Id),0))
              THROW 51830,'Cancellation transaction requires its current immutable decision and exact original term.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_PolicyVersion_Cancellation ON PolicyVersion AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted v JOIN PolicyTransaction t ON t.Id=v.TransactionId
              JOIN CancellationIssueDecision d ON d.Id=t.CancellationIssueDecisionId JOIN CancellationPreview p ON p.Id=d.PreviewId
              JOIN PolicyVersion basis ON basis.Id=d.BaseVersionId
              WHERE v.SliceOrdinal<>1 OR v.EffectiveAt<>d.EffectiveAt OR v.ProcessedAt<>d.CreatedAt
                OR ISNULL(JSON_VALUE(v.SnapshotJson,'$.snapshotFormat'),'')<>'issued-cancellation-1'
                OR ISNULL(JSON_VALUE(v.SnapshotJson,'$.cancellation.outcome'),'')<>'cancelled'
                OR ISNULL(JSON_VALUE(v.SnapshotJson,'$.cancellation.reasonCode'),'')<>p.ReasonCode
                OR ISNULL(JSON_VALUE(v.SnapshotJson,'$.cancellation.ruleVersion'),'')<>p.RuleVersion
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.cancellation.effectiveAt')) IS NULL
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.cancellation.effectiveAt'))<>d.EffectiveAt
                OR ISNULL(JSON_VALUE(v.SnapshotJson,'$.provenance.inputHash'),'')<>LOWER(CONVERT(varchar(64),d.PreviewHash,2))
                OR ISNULL(JSON_VALUE(v.SnapshotJson,'$.provenance.source'),'')<>'backoffice'
                OR ISNULL(TRY_CONVERT(int,JSON_VALUE(v.SnapshotJson,'$.provenance.sliceOrdinal')),0)<>1
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.provenance.effectiveAt')) IS NULL
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.provenance.effectiveAt'))<>d.EffectiveAt
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.provenance.processedAt')) IS NULL
                OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.provenance.processedAt'))<>t.ProcessedAt
                OR EXISTS(SELECT 1 FROM (VALUES('sourceQuoteId',t.SourceQuoteId),('cancellationIssueDecisionId',d.Id),
                  ('cancellationApprovalId',d.ApprovalId),('cancellationPreviewId',d.PreviewId),('baseVersionId',d.BaseVersionId),
                  ('revisionId',d.RevisionId),('transactionId',t.Id)) expected(Name,Id)
                  WHERE ISNULL(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.provenance.'+expected.Name)),'00000000-0000-0000-0000-000000000000')<>expected.Id)
                OR EXISTS(SELECT 1 FROM (VALUES('insured'),('risk'),('cover'),('premium'),('term')) field(Name)
                  WHERE JSON_QUERY(v.SnapshotJson,'$.'+field.Name) IS NULL
                    OR CONVERT(varbinary(max),JSON_QUERY(v.SnapshotJson,'$.'+field.Name))<>CONVERT(varbinary(max),JSON_QUERY(basis.SnapshotJson,'$.'+field.Name)))
                OR v.Sequence<>1+COALESCE((SELECT MAX(old.Sequence) FROM PolicyVersion old WHERE old.TermId=v.TermId AND old.Id<>v.Id),0))
              THROW 51831,'Cancellation version must preserve historical declarations and exact cancellation outcome.',1;
            END;
            """);
        AddCancellationPostingGuards(migration);
        AddCancellationClosureGuards(migration);
    }
}
