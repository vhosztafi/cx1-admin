namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class CancellationPolicyGraph
{
    private const string Before_ServicingExpectedPostingMovement = """
        CREATE   VIEW ServicingExpectedPostingMovement AS
            SELECT o.TransactionId,v.Code,1 AS Ordinal,v.Amount,t.StartsAt AS CoverageStartsAt,t.EndsAt AS CoverageEndsAt
              FROM IssueFinancialObligation o JOIN PolicyTerm t ON t.Id=o.TermId
              CROSS APPLY(VALUES('premium',o.Premium),('tax',o.Tax),('fee',o.Fee),('commission',o.Commission),('fee-share',o.FeeShare)) v(Code,Amount)
              WHERE o.Purpose='first-issue'
            UNION ALL
            SELECT o.TransactionId,v.Code,CONVERT(int,s.[key])+1,v.Amount,x.EffectiveAt,x.CoverageEndsAt
              FROM IssueFinancialObligation o JOIN PolicyTransaction t ON t.Id=o.TransactionId
              JOIN ServicingRatingResult r ON r.Id=t.ServicingRatingId
              CROSS APPLY OPENJSON(r.ResultJson,'$.rating.slices') s
              CROSS APPLY OPENJSON(s.value) WITH(EffectiveAt datetimeoffset '$.effectiveAt',CoverageEndsAt datetimeoffset '$.coverageEndsAt',
                Premium decimal(15,2) '$.premium',Tax decimal(15,2) '$.tax',Commission decimal(15,2) '$.brokerCommission') x
              CROSS APPLY(VALUES('premium',x.Premium),('tax',x.Tax),('commission',x.Commission)) v(Code,Amount)
              WHERE o.Purpose IN ('adjustment','renewal')
            UNION ALL
            SELECT o.TransactionId,v.Code,1,v.Amount,t.EffectiveAt,pt.EndsAt
              FROM IssueFinancialObligation o JOIN PolicyTransaction t ON t.Id=o.TransactionId JOIN PolicyTerm pt ON pt.Id=o.TermId
              CROSS APPLY(VALUES('fee',o.Fee),('fee-share',o.FeeShare)) v(Code,Amount)
              WHERE o.Purpose IN ('adjustment','renewal');
        """;
    private const string Before_TR_IssueFinancialComponent_Source = """
        CREATE TRIGGER TR_IssueFinancialComponent_Source ON IssueFinancialComponent AFTER INSERT AS BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT 1 FROM inserted c JOIN IssueFinancialObligation o WITH(UPDLOCK,HOLDLOCK) ON o.Id=c.ObligationId
          JOIN PolicyTerm pt ON pt.Id=o.TermId WHERE c.OriginalComponentId IS NOT NULL
            OR c.CoverageStartsAt<pt.StartsAt OR c.CoverageEndsAt>pt.EndsAt
            OR NOT EXISTS(SELECT 1 FROM ServicingExpectedPostingMovement e WHERE e.TransactionId=c.TransactionId AND e.Code=c.Code AND e.Ordinal=c.Ordinal
              AND e.Amount=c.Amount AND e.CoverageStartsAt=c.CoverageStartsAt AND e.CoverageEndsAt=c.CoverageEndsAt)
            OR EXISTS(SELECT 1 FROM Journal j WITH(UPDLOCK,HOLDLOCK) WHERE j.ObligationId=o.Id AND j.PostedAt IS NOT NULL))
          THROW 51189,'Component must retain its exact approved movement and interval before posting.',1;
        END;
        """;
    private const string Before_TR_IssueFinancialObligation_Source = """
        CREATE TRIGGER TR_IssueFinancialObligation_Source ON IssueFinancialObligation AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted o JOIN Policy p ON p.Id=o.PolicyId JOIN PolicyTransaction t ON t.Id=o.TransactionId
              LEFT JOIN UnderwritingCycle qc ON qc.Id=t.CycleId LEFT JOIN QuoteRatingResult qr ON qr.Id=t.RatingId
              LEFT JOIN ServicingCycle sc ON sc.Id=t.ServicingCycleId LEFT JOIN ServicingRatingResult sr ON sr.Id=t.ServicingRatingId
              LEFT JOIN BinderVersion b ON b.Id=CASE WHEN t.Kind='new-business' THEN qc.BinderVersionId ELSE sc.BinderVersionId END
              LEFT JOIN AgencyTermsVersion a ON a.Id=CASE WHEN t.Kind='new-business' THEN qc.AgencyTermsVersionId ELSE sc.AgencyTermsVersionId END
              WHERE o.AgencyId<>p.AgencyId OR o.ClientId<>p.ClientId OR o.RelationshipId<>p.RelationshipId OR b.Id IS NULL OR a.Id IS NULL
                OR o.ProviderId<>b.ProviderId OR o.AgencyTermsVersionId<>a.Id
                OR o.Purpose<>CASE WHEN t.Kind='new-business' THEN 'first-issue' ELSE t.Kind END
                OR (t.Kind='new-business' AND (qc.Id IS NULL OR qr.Id IS NULL OR o.Premium<>qr.TermPremium OR o.Tax<>qr.Tax OR o.Fee<>qr.Fee OR o.Commission<>qr.BrokerCommission))
                OR (t.Kind<>'new-business' AND (t.Kind NOT IN ('adjustment','renewal') OR sc.Id IS NULL OR sr.Id IS NULL OR o.Premium<>sr.Premium OR o.Tax<>sr.Tax OR o.Fee<>sr.Fee OR o.Commission<>sr.BrokerCommission))
                OR CONVERT(varbinary(max),o.TermsSnapshotJson)<>CONVERT(varbinary(max),a.Snapshot)
                OR COALESCE(JSON_VALUE(a.Snapshot,'$.settlement.premiumCollection'),'') NOT IN ('agency','mga')
                OR COALESCE(JSON_VALUE(a.Snapshot,'$.settlement.commissionSettlement'),'') NOT IN ('net-remittance','separate-payment')
                OR o.DebtorKind<>CASE WHEN JSON_VALUE(a.Snapshot,'$.settlement.premiumCollection')='agency' THEN 'agency' ELSE 'relationship' END
                OR o.Settlement<>CASE WHEN JSON_VALUE(a.Snapshot,'$.settlement.premiumCollection')='agency' AND JSON_VALUE(a.Snapshot,'$.settlement.commissionSettlement')='net-remittance' THEN 'net-remittance' ELSE 'separate-payment' END
                OR o.FeeShare<>ROUND(o.Fee*CASE WHEN JSON_VALUE(a.Snapshot,'$.commercialTerms.feeSharing')='agreed-split' THEN COALESCE(TRY_CONVERT(decimal(9,4),JSON_VALUE(a.Snapshot,'$.commercialTerms.feeShareBasisPoints')),-1) ELSE 0 END/10000,2))
              THROW 51188,'Issue amounts, debtor and settlement must retain their actual rating and agreed terms.',1;
            END;
        """;
    private const string Before_TR_PolicyTransaction_Source = """
        CREATE TRIGGER TR_PolicyTransaction_Source ON PolicyTransaction AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN PolicyTerm t ON t.Id=i.TermId
              LEFT JOIN QuoteAcceptance a ON a.Id=i.AcceptanceId LEFT JOIN UnderwritingCycle c ON c.Id=i.CycleId
              WHERE i.Kind='new-business' AND (a.Id IS NULL OR c.Id IS NULL OR a.RatingId<>i.RatingId OR c.QuoteRevisionId<>i.QuoteRevisionId
                OR c.ProductVersionId<>t.ProductVersionId OR c.StartsAt<>t.StartsAt OR c.EndsAt<>t.EndsAt OR i.EffectiveAt<>t.StartsAt))
              THROW 51183,'First issue must retain exact accepted rating, revision and term.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN PolicyTerm t ON t.Id=i.TermId
              LEFT JOIN ServicingCycle c ON c.Id=i.ServicingCycleId LEFT JOIN ServicingDraft d ON d.Id=i.ServicingDraftId
              LEFT JOIN ServicingAcceptance a ON a.Id=i.ServicingAcceptanceId LEFT JOIN ServicingRatingResult r ON r.Id=i.ServicingRatingId
              WHERE i.Kind<>'new-business' AND (c.Id IS NULL OR d.Id IS NULL OR a.Id IS NULL OR r.Id IS NULL
                OR i.Kind<>d.Kind OR i.Kind NOT IN ('adjustment','renewal') OR (i.Kind='adjustment' AND c.BaseTermId<>t.Id) OR c.ProductVersionId<>t.ProductVersionId
                OR d.State<>'draft' OR d.CurrentCycleId IS NULL OR d.CurrentCycleId<>c.Id OR d.CurrentRevisionId<>i.ServicingRevisionId
                OR c.State<>'rated' OR c.CurrentRatingId<>r.Id OR c.CurrentAcceptanceId IS NULL OR c.CurrentAcceptanceId<>a.Id
                OR r.Outcome<>'rated' OR r.ExpiresAt<=i.ProcessedAt OR a.RecordedAt>i.ProcessedAt
                OR (i.Kind='adjustment' AND t.CurrentVersionId<>c.BaseVersionId)
                OR i.EffectiveAt<t.StartsAt OR i.EffectiveAt>=t.EndsAt
                OR NOT EXISTS(SELECT 1 FROM OPENJSON(c.InputJson,'$.slices') WITH(effectiveAt datetimeoffset '$.effectiveAt') s HAVING MIN(s.effectiveAt)=i.EffectiveAt)))
              THROW 51511,'Servicing transaction requires the exact current accepted decision and original policy term.',1;
            END;
        """;
    private const string Before_TR_PolicyVersion_Source = """
        CREATE TRIGGER TR_PolicyVersion_Source ON PolicyVersion AFTER INSERT AS BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT 1 FROM inserted v JOIN PolicyTransaction t ON t.Id=v.TransactionId JOIN Policy p ON p.Id=v.PolicyId JOIN PolicyTerm pt ON pt.Id=v.TermId JOIN Product product ON product.Id=p.ProductId
          WHERE (t.Kind='new-business' AND v.EffectiveAt<>t.EffectiveAt) OR v.ProcessedAt<>t.ProcessedAt OR v.ContentHash<>HASHBYTES('SHA2_256',CONVERT(varchar(max),v.SnapshotJson COLLATE Latin1_General_100_BIN2_UTF8))
            OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.productVersionId')),'00000000-0000-0000-0000-000000000000')<>pt.ProductVersionId
            OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.insured.clientId')),'00000000-0000-0000-0000-000000000000')<>p.ClientId
            OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(v.SnapshotJson,'$.insured.clientAgencyRelationshipId')),'00000000-0000-0000-0000-000000000000')<>p.RelationshipId
            OR COALESCE(JSON_VALUE(v.SnapshotJson,'$.productCode'),'')<>product.Code
            OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.term.startsAt')) IS NULL OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.term.startsAt'))<>pt.StartsAt
            OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.term.endsAt')) IS NULL OR TRY_CONVERT(datetimeoffset,JSON_VALUE(v.SnapshotJson,'$.term.endsAt'))<>pt.EndsAt)
          THROW 51184,'Issued snapshot must agree with source, coverage and content hash.',1;
        END;
        """;
    private const string Before_TR_ServicingDraft_Issued = """
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
        """;
}
