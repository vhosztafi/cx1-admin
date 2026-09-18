namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class RenewalIssueGraph
{
    // Frozen prior definitions keep historical migrations immutable.
    private const string PreviousDecisionSource = """
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
        """;
    private const string PreviousTransactionDecision = """
            CREATE TRIGGER TR_PolicyTransaction_IssueDecision ON PolicyTransaction AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted t JOIN ServicingIssueDecision d ON d.Id=t.ServicingIssueDecisionId
              WHERE t.Kind<>'adjustment' OR t.TermId<>d.BaseTermId OR t.ProcessedAt<>d.CreatedAt OR t.EffectiveAt<>d.EffectiveAt
                OR t.CreatedBy<>d.ActorId OR CONVERT(varbinary(max),t.Reason)<>CONVERT(varbinary(max),d.Reason))
              THROW 51522,'Servicing transaction must retain its immutable issue decision.',1;
            END;
        """;
    private const string PreviousTransactionSource = """
            ALTER TRIGGER TR_PolicyTransaction_Source ON PolicyTransaction AFTER INSERT AS BEGIN
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
                OR i.Kind<>d.Kind OR i.Kind<>'adjustment' OR c.BaseTermId<>t.Id OR c.ProductVersionId<>t.ProductVersionId
                OR d.State<>'draft' OR d.CurrentCycleId IS NULL OR d.CurrentCycleId<>c.Id OR d.CurrentRevisionId<>i.ServicingRevisionId
                OR c.State<>'rated' OR c.CurrentRatingId<>r.Id OR c.CurrentAcceptanceId IS NULL OR c.CurrentAcceptanceId<>a.Id
                OR r.Outcome<>'rated' OR r.ExpiresAt<=i.ProcessedAt OR a.RecordedAt>i.ProcessedAt
                OR t.CurrentVersionId<>c.BaseVersionId
                OR i.EffectiveAt<t.StartsAt OR i.EffectiveAt>=t.EndsAt
                OR NOT EXISTS(SELECT 1 FROM OPENJSON(c.InputJson,'$.slices') WITH(effectiveAt datetimeoffset '$.effectiveAt') s HAVING MIN(s.effectiveAt)=i.EffectiveAt)))
              THROW 51511,'Servicing transaction requires the exact current accepted decision and original policy term.',1;
            END;
        """;
    private const string PreviousPostingMovement = """
            CREATE VIEW ServicingExpectedPostingMovement AS
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
              WHERE o.Purpose='adjustment'
            UNION ALL
            SELECT o.TransactionId,v.Code,1,v.Amount,t.EffectiveAt,pt.EndsAt
              FROM IssueFinancialObligation o JOIN PolicyTransaction t ON t.Id=o.TransactionId JOIN PolicyTerm pt ON pt.Id=o.TermId
              CROSS APPLY(VALUES('fee',o.Fee),('fee-share',o.FeeShare)) v(Code,Amount)
              WHERE o.Purpose='adjustment';
        """;
    private const string PreviousObligationSource = """
            ALTER TRIGGER TR_IssueFinancialObligation_Source ON IssueFinancialObligation AFTER INSERT AS BEGIN
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
                OR (t.Kind<>'new-business' AND (t.Kind<>'adjustment' OR sc.Id IS NULL OR sr.Id IS NULL OR o.Premium<>sr.Premium OR o.Tax<>sr.Tax OR o.Fee<>sr.Fee OR o.Commission<>sr.BrokerCommission))
                OR CONVERT(varbinary(max),o.TermsSnapshotJson)<>CONVERT(varbinary(max),a.Snapshot)
                OR COALESCE(JSON_VALUE(a.Snapshot,'$.settlement.premiumCollection'),'') NOT IN ('agency','mga')
                OR COALESCE(JSON_VALUE(a.Snapshot,'$.settlement.commissionSettlement'),'') NOT IN ('net-remittance','separate-payment')
                OR o.DebtorKind<>CASE WHEN JSON_VALUE(a.Snapshot,'$.settlement.premiumCollection')='agency' THEN 'agency' ELSE 'relationship' END
                OR o.Settlement<>CASE WHEN JSON_VALUE(a.Snapshot,'$.settlement.premiumCollection')='agency' AND JSON_VALUE(a.Snapshot,'$.settlement.commissionSettlement')='net-remittance' THEN 'net-remittance' ELSE 'separate-payment' END
                OR o.FeeShare<>ROUND(o.Fee*CASE WHEN JSON_VALUE(a.Snapshot,'$.commercialTerms.feeSharing')='agreed-split' THEN COALESCE(TRY_CONVERT(decimal(9,4),JSON_VALUE(a.Snapshot,'$.commercialTerms.feeShareBasisPoints')),-1) ELSE 0 END/10000,2))
              THROW 51188,'Issue amounts, debtor and settlement must retain their actual rating and agreed terms.',1;
            END;
        """;
}
