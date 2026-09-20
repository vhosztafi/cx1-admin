using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class CommercialCancellationIssue
{
    private static void ConfigureGuards(MigrationBuilder m,bool forward)
    {
        Patch(m,"TR_CancellationPreview_Source","s.Scope<>'cancellation-review'",
            "s.Scope<>CASE WHEN EXISTS(SELECT 1 FROM Product product WHERE product.Id=t.ProductId AND product.Code='commercial-combined') THEN 'commercial-cancellation-review' ELSE 'cancellation-review' END",forward);
        Patch(m,"TR_PolicyVersion_Cancellation","ISNULL(JSON_VALUE(v.SnapshotJson,'$.snapshotFormat'),'')<>'issued-cancellation-1'",
            "ISNULL(JSON_VALUE(v.SnapshotJson,'$.snapshotFormat'),'')<>CASE WHEN JSON_VALUE(basis.SnapshotJson,'$.productCode')='commercial-combined' THEN 'issued-commercial-cancellation-1' ELSE 'issued-cancellation-1' END",forward);
        Patch(m,"TR_CommercialExposureIssueDecision_Source","v.TransactionKind NOT IN ('new-business','adjustment','renewal')",
            "v.TransactionKind NOT IN ('new-business','adjustment','renewal','cancellation')",forward);
        Patch(m,"TR_ServicingDraft_CancellationIssued",
            "(SELECT COUNT(*) FROM CancellationConsequence c WHERE c.TransactionId=t.Id AND c.VersionId=v.Id AND c.DecisionId=d.Id)=4",
            "(SELECT COUNT(*) FROM CancellationConsequence c WHERE c.TransactionId=t.Id AND c.VersionId=v.Id AND c.DecisionId=d.Id)=CASE WHEN JSON_VALUE(v.SnapshotJson,'$.productCode')='commercial-combined' THEN CASE WHEN EXISTS(SELECT 1 FROM OPENJSON(v.SnapshotJson,'$.cover.sections') WITH(code nvarchar(100)) section WHERE section.code='employers-liability') THEN 3 ELSE 2 END ELSE 4 END",forward);
    }

    // Change only the expected current clauses, preserving all earlier temporal,
    // ownership and finance guards. Refuse an unknown database definition.
    private static void Patch(MigrationBuilder m,string name,string before,string after,bool forward)
    {
        var old=forward?before:after;var next=forward?after:before;
        static string Literal(string text)=>"N'"+text.Replace("'","''")+"'";
        m.Sql($"""
            DECLARE @definition nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID({Literal(name)}));
            DECLARE @old nvarchar(max)={Literal(old)},@new nvarchar(max)={Literal(next)};
            IF @definition IS NULL OR (LEN(@definition)-LEN(REPLACE(@definition,@old,N'')))<>LEN(@old)
              THROW 51960,'Commercial cancellation requires the exact prior guard clause.',1;
            SET @definition=REPLACE(@definition,@old,@new);
            DECLARE @triggerAt int=CHARINDEX('TRIGGER',UPPER(@definition));
            IF @triggerAt<1 THROW 51960,'Prior trigger definition is unavailable.',1;
            SET @definition=STUFF(@definition,1,@triggerAt-1,'CREATE OR ALTER ');
            EXEC sys.sp_executesql @definition;
            """);
    }

    private static void AddReleaseGuards(MigrationBuilder m)
    {
        m.Sql("""
            CREATE TRIGGER TR_CommercialExposureDecision_Cancellation ON CommercialExposureIssueDecision AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId
              WHERE v.TransactionKind='cancellation' AND
                (i.DecisionHash<>HASHBYTES('SHA2_256',CONVERT(varchar(max),i.DecisionJson COLLATE Latin1_General_100_BIN2_UTF8))
                 OR ISNULL(JSON_VALUE(i.DecisionJson,'$.format'),'')<>'commercial-cancellation-exposure-decision-1'
                 OR ISNULL(JSON_VALUE(i.DecisionJson,'$.propertySum'),'')<>'0.00' OR v.LocationsJson<>'[]'
                 OR i.AssessedAt<>v.ProcessedAt OR i.CreatedAt<>v.ProcessedAt OR i.CreatedBy<>v.CreatedBy))
              THROW 51961,'Cancellation release requires an exact immutable zero exposure decision.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId
              WHERE v.TransactionKind='cancellation' AND ((SELECT COUNT(*) FROM OPENJSON(i.DecisionJson))<>22
                OR EXISTS(SELECT 1 FROM OPENJSON(i.DecisionJson) j WHERE j.[key] NOT IN
                  ('format','bookId','policyId','termId','transactionId','versionId','sourceHash','baseExposureVersionId','baseVersionId',
                   'baseSourceHash','binderVersionId','cancellationIssueDecisionId','cancellationPreviewId','cancellationApprovalId',
                   'previewHash','authorityGrantId','authorityVersionId','assessedAt','effectiveAt','termStartsAt','termEndsAt','propertySum'))
                OR EXISTS(SELECT j.[key] FROM OPENJSON(i.DecisionJson) j GROUP BY j.[key] HAVING COUNT(*)<>1)))
              THROW 51961,'Cancellation release decision must have its closed complete shape.',1;
            IF EXISTS(SELECT i.Id,v.BookId,v.PolicyId,v.TermId,v.TransactionId,v.VersionId,v.SourceHash,
                baseExposure.Id,baseExposure.VersionId,baseExposure.SourceHash,baseExposure.BinderVersionId,
                d.Id,d.PreviewId,d.ApprovalId,d.PreviewHash,d.AuthorityGrantId,d.AuthorityVersionId,
                i.AssessedAt,v.EffectiveAt,v.TermStartsAt,v.TermEndsAt
              FROM inserted i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId
                JOIN PolicyTransaction t ON t.Id=v.TransactionId JOIN CancellationIssueDecision d ON d.Id=t.CancellationIssueDecisionId
                JOIN CommercialExposureVersion baseExposure ON baseExposure.VersionId=d.BaseVersionId
              WHERE v.TransactionKind='cancellation'
              EXCEPT SELECT i.Id,j.bookId,j.policyId,j.termId,j.transactionId,j.versionId,TRY_CONVERT(binary(32),j.sourceHash,2),
                j.baseExposureVersionId,j.baseVersionId,TRY_CONVERT(binary(32),j.baseSourceHash,2),j.binderVersionId,
                j.cancellationIssueDecisionId,j.cancellationPreviewId,j.cancellationApprovalId,TRY_CONVERT(binary(32),j.previewHash,2),
                j.authorityGrantId,j.authorityVersionId,j.assessedAt,j.effectiveAt,j.termStartsAt,j.termEndsAt
              FROM inserted i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId CROSS APPLY OPENJSON(i.DecisionJson)
                WITH(bookId uniqueidentifier,policyId uniqueidentifier,termId uniqueidentifier,transactionId uniqueidentifier,versionId uniqueidentifier,
                  sourceHash varchar(64),baseExposureVersionId uniqueidentifier,baseVersionId uniqueidentifier,baseSourceHash varchar(64),binderVersionId uniqueidentifier,
                  cancellationIssueDecisionId uniqueidentifier,cancellationPreviewId uniqueidentifier,cancellationApprovalId uniqueidentifier,previewHash varchar(64),
                  authorityGrantId uniqueidentifier,authorityVersionId uniqueidentifier,assessedAt datetimeoffset,effectiveAt datetimeoffset,termStartsAt datetimeoffset,termEndsAt datetimeoffset) j
              WHERE v.TransactionKind='cancellation')
              THROW 51961,'Cancellation release metadata must match its owned base and reviewed decision.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId
                JOIN PolicyTransaction t ON t.Id=v.TransactionId LEFT JOIN CancellationIssueDecision d ON d.Id=t.CancellationIssueDecisionId
                LEFT JOIN CommercialExposureVersion b ON b.VersionId=d.BaseVersionId
                LEFT JOIN UserAuthorityGrant g ON g.Id=d.AuthorityGrantId
              WHERE v.TransactionKind='cancellation' AND (d.Id IS NULL OR b.Id IS NULL OR b.PolicyId<>v.PolicyId OR b.TermId<>v.TermId
                OR b.BookId<>v.BookId OR b.BinderVersionId<>v.BinderVersionId OR b.TransactionKind='cancellation'
                OR d.ActorId<>i.CreatedBy OR g.Id IS NULL OR g.UserId<>i.CreatedBy OR g.AuthorityVersionId<>d.AuthorityVersionId
                OR g.EffectiveFrom>i.AssessedAt OR g.EffectiveTo<=i.AssessedAt OR g.EffectiveFrom>v.TermStartsAt OR g.EffectiveTo<v.TermEndsAt
                OR (g.RevokedAt IS NOT NULL AND g.RevokedAt<=i.AssessedAt)))
              THROW 51961,'Cancellation release requires current actor authority and exact original book.',1;
            END;
            """);
        m.Sql("""
            CREATE TRIGGER TR_ServicingDraft_CommercialCancellationIssued ON ServicingDraft AFTER INSERT,UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted d JOIN Policy p ON p.Id=d.PolicyId JOIN Product product ON product.Id=p.ProductId
              WHERE d.State='issued' AND d.Kind='cancellation' AND product.Code='commercial-combined' AND NOT EXISTS(
                SELECT 1 FROM PolicyTransaction t JOIN PolicyVersion v ON v.TransactionId=t.Id
                  JOIN CommercialExposureVersion e ON e.VersionId=v.Id
                  JOIN CommercialExposureIssueDecision ed ON ed.ExposureVersionId=e.Id
                WHERE t.Id=d.IssuedTransactionId AND e.TransactionKind='cancellation' AND e.LocationsJson='[]'
                  AND JSON_VALUE(ed.DecisionJson,'$.format')='commercial-cancellation-exposure-decision-1'
                  AND EXISTS(SELECT 1 FROM CancellationConsequence c WHERE c.TransactionId=t.Id AND c.Kind='notice')
                  AND EXISTS(SELECT 1 FROM CancellationConsequence c WHERE c.TransactionId=t.Id AND c.Kind='task-close')
                  AND NOT EXISTS(SELECT 1 FROM CancellationConsequence c WHERE c.TransactionId=t.Id AND c.Kind='mid-removal')
                  AND (SELECT COUNT(*) FROM CancellationConsequence c WHERE c.TransactionId=t.Id AND c.Kind='certificate-withdrawal')=
                    CASE WHEN EXISTS(SELECT 1 FROM OPENJSON(v.SnapshotJson,'$.cover.sections') WITH(code nvarchar(100)) s WHERE s.code='employers-liability') THEN 1 ELSE 0 END))
              THROW 51962,'Commercial cancellation requires atomic zero exposure and product-specific consequences.',1;
            END;
            """);
    }
}
