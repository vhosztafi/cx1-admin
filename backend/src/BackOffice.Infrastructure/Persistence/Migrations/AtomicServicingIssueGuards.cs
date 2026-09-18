using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

internal static partial class AtomicServicingIssueGuards
{
    internal static void Up(MigrationBuilder migration)
    {
        foreach(var table in new[]{"ServicingIssueDecision","PolicyMidIntent"})
            migration.Sql($"CREATE TRIGGER TR_{table}_AppendOnly ON [{table}] AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51520,'Issued decision and delivery intent are immutable.',1; END;");
        migration.Sql("""
            CREATE TRIGGER TR_ServicingIssueDecision_Source ON ServicingIssueDecision AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingDraft d ON d.Id=i.DraftId JOIN ServicingCycle c ON c.Id=i.CycleId
              JOIN ServicingAcceptance a ON a.Id=i.AcceptanceId JOIN ServicingTermsVersion t ON t.Id=i.TermsVersionId
              JOIN ServicingRatingResult r ON r.Id=i.RatingId JOIN PolicyTerm pt ON pt.Id=i.BaseTermId
              JOIN UserAuthorityGrant g WITH(HOLDLOCK) ON g.Id=i.GrantId JOIN AuthorityVersion av WITH(HOLDLOCK) ON av.Id=i.AuthorityVersionId
              WHERE d.Kind<>'adjustment' OR d.State<>'draft' OR d.CurrentCycleId IS NULL OR d.CurrentCycleId<>c.Id OR d.CurrentRevisionId<>i.RevisionId
                OR c.CurrentRatingId<>r.Id OR c.CurrentTermsVersionId IS NULL OR c.CurrentTermsVersionId<>t.Id OR c.CurrentAcceptanceId IS NULL OR c.CurrentAcceptanceId<>a.Id
                OR c.State<>'rated' OR r.Outcome<>'rated' OR r.ExpiresAt<=i.CreatedAt OR a.RecordedAt>i.CreatedAt
                OR pt.CurrentVersionId<>i.BaseVersionId OR c.BaseVersionId<>i.BaseVersionId OR c.InputHash<>i.InputHash
                OR a.TermsVersionId<>t.Id OR a.TermsHash<>i.TermsHash OR t.TermsHash<>i.TermsHash OR a.AssuranceHash<>i.AssuranceHash
                OR g.RevokedAt IS NOT NULL OR g.EffectiveFrom>i.CreatedAt OR g.EffectiveTo<=i.CreatedAt OR g.EffectiveFrom>i.EffectiveAt OR g.EffectiveTo<pt.EndsAt
                OR av.State<>'published' OR av.ProductVersionId<>c.ProductVersionId OR av.BinderVersionId<>c.BinderVersionId
                OR av.EffectiveFrom>i.CreatedAt OR av.EffectiveTo<=i.CreatedAt OR av.EffectiveFrom>i.EffectiveAt OR av.EffectiveTo<pt.EndsAt
                OR NOT EXISTS(SELECT 1 FROM [User] u WHERE u.Id=i.ActorId AND u.State='active' AND u.AgencyId IS NULL)
                OR NOT EXISTS(SELECT 1 FROM UserRole ur JOIN Role role ON role.Id=ur.RoleId WHERE ur.UserId=i.ActorId AND role.Code IN ('underwriter','senior-underwriter'))
                OR NOT EXISTS(SELECT 1 FROM OPENJSON(c.InputJson,'$.slices') WITH(effectiveAt datetimeoffset '$.effectiveAt') s HAVING MIN(s.effectiveAt)=i.EffectiveAt))
              THROW 51521,'Issue decision must retain exact current accepted terms, base and issuing authority.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_PolicyTransaction_IssueDecision ON PolicyTransaction AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted t JOIN ServicingIssueDecision d ON d.Id=t.ServicingIssueDecisionId
              WHERE t.Kind<>'adjustment' OR t.TermId<>d.BaseTermId OR t.ProcessedAt<>d.CreatedAt OR t.EffectiveAt<>d.EffectiveAt
                OR t.CreatedBy<>d.ActorId OR CONVERT(varbinary(max),t.Reason)<>CONVERT(varbinary(max),d.Reason))
              THROW 51522,'Servicing transaction must retain its immutable issue decision.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_PolicyDocumentRequest_Purpose ON PolicyDocumentRequest AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted d JOIN PolicyTransaction t ON t.Id=d.TransactionId
              WHERE d.Purpose<>CASE WHEN t.Kind='new-business' THEN 'first-issue' ELSE t.Kind END)
              THROW 51523,'Document purpose must identify its issued transaction.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_PolicyMidIntent_Source ON PolicyMidIntent AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN PolicyTransaction t ON t.Id=i.TransactionId JOIN PolicyVersion v ON v.Id=i.VersionId JOIN OutboxWork w ON w.Id=i.WorkId
              WHERE i.Purpose<>t.Kind OR i.PayloadHash<>HASHBYTES('SHA2_256',CONVERT(varchar(max),i.PayloadJson COLLATE Latin1_General_100_BIN2_UTF8))
                OR CONVERT(varbinary(max),w.Payload)<>CONVERT(varbinary(max),i.PayloadJson) OR w.SubjectRecordId<>i.Id OR w.Kind<>'mid-update'
                OR w.OperationKey<>'mid-update/'+LOWER(REPLACE(CONVERT(varchar(36),i.Id),'-',''))
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.PayloadJson,'$.versionId')),'00000000-0000-0000-0000-000000000000')<>v.Id
                OR COALESCE(JSON_VALUE(i.PayloadJson,'$.contentHash'),'')<>LOWER(CONVERT(varchar(64),v.ContentHash,2)))
              THROW 51524,'MID intent must retain exact issued version and durable work.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingDraft_Issued ON ServicingDraft AFTER INSERT,UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE d.State='issued' AND
              (i.State<>d.State OR i.IssuedTransactionId<>d.IssuedTransactionId OR i.CurrentRevisionId<>d.CurrentRevisionId
                OR ISNULL(i.CurrentCycleId,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.CurrentCycleId,'00000000-0000-0000-0000-000000000000')))
              THROW 51525,'Issued drafts cannot be reopened or revised.',1;
            IF EXISTS(SELECT 1 FROM inserted d WHERE d.State='issued' AND NOT EXISTS(
              SELECT 1 FROM PolicyTransaction t JOIN ServicingIssueDecision s ON s.Id=t.ServicingIssueDecisionId
                JOIN IssueFinancialObligation o ON o.TransactionId=t.Id JOIN Journal j ON j.ObligationId=o.Id AND j.PostedAt IS NOT NULL
                JOIN ServicingCycle c ON c.Id=s.CycleId
              WHERE t.Id=d.IssuedTransactionId AND t.ServicingDraftId=d.Id AND t.ServicingRevisionId=d.CurrentRevisionId AND t.ServicingCycleId=d.CurrentCycleId
                AND (SELECT COUNT(*) FROM PolicyVersion v WHERE v.TransactionId=t.Id)=(SELECT COUNT(*) FROM OPENJSON(c.InputJson,'$.slices'))
                AND NOT EXISTS(SELECT 1 FROM PolicyVersion v WHERE v.TransactionId=t.Id AND
                  ((SELECT COUNT(DISTINCT doc.Kind) FROM PolicyDocumentRequest doc WHERE doc.VersionId=v.Id AND doc.Purpose=t.Kind)<>3
                    OR NOT EXISTS(SELECT 1 FROM PolicyMidIntent m WHERE m.VersionId=v.Id AND m.Purpose=t.Kind)))
                AND NOT EXISTS(SELECT 1 FROM ServicingLease l WHERE l.DraftId=d.Id AND l.Active=1)))
              THROW 51525,'Issued draft requires its complete atomic policy, posting, documents and MID intent graph.',1;
            END;
            """);
        AddVersionAndQuoteGuards(migration);
    }
}
