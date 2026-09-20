using Microsoft.EntityFrameworkCore.Migrations;
namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class CommercialRenewalPreparation
{
    // Exact retained SQL baselines make this additive migration reproducible.
    private static void SetCommercialRenewalGuards(MigrationBuilder migration,bool forward)
    {
        migration.Sql((forward?Replace(Previous0,["='commercial-servicing-rating-input-1'"," IN ('commercial-servicing-rating-input-1','commercial-servicing-rating-input-2')"]):Previous0).Replace("CREATE TRIGGER","CREATE OR ALTER TRIGGER",StringComparison.Ordinal));
        migration.Sql((forward?Replace(Previous1,["THEN 'commercial-servicing-rating-input-1'","THEN CASE WHEN d.Kind='renewal' THEN 'commercial-servicing-rating-input-2' ELSE 'commercial-servicing-rating-input-1' END"]):Previous1).Replace("CREATE TRIGGER","CREATE OR ALTER TRIGGER",StringComparison.Ordinal));
        migration.Sql((forward?Replace(Previous2,["THEN 'commercial-servicing-rating'","THEN CASE WHEN i.RenewalPreparationVersionId IS NULL THEN 'commercial-servicing-rating' ELSE 'commercial-renewal-preparation' END"]):Previous2).Replace("CREATE TRIGGER","CREATE OR ALTER TRIGGER",StringComparison.Ordinal));
        migration.Sql((forward?Replace(Previous3,["d.Kind<>'adjustment' OR i.RenewalPreparationVersionId IS NOT NULL","d.Kind NOT IN ('adjustment','renewal') OR (d.Kind='adjustment' AND i.RenewalPreparationVersionId IS NOT NULL) OR (d.Kind='renewal' AND i.RenewalPreparationVersionId IS NULL)","OR COALESCE(TRY_CONVERT(decimal(19,2),JSON_VALUE(i.InputJson,'$.fee')),-1)<>COALESCE(TRY_CONVERT(decimal(19,2),JSON_VALUE(r.DefinitionJson,'$.adjustmentFee')),-2)","OR (d.Kind='adjustment' AND COALESCE(TRY_CONVERT(decimal(19,2),JSON_VALUE(i.InputJson,'$.fee')),-1)<>COALESCE(TRY_CONVERT(decimal(19,2),JSON_VALUE(r.DefinitionJson,'$.adjustmentFee')),-2))"]):Previous3).Replace("CREATE TRIGGER","CREATE OR ALTER TRIGGER",StringComparison.Ordinal));
        migration.Sql((forward?Replace(Previous4,["<>'commercial-servicing-rating-input-1'"," NOT IN ('commercial-servicing-rating-input-1','commercial-servicing-rating-input-2')"]):Previous4).Replace("CREATE TRIGGER","CREATE OR ALTER TRIGGER",StringComparison.Ordinal));
        migration.Sql((forward?Replace(Previous5,["<>'commercial-servicing-rating-input-1'"," NOT IN ('commercial-servicing-rating-input-1','commercial-servicing-rating-input-2')","='commercial-servicing-rating-input-1'"," IN ('commercial-servicing-rating-input-1','commercial-servicing-rating-input-2')"]):Previous5).Replace("CREATE TRIGGER","CREATE OR ALTER TRIGGER",StringComparison.Ordinal));
        migration.Sql((forward?Replace(Previous6,["tr.Kind='adjustment'","tr.Kind IN ('adjustment','renewal')"]):Previous6).Replace("CREATE TRIGGER","CREATE OR ALTER TRIGGER",StringComparison.Ordinal));
        migration.Sql((forward?Replace(Previous7,["v.TransactionKind NOT IN ('new-business','adjustment')","v.TransactionKind NOT IN ('new-business','adjustment','renewal')","WHERE v.TransactionKind='adjustment'","WHERE v.TransactionKind IN ('adjustment','renewal')"]):Previous7).Replace("CREATE TRIGGER","CREATE OR ALTER TRIGGER",StringComparison.Ordinal));
        migration.Sql((forward?Replace(Previous8,["s.Scope<>'renewal-preparation'","s.Scope<>CASE WHEN EXISTS(SELECT 1 FROM Product product WHERE product.Id=i.ProductId AND product.Code='commercial-combined') THEN 'commercial-renewal-preparation' ELSE 'renewal-preparation' END"]):Previous8).Replace("CREATE TRIGGER","CREATE OR ALTER TRIGGER",StringComparison.Ordinal));
        migration.Sql((forward?Replace(Previous9,["s.Scope<>'renewal-preparation'","s.Scope<>CASE WHEN EXISTS(SELECT 1 FROM Product product WHERE product.Id=t.ProductId AND product.Code='commercial-combined') THEN 'commercial-renewal-preparation' ELSE 'renewal-preparation' END"]):Previous9).Replace("CREATE TRIGGER","CREATE OR ALTER TRIGGER",StringComparison.Ordinal));
    }
    private static string Replace(string sql,string[] pairs)
    {
        for(var i=0;i<pairs.Length;i+=2)
        {
            if(!sql.Contains(pairs[i],StringComparison.Ordinal))throw new InvalidOperationException("Commercial renewal guard baseline mismatch.");
            sql=sql.Replace(pairs[i],pairs[i+1],StringComparison.Ordinal);
        }
        return sql;
    }

    // Retained baseline: CommercialServicingRatingGuards.cs, TR_ServicingReferral_Source.
    private const string Previous0="""
        CREATE OR ALTER TRIGGER TR_ServicingReferral_Source ON ServicingReferral AFTER INSERT AS BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId JOIN ServicingDraft d ON d.Id=i.DraftId
            JOIN ServicingRatingResult r ON r.Id=i.RatingId WHERE i.State<>'open' OR i.LatestDecisionId IS NOT NULL OR d.State<>'draft'
            OR d.CurrentCycleId IS NULL OR d.CurrentCycleId<>c.Id OR d.CurrentRevisionId IS NULL OR d.CurrentRevisionId<>i.RevisionId
            OR c.State<>'rated' OR c.CurrentRatingId IS NULL OR c.CurrentRatingId<>i.RatingId OR r.Outcome<>'rated'
            OR i.CreatedAt<r.CompletedAt OR i.CreatedAt>=r.ExpiresAt)
          THROW 51340,'Referral requires the current rated draft revision and starts open.',1;
        IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId CROSS APPLY OPENJSON(i.RequiredAuthorityJson,'$.triggers') t
            WHERE COALESCE(JSON_VALUE(t.value,'$.source'),'') NOT IN ('binder','authority','source')
            OR COALESCE(JSON_VALUE(t.value,'$.requirement.ruleCode'),'')<>i.RuleCode OR COALESCE(JSON_VALUE(t.value,'$.requirement.dimension'),'')<>i.Dimension
            OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(t.value,'$.requirement.targetId')),'00000000-0000-0000-0000-000000000000')<>i.TargetKey
            OR (JSON_VALUE(t.value,'$.requirement.targetId') IS NOT NULL AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(t.value,'$.requirement.targetId')) IS NULL)
            OR NOT EXISTS(SELECT 1 FROM OPENJSON(c.InputJson,'$.slices') s
                WHERE TRY_CONVERT(datetimeoffset,JSON_VALUE(s.value,'$.effectiveAt'))=TRY_CONVERT(datetimeoffset,JSON_VALUE(t.value,'$.effectiveAt'))))
          THROW 51341,'Referral triggers must retain the owned rated dates, rule, dimension and target.',1;
        IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId WHERE i.RiskItemId IS NOT NULL AND NOT EXISTS(
            SELECT 1 FROM OPENJSON(c.InputJson,'$.slices') s CROSS APPLY OPENJSON(s.value,CASE WHEN JSON_VALUE(c.InputJson,'$.format')='commercial-servicing-rating-input-1' THEN '$.commercial.pricing.proposal.risk.locations' ELSE '$.input.drivers' END) WITH(Id uniqueidentifier '$.id') driver WHERE driver.Id=i.RiskItemId))
          THROW 51342,'Referral item must exist in the cumulative rated product risk.',1;
        IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId
            CROSS APPLY OPENJSON(i.RequiredAuthorityJson,'$.triggers') t
            WHERE JSON_VALUE(c.InputJson,'$.format')='commercial-servicing-rating-input-1' AND i.RiskItemId IS NOT NULL
              AND NOT EXISTS(SELECT 1 FROM OPENJSON(c.InputJson,'$.slices') s
                CROSS APPLY OPENJSON(s.value,'$.commercial.pricing.proposal.risk.locations') WITH(Id uniqueidentifier '$.id') location
                WHERE location.Id=i.RiskItemId AND TRY_CONVERT(datetimeoffset,JSON_VALUE(s.value,'$.effectiveAt'))=TRY_CONVERT(datetimeoffset,JSON_VALUE(t.value,'$.effectiveAt'))))
          THROW 51941,'Commercial referral target must belong to every retained trigger date.',1;
        END;
        """;

    // Retained baseline: CommercialServicingRatingGuards.cs, TR_ServicingCycle_Source.
    private const string Previous1="""
        CREATE OR ALTER TRIGGER TR_ServicingCycle_Source ON ServicingCycle AFTER INSERT AS BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingDraft d ON d.Id=i.DraftId
        JOIN PolicyVersion b ON b.Id=i.BaseVersionId
        JOIN Policy p ON p.Id=i.PolicyId JOIN PolicyTerm t ON t.Id=i.BaseTermId
        JOIN AgencyTermsVersion a ON a.Id=i.AgencyTermsVersionId JOIN OutboxWork w ON w.Id=i.WorkId
        WHERE d.State<>'draft' OR d.Kind NOT IN ('adjustment','renewal') OR d.CurrentRevisionId IS NULL OR d.CurrentRevisionId<>i.RevisionId
        OR i.State<>'rating-pending' OR a.AgencyId<>p.AgencyId
        OR (d.Kind='adjustment' AND t.ProductVersionId<>i.ProductVersionId)
        OR w.Kind<>'servicing-rating' OR w.SubjectRecordId IS NULL OR w.SubjectRecordId<>i.Id
        OR w.ScenarioVersionId IS NULL OR w.ScenarioVersionId<>i.ScenarioVersionId OR w.OperationKey<>'servicing-rating/'+LOWER(REPLACE(CONVERT(varchar(36),i.Id),'-',''))
        OR i.InputHash<>HASHBYTES('SHA2_256',CONVERT(varchar(max),i.InputJson COLLATE Latin1_General_100_BIN2_UTF8))
        OR COALESCE(JSON_VALUE(i.InputJson,'$.format'),'')<>CASE WHEN JSON_VALUE(b.SnapshotJson,'$.productCode')='commercial-combined' THEN 'commercial-servicing-rating-input-1' WHEN d.Kind='renewal' THEN 'servicing-rating-input-2' ELSE 'servicing-rating-input-1' END)
        THROW 51223,'Cycle requires active owned revision, exact input and durable work provenance.',1;
        END;
        """;

    // Retained baseline: CommercialServicingRatingGuards.cs, TR_ServicingCycle_SettingInsert.
    private const string Previous2="""
        CREATE OR ALTER TRIGGER TR_ServicingCycle_SettingInsert ON ServicingCycle AFTER INSERT AS BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN SettingVersion s ON s.Id=i.ServicingSettingVersionId
        JOIN PolicyVersion b ON b.Id=i.BaseVersionId JOIN ServicingRevision r ON r.Id=i.RevisionId
        WHERE i.ServicingSettingVersionId IS NULL OR s.Id IS NULL OR s.Scope<>CASE WHEN JSON_VALUE(b.SnapshotJson,'$.productCode')='commercial-combined' THEN 'commercial-servicing-rating' WHEN i.RenewalPreparationVersionId IS NULL THEN 'servicing-rating' ELSE 'renewal-preparation' END
        OR s.EffectiveFrom>i.CreatedAt
        OR COALESCE(JSON_VALUE(s.[Values],'$.kind'),'')<>s.Scope
        OR COALESCE(JSON_VALUE(s.[Values],'$.demo'),'')<>'true'
        OR COALESCE(JSON_VALUE(s.[Values],'$.schemaVersion'),'')<>'1'
        OR COALESCE(JSON_VALUE(s.[Values],'$.currency'),'')<>'GBP'
        OR (i.RenewalPreparationVersionId IS NULL AND COALESCE(JSON_VALUE(s.[Values],'$.earningBasis'),'')<>'london-calendar-days')
        OR TRY_CONVERT(decimal(19,2),CASE WHEN i.RenewalPreparationVersionId IS NULL THEN JSON_VALUE(s.[Values],'$.adjustmentFee') ELSE JSON_VALUE(s.[Values],'$.renewalFee') END) IS NULL
        OR TRY_CONVERT(decimal(19,2),CASE WHEN i.RenewalPreparationVersionId IS NULL THEN JSON_VALUE(s.[Values],'$.adjustmentFee') ELSE JSON_VALUE(s.[Values],'$.renewalFee') END) NOT BETWEEN 0 AND 9999999999999.99
        OR TRY_CONVERT(decimal(19,2),JSON_VALUE(i.InputJson,'$.fee')) IS NULL
        OR TRY_CONVERT(decimal(19,2),JSON_VALUE(i.InputJson,'$.fee'))<>TRY_CONVERT(decimal(19,2),CASE WHEN i.RenewalPreparationVersionId IS NULL THEN JSON_VALUE(s.[Values],'$.adjustmentFee') ELSE JSON_VALUE(s.[Values],'$.renewalFee') END)
        OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.InputJson,'$.servicingSettingVersionId')),'00000000-0000-0000-0000-000000000000')<>i.ServicingSettingVersionId
        OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.InputJson,'$.policyId')),'00000000-0000-0000-0000-000000000000')<>i.PolicyId
        OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.InputJson,'$.baseTermId')),'00000000-0000-0000-0000-000000000000')<>i.BaseTermId
        OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.InputJson,'$.requestedBy')),'00000000-0000-0000-0000-000000000000')<>i.RequestedBy
        OR TRY_CONVERT(datetimeoffset,JSON_VALUE(i.InputJson,'$.requestedAt')) IS NULL
        OR TRY_CONVERT(datetimeoffset,JSON_VALUE(i.InputJson,'$.requestedAt'))<>i.CreatedAt
        OR COALESCE(JSON_VALUE(i.InputJson,'$.baseContentHash'),'')<>LOWER(CONVERT(varchar(64),b.ContentHash,2))
        OR COALESCE(JSON_VALUE(i.InputJson,'$.revisionContentHash'),'')<>LOWER(CONVERT(varchar(64),r.ContentHash,2)))
        THROW 51240,'Rating requires its exact setting, fee, source hashes and request clock.',1;
        END;
        """;

    // Retained baseline: CommercialServicingRatingGuards.cs, TR_ServicingCycle_CommercialSource.
    private const string Previous3="""
        CREATE OR ALTER TRIGGER TR_ServicingCycle_CommercialSource ON ServicingCycle AFTER INSERT AS BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT 1 FROM inserted i JOIN PolicyVersion b ON b.Id=i.BaseVersionId
          JOIN ServicingDraft d ON d.Id=i.DraftId JOIN RatingRuleVersion r ON r.Id=i.RatingRuleVersionId
          WHERE JSON_VALUE(b.SnapshotJson,'$.productCode')='commercial-combined' AND
          (d.Kind<>'adjustment' OR i.RenewalPreparationVersionId IS NOT NULL
           OR COALESCE(JSON_VALUE(i.InputJson,'$.ratingDefinition.productCode'),'')<>'commercial-combined'
           OR COALESCE(JSON_VALUE(r.DefinitionJson,'$.productCode'),'')<>'commercial-combined'
           OR COALESCE(TRY_CONVERT(decimal(19,2),JSON_VALUE(i.InputJson,'$.fee')),-1)<>COALESCE(TRY_CONVERT(decimal(19,2),JSON_VALUE(r.DefinitionJson,'$.adjustmentFee')),-2)
           OR JSON_QUERY(i.InputJson,'$.slices') IS NULL OR (SELECT COUNT(*) FROM OPENJSON(i.InputJson,'$.slices')) NOT BETWEEN 1 AND 100
           OR EXISTS(SELECT 1 FROM OPENJSON(i.InputJson,'$.slices') s
               WHERE JSON_QUERY(s.value,'$.input') IS NOT NULL OR JSON_QUERY(s.value,'$.commercial') IS NULL
                 OR COALESCE(JSON_VALUE(s.value,'$.commercial.pricing.proposal.productCode'),'')<>'commercial-combined')))
         THROW 51940,'Commercial servicing requires owned commercial adjustment inputs and its published adjustment fee.',1;
        END;
        """;

    // Retained baseline: CommercialServicingDecisionGuards.cs, TR_ServicingEvidenceAssociation_Commercial.
    private const string Previous4="""
        CREATE TRIGGER TR_ServicingEvidenceAssociation_Commercial ON ServicingEvidenceAssociation AFTER INSERT AS BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId JOIN Product p ON p.Id=c.ProductId
         WHERE (i.RequirementCode LIKE 'cc-%' AND (p.Code<>'commercial-combined' OR JSON_VALUE(c.InputJson,'$.format')<>'commercial-servicing-rating-input-1'
           OR (i.RiskItemId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM OPENJSON(c.InputJson,'$.slices') s
              CROSS APPLY OPENJSON(s.value,CASE WHEN i.RequirementCode='cc-wage-proof' THEN '$.commercial.pricing.proposal.risk.wages' ELSE '$.commercial.pricing.proposal.risk.locations' END)
              WITH(Id uniqueidentifier '$.id') subject WHERE subject.Id=i.RiskItemId))))
         OR (p.Code='commercial-combined' AND i.RequirementCode NOT LIKE 'cc-%' AND i.RequirementCode NOT IN ('capacity-response','signed-statement','acceptance-proof')))
         THROW 51942,'Commercial proof requires an owned commercial schedule subject and purpose.',1;
        END;
        """;

    // Retained baseline: CommercialServicingAtomicGuards.cs, TR_ServicingDraft_Issued.
    private const string Previous5="""
        CREATE OR ALTER TRIGGER TR_ServicingDraft_Issued ON ServicingDraft AFTER INSERT,UPDATE AS BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE d.State='issued' AND
          (i.State<>d.State OR i.IssuedTransactionId<>d.IssuedTransactionId OR i.CurrentRevisionId<>d.CurrentRevisionId
            OR ISNULL(i.CurrentCycleId,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.CurrentCycleId,'00000000-0000-0000-0000-000000000000')))
          THROW 51525,'Issued drafts cannot be reopened or revised.',1;
        IF EXISTS(SELECT 1 FROM inserted d WHERE d.State='issued' AND d.Kind<>'cancellation' AND NOT EXISTS(
          SELECT 1 FROM PolicyTransaction t JOIN ServicingIssueDecision s ON s.Id=t.ServicingIssueDecisionId
            JOIN IssueFinancialObligation o ON o.TransactionId=t.Id JOIN Journal j ON j.ObligationId=o.Id AND j.PostedAt IS NOT NULL
            JOIN ServicingCycle c ON c.Id=s.CycleId
          WHERE t.Id=d.IssuedTransactionId AND t.ServicingDraftId=d.Id AND t.ServicingRevisionId=d.CurrentRevisionId AND t.ServicingCycleId=d.CurrentCycleId
            AND (SELECT COUNT(*) FROM PolicyVersion v WHERE v.TransactionId=t.Id)=(SELECT COUNT(*) FROM OPENJSON(c.InputJson,'$.slices'))
            AND NOT EXISTS(SELECT 1 FROM PolicyVersion v WHERE v.TransactionId=t.Id AND
              ((JSON_VALUE(c.InputJson,'$.format')<>'commercial-servicing-rating-input-1' AND
                ((SELECT COUNT(DISTINCT doc.Kind) FROM PolicyDocumentRequest doc WHERE doc.VersionId=v.Id AND doc.Purpose=t.Kind)<>3
                  OR NOT EXISTS(SELECT 1 FROM PolicyMidIntent m WHERE m.VersionId=v.Id AND m.Purpose=t.Kind)))
              OR (JSON_VALUE(c.InputJson,'$.format')='commercial-servicing-rating-input-1' AND
                (COALESCE(APPLOCK_MODE('public','CoverMGA.CommercialExposure','Transaction'),'')<>'Exclusive'
                  OR NOT EXISTS(SELECT 1 FROM PolicyDocumentRequest doc WHERE doc.VersionId=v.Id AND doc.Purpose=t.Kind AND doc.Kind='policy-schedule')
                  OR NOT EXISTS(SELECT 1 FROM PolicyDocumentRequest doc WHERE doc.VersionId=v.Id AND doc.Purpose=t.Kind AND doc.Kind='policy-statement')
                  OR (SELECT COUNT(*) FROM PolicyDocumentRequest doc WHERE doc.VersionId=v.Id AND doc.Purpose=t.Kind)<>
                    2+CASE WHEN EXISTS(SELECT 1 FROM OPENJSON(v.SnapshotJson,'$.cover.sections') WITH(code nvarchar(100)) section WHERE section.code='employers-liability') THEN 1 ELSE 0 END
                  OR (SELECT COUNT(*) FROM PolicyDocumentRequest doc WHERE doc.VersionId=v.Id AND doc.Purpose=t.Kind AND doc.Kind='policy-certificate')<>
                    CASE WHEN EXISTS(SELECT 1 FROM OPENJSON(v.SnapshotJson,'$.cover.sections') WITH(code nvarchar(100)) section WHERE section.code='employers-liability') THEN 1 ELSE 0 END
                  OR EXISTS(SELECT 1 FROM PolicyMidIntent m WHERE m.VersionId=v.Id)
                  OR NOT EXISTS(SELECT 1 FROM CommercialExposureVersion e JOIN CommercialExposureIssueDecision decision ON decision.ExposureVersionId=e.Id
                    WHERE e.VersionId=v.Id AND JSON_VALUE(decision.DecisionJson,'$.format')='commercial-servicing-exposure-decision-1')))))
            AND NOT EXISTS(SELECT 1 FROM ServicingLease l WHERE l.DraftId=d.Id AND l.Active=1)))
          THROW 51525,'Issued draft requires its complete atomic policy, posting, documents and MID intent graph.',1;
        END;
        """;

    // Retained baseline: CommercialServicingProjectionGuards.cs, TR_CommercialExposureVersion_Source.
    private const string Previous6="""
        CREATE OR ALTER TRIGGER TR_CommercialExposureVersion_Source ON CommercialExposureVersion AFTER INSERT AS BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT i.Id,i.SourceHash,i.TermStartsAt,i.TermEndsAt,i.EffectiveAt,i.ProcessedAt,i.TransactionKind,i.TransactionSequence,i.SliceOrdinal FROM inserted i
          EXCEPT SELECT i.Id,v.ContentHash,t.StartsAt,t.EndsAt,v.EffectiveAt,v.ProcessedAt,tr.Kind,tr.Sequence,v.SliceOrdinal FROM inserted i
            JOIN PolicyVersion v ON v.Id=i.VersionId JOIN PolicyTerm t ON t.Id=v.TermId JOIN PolicyTransaction tr ON tr.Id=v.TransactionId)
          THROW 51423, 'Exposure metadata must match its immutable issued source.', 1;
        IF EXISTS(SELECT 1 FROM inserted i JOIN PolicyVersion v ON v.Id=i.VersionId JOIN Policy p ON p.Id=i.PolicyId
          JOIN CommercialExposureBook b ON b.Id=i.BookId JOIN PolicyTransaction tr ON tr.Id=i.TransactionId
          LEFT JOIN UnderwritingCycle uc ON uc.Id=tr.CycleId LEFT JOIN ServicingCycle sc ON sc.Id=tr.ServicingCycleId
          LEFT JOIN CancellationIssueDecision cd ON cd.Id=tr.CancellationIssueDecisionId
          LEFT JOIN CommercialExposureVersion baseExposure ON baseExposure.VersionId=cd.BaseVersionId
          WHERE b.ProductId<>p.ProductId OR COALESCE(JSON_VALUE(v.SnapshotJson,'$.productCode'),'')<>'commercial-combined'
            OR (tr.Kind<>'cancellation' AND (COALESCE(JSON_VALUE(v.SnapshotJson,'$.snapshotFormat'),'')<>CASE WHEN tr.Kind='adjustment' THEN 'issued-commercial-servicing-1' ELSE 'issued-commercial-1' END
              OR COALESCE(uc.BinderVersionId,sc.BinderVersionId,'00000000-0000-0000-0000-000000000000')<>i.BinderVersionId))
            OR (tr.Kind='cancellation' AND (baseExposure.Id IS NULL OR baseExposure.BookId<>i.BookId OR baseExposure.BinderVersionId<>i.BinderVersionId)))
          THROW 51423, 'Exposure requires its exact commercial product and source binder.', 1;
        IF EXISTS(SELECT 1 FROM inserted i JOIN PolicyVersion v ON v.Id=i.VersionId
          WHERE (SELECT COUNT(*) FROM OPENJSON(i.LocationsJson))>100
            OR (i.TransactionKind<>'cancellation' AND (ISJSON(JSON_QUERY(v.SnapshotJson,'$.risk.locations'),ARRAY)<>1
              OR JSON_QUERY(v.SnapshotJson,'$.risk.locations') IS NULL
              OR (SELECT COUNT(*) FROM OPENJSON(i.LocationsJson))<>(SELECT COUNT(*) FROM OPENJSON(v.SnapshotJson,'$.risk.locations')))))
          THROW 51424, 'Exposure must retain every issued location.', 1;
        IF EXISTS(SELECT i.Id,j.riskItemId,j.district,j.sumInsured FROM inserted i CROSS APPLY OPENJSON(i.LocationsJson)
            WITH(riskItemId uniqueidentifier,district nvarchar(100),sumInsured decimal(38,10)) j WHERE i.TransactionKind<>'cancellation'
          EXCEPT SELECT i.Id,j.id,LEFT(UPPER(REPLACE(j.postcode,' ','')),LEN(REPLACE(j.postcode,' ',''))-3),j.buildings+j.contents+j.stock
            FROM inserted i JOIN PolicyVersion v ON v.Id=i.VersionId CROSS APPLY OPENJSON(v.SnapshotJson,'$.risk.locations')
            WITH(id uniqueidentifier,postcode nvarchar(100) '$.address.postcode',buildings decimal(38,10),contents decimal(38,10),stock decimal(38,10)) j
            WHERE i.TransactionKind<>'cancellation')
          THROW 51424, 'Exposure property values and districts must match the complete issued source.', 1;
        INSERT CommercialExposureLocation(ExposureVersionId,RiskItemId,District,SumInsured)
          SELECT i.Id,j.riskItemId,j.district,j.sumInsured FROM inserted i CROSS APPLY OPENJSON(i.LocationsJson)
            WITH(riskItemId uniqueidentifier,district nvarchar(100),sumInsured decimal(38,10)) j;
        END;
        """;

    // Retained baseline: CommercialServicingExposureGuards.cs, TR_CommercialExposureIssueDecision_Source.
    private const string Previous7="""
        CREATE OR ALTER TRIGGER TR_CommercialExposureIssueDecision_Source ON CommercialExposureIssueDecision AFTER INSERT AS BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT 1 FROM inserted i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId WHERE v.TransactionKind NOT IN ('new-business','adjustment'))
         THROW 51945,'Unsupported commercial exposure decision transaction.',1;
        DECLARE @initial TABLE(Id uniqueidentifier,ExposureVersionId uniqueidentifier,AssessedAt datetimeoffset,DecisionJson nvarchar(max),DecisionHash binary(32),CreatedAt datetimeoffset,CreatedBy uniqueidentifier);
        INSERT @initial SELECT i.Id,i.ExposureVersionId,i.AssessedAt,i.DecisionJson,i.DecisionHash,i.CreatedAt,i.CreatedBy
         FROM inserted i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId WHERE v.TransactionKind='new-business';

        IF EXISTS(SELECT 1 FROM @initial i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId
          WHERE i.DecisionHash<>HASHBYTES('SHA2_256',CONVERT(varchar(max),i.DecisionJson COLLATE Latin1_General_100_BIN2_UTF8))
            OR COALESCE(JSON_VALUE(i.DecisionJson,'$.format'),'')<>'commercial-exposure-decision-1'
            OR v.TransactionKind<>'new-business' OR i.AssessedAt<>v.ProcessedAt OR i.CreatedBy<>v.CreatedBy)
          THROW 51921,'Capacity decision requires its exact issued source and hash.',1;
        IF EXISTS(SELECT i.Id,v.BookId,v.PolicyId,v.VersionId,v.SourceHash,v.TermStartsAt,v.TermEndsAt,i.AssessedAt
          FROM @initial i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId
          EXCEPT SELECT i.Id,j.bookId,j.policyId,j.versionId,TRY_CONVERT(binary(32),j.sourceHash,2),j.[from],j.[to],j.assessedAt
          FROM @initial i CROSS APPLY OPENJSON(i.DecisionJson) WITH(bookId uniqueidentifier,policyId uniqueidentifier,versionId uniqueidentifier,
            sourceHash varchar(64),[from] datetimeoffset,[to] datetimeoffset,assessedAt datetimeoffset) j)
          THROW 51921,'Capacity decision metadata must match the retained header.',1;
        IF EXISTS(SELECT 1 FROM @initial i WHERE ISJSON(JSON_QUERY(i.DecisionJson,'$.intervals'),ARRAY)<>1
          OR JSON_QUERY(i.DecisionJson,'$.intervals') IS NULL OR NOT EXISTS(SELECT 1 FROM OPENJSON(i.DecisionJson,'$.intervals')))
          THROW 51921,'Capacity decision needs a complete interval assessment.',1;
        IF EXISTS(SELECT 1 FROM @initial i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId
          JOIN PolicyVersion pv ON pv.Id=v.VersionId JOIN BinderVersion b ON b.Id=v.BinderVersionId
          LEFT JOIN UserAuthorityGrant g ON g.Id=TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DecisionJson,'$.authorityGrantId'))
          LEFT JOIN AuthorityVersion a ON a.Id=g.AuthorityVersionId
          WHERE g.Id IS NULL OR g.UserId<>i.CreatedBy OR a.BinderVersionId<>v.BinderVersionId
            OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DecisionJson,'$.authorityVersionId')),'00000000-0000-0000-0000-000000000000')<>a.Id
            OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(pv.SnapshotJson,'$.provenance.authorityVersionId')),'00000000-0000-0000-0000-000000000000')<>a.Id
            OR g.EffectiveFrom>i.AssessedAt OR g.EffectiveTo<=i.AssessedAt OR g.EffectiveFrom>v.TermStartsAt OR g.EffectiveTo<v.TermEndsAt
            OR (g.RevokedAt IS NOT NULL AND g.RevokedAt<=i.AssessedAt)
            OR COALESCE(TRY_CONVERT(decimal(38,2),JSON_VALUE(i.DecisionJson,'$.actorDistrictLimit')),-1)<>TRY_CONVERT(decimal(38,2),JSON_VALUE(a.DefinitionJson,'$.limits.districtProperty'))
            OR COALESCE(TRY_CONVERT(decimal(38,2),JSON_VALUE(i.DecisionJson,'$.binderDistrictLimit')),-1)<>TRY_CONVERT(decimal(38,2),JSON_VALUE(b.DefinitionJson,'$.limits.districtProperty')))
          THROW 51921,'Capacity decision must retain the actual actor and binder district authority.',1;
        DECLARE @interval TABLE(DecisionId uniqueidentifier,ExposureId uniqueidentifier,District nvarchar(4) COLLATE Latin1_General_100_BIN2,StartsAt datetimeoffset,EndsAt datetimeoffset,
          Proposed decimal(38,2),OtherSum decimal(38,2),Resulting decimal(38,2),PolicyCount int,LimitId uniqueidentifier,LimitHash binary(32),
          LimitAmount decimal(38,2),Headroom decimal(38,2),Blocker nvarchar(100));
        INSERT @interval SELECT i.Id,i.ExposureVersionId,j.district,j.[from],j.[to],j.proposedPropertySum,j.otherPropertySum,j.resultingPropertySum,j.policyCount,
          j.limitVersionId,TRY_CONVERT(binary(32),j.limitHash,2),j.limit,j.headroom,j.blocker FROM @initial i CROSS APPLY OPENJSON(i.DecisionJson,'$.intervals')
          WITH(district nvarchar(4),[from] datetimeoffset,[to] datetimeoffset,proposedPropertySum decimal(38,2),otherPropertySum decimal(38,2),
            resultingPropertySum decimal(38,2),policyCount int,limitVersionId uniqueidentifier,limitHash varchar(64),limit decimal(38,2),headroom decimal(38,2),blocker nvarchar(100)) j;
        IF EXISTS(SELECT 1 FROM @interval r JOIN @initial i ON i.Id=r.DecisionId JOIN CommercialExposureVersion v ON v.Id=r.ExposureId
          LEFT JOIN CommercialExposureLimitVersion l ON l.Id=r.LimitId
          WHERE r.District IS NULL OR r.StartsAt IS NULL OR r.EndsAt IS NULL OR r.StartsAt>=r.EndsAt OR r.StartsAt<v.TermStartsAt OR r.EndsAt>v.TermEndsAt
            OR r.Proposed IS NULL OR r.OtherSum IS NULL OR r.Resulting IS NULL OR r.Headroom IS NULL OR r.LimitAmount IS NULL OR r.LimitHash IS NULL
            OR r.PolicyCount IS NULL OR r.PolicyCount<0 OR r.Proposed<0 OR r.OtherSum<0 OR r.Headroom<0 OR r.Resulting<>r.Proposed+r.OtherSum
            OR r.Headroom<>r.LimitAmount-r.Resulting OR r.Blocker IS NOT NULL OR l.Id IS NULL OR l.BookId<>v.BookId
            OR r.Resulting>TRY_CONVERT(decimal(38,2),JSON_VALUE(i.DecisionJson,'$.actorDistrictLimit'))
            OR r.Resulting>TRY_CONVERT(decimal(38,2),JSON_VALUE(i.DecisionJson,'$.binderDistrictLimit'))
            OR (l.District<>'*' AND l.District<>r.District) OR l.PublishedAt>i.AssessedAt OR r.StartsAt<l.EffectiveFrom OR r.EndsAt>l.EffectiveTo
            OR r.LimitHash<>l.ContentHash OR r.LimitAmount<>l.Amount
            OR r.Proposed<>COALESCE((SELECT SUM(c.SumInsured) FROM CommercialExposureLocation c WHERE c.ExposureVersionId=v.Id AND c.District=r.District),0))
          THROW 51921,'Capacity decision needs exact own exposure and applicable pinned limits.',1;
        IF EXISTS(SELECT i.Id,COALESCE(c.District,'*') FROM @initial i LEFT JOIN CommercialExposureLocation c ON c.ExposureVersionId=i.ExposureVersionId
          EXCEPT SELECT DecisionId,District FROM @interval)
          OR EXISTS(SELECT DecisionId,District FROM @interval EXCEPT
            SELECT i.Id,COALESCE(c.District,'*') FROM @initial i LEFT JOIN CommercialExposureLocation c ON c.ExposureVersionId=i.ExposureVersionId)
          THROW 51921,'Capacity decision must include every own district.',1;
        IF EXISTS(SELECT 1 FROM (SELECT r.*,LAG(r.EndsAt,1,v.TermStartsAt) OVER(PARTITION BY r.DecisionId,r.District ORDER BY r.StartsAt) AS PreviousEnd
          FROM @interval r JOIN CommercialExposureVersion v ON v.Id=r.ExposureId) ordered WHERE StartsAt<>PreviousEnd)
          OR EXISTS(SELECT r.DecisionId,r.District FROM @interval r JOIN CommercialExposureVersion v ON v.Id=r.ExposureId
            GROUP BY r.DecisionId,r.District,v.TermEndsAt HAVING MAX(r.EndsAt)<>v.TermEndsAt)
          THROW 51921,'Capacity decision intervals must cover the complete issued term without gaps.',1;

        DECLARE @adjustment TABLE(Id uniqueidentifier,ExposureVersionId uniqueidentifier,AssessedAt datetimeoffset,DecisionJson nvarchar(max),DecisionHash binary(32),CreatedBy uniqueidentifier);
        INSERT @adjustment SELECT i.Id,i.ExposureVersionId,i.AssessedAt,i.DecisionJson,i.DecisionHash,i.CreatedBy
         FROM inserted i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId WHERE v.TransactionKind='adjustment';
        IF EXISTS(SELECT 1 FROM @adjustment i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId
         JOIN PolicyTransaction t ON t.Id=v.TransactionId JOIN ServicingIssueDecision d ON d.Id=t.ServicingIssueDecisionId
         JOIN ServicingCycle c ON c.Id=d.CycleId JOIN UserAuthorityGrant g ON g.Id=d.GrantId
         JOIN AuthorityVersion a ON a.Id=d.AuthorityVersionId JOIN BinderVersion b ON b.Id=c.BinderVersionId
         WHERE i.DecisionHash<>HASHBYTES('SHA2_256',CONVERT(varchar(max),i.DecisionJson COLLATE Latin1_General_100_BIN2_UTF8))
         OR COALESCE(JSON_VALUE(i.DecisionJson,'$.format'),'')<>'commercial-servicing-exposure-decision-1'
         OR i.AssessedAt<>v.ProcessedAt OR i.CreatedBy<>d.ActorId OR i.CreatedBy<>v.CreatedBy OR g.UserId<>d.ActorId
         OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DecisionJson,'$.bookId')),'00000000-0000-0000-0000-000000000000')<>v.BookId
         OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DecisionJson,'$.policyId')),'00000000-0000-0000-0000-000000000000')<>v.PolicyId
         OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DecisionJson,'$.versionId')),'00000000-0000-0000-0000-000000000000')<>v.VersionId
         OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DecisionJson,'$.transactionId')),'00000000-0000-0000-0000-000000000000')<>v.TransactionId
         OR COALESCE(JSON_VALUE(i.DecisionJson,'$.sourceHash'),'')<>LOWER(CONVERT(varchar(64),v.SourceHash,2))
         OR COALESCE(TRY_CONVERT(datetimeoffset,JSON_VALUE(i.DecisionJson,'$.assessedAt')),'0001-01-01')<>i.AssessedAt
         OR COALESCE(TRY_CONVERT(datetimeoffset,JSON_VALUE(i.DecisionJson,'$.from')),'0001-01-01')<>d.EffectiveAt
         OR COALESCE(TRY_CONVERT(datetimeoffset,JSON_VALUE(i.DecisionJson,'$.to')),'0001-01-01')<>v.TermEndsAt
         OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DecisionJson,'$.authorityGrantId')),'00000000-0000-0000-0000-000000000000')<>g.Id
         OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.DecisionJson,'$.authorityVersionId')),'00000000-0000-0000-0000-000000000000')<>a.Id
         OR g.AuthorityVersionId<>a.Id OR a.BinderVersionId<>b.Id OR b.Id<>v.BinderVersionId
         OR g.EffectiveFrom>i.AssessedAt OR g.EffectiveTo<=i.AssessedAt OR g.EffectiveFrom>d.EffectiveAt OR g.EffectiveTo<v.TermEndsAt
         OR (g.RevokedAt IS NOT NULL AND g.RevokedAt<=i.AssessedAt)
         OR COALESCE(TRY_CONVERT(decimal(38,2),JSON_VALUE(i.DecisionJson,'$.actorDistrictLimit')),-1)<>TRY_CONVERT(decimal(38,2),JSON_VALUE(a.DefinitionJson,'$.limits.districtProperty'))
         OR COALESCE(TRY_CONVERT(decimal(38,2),JSON_VALUE(i.DecisionJson,'$.binderDistrictLimit')),-1)<>TRY_CONVERT(decimal(38,2),JSON_VALUE(b.DefinitionJson,'$.limits.districtProperty')))
         THROW 51945,'Commercial adjustment capacity decision must retain exact source, clock and authority.',1;
        IF EXISTS(SELECT 1 FROM @adjustment i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId
         WHERE JSON_QUERY(i.DecisionJson,'$.versions') IS NULL OR (SELECT COUNT(*) FROM OPENJSON(i.DecisionJson,'$.versions'))<>
         (SELECT COUNT(*) FROM PolicyVersion p WHERE p.TransactionId=v.TransactionId)
         OR EXISTS(SELECT p.Id,p.ContentHash,p.EffectiveAt,p.SliceOrdinal FROM PolicyVersion p WHERE p.TransactionId=v.TransactionId
         EXCEPT SELECT j.versionId,TRY_CONVERT(binary(32),j.sourceHash,2),j.effectiveAt,j.sliceOrdinal FROM OPENJSON(i.DecisionJson,'$.versions')
         WITH(versionId uniqueidentifier,sourceHash varchar(64),effectiveAt datetimeoffset,sliceOrdinal int) j))
         THROW 51945,'Capacity decision must retain the complete immutable adjustment version manifest.',1;
        DECLARE @dated TABLE(DecisionId uniqueidentifier,ExposureId uniqueidentifier,District nvarchar(4) COLLATE Latin1_General_100_BIN2,StartsAt datetimeoffset,EndsAt datetimeoffset,
         OwnSum decimal(38,2),OtherSum decimal(38,2),Total decimal(38,2),PolicyCount int,LimitId uniqueidentifier,LimitHash binary(32),LimitAmount decimal(38,2),Headroom decimal(38,2),Blocker nvarchar(100));
        INSERT @dated SELECT i.Id,i.ExposureVersionId,j.district,j.[from],j.[to],j.proposedPropertySum,j.otherPropertySum,j.resultingPropertySum,j.policyCount,
         j.limitVersionId,TRY_CONVERT(binary(32),j.limitHash,2),j.limit,j.headroom,j.blocker FROM @adjustment i CROSS APPLY OPENJSON(i.DecisionJson,'$.intervals')
         WITH(district nvarchar(4),[from] datetimeoffset,[to] datetimeoffset,proposedPropertySum decimal(38,2),otherPropertySum decimal(38,2),resultingPropertySum decimal(38,2),
         policyCount int,limitVersionId uniqueidentifier,limitHash varchar(64),limit decimal(38,2),headroom decimal(38,2),blocker nvarchar(100)) j;
        IF EXISTS(SELECT 1 FROM @adjustment i WHERE NOT EXISTS(SELECT 1 FROM @dated d WHERE d.DecisionId=i.Id))
         THROW 51945,'A commercial adjustment requires a complete dated capacity assessment.',1;
        DECLARE @district TABLE(DecisionId uniqueidentifier,District nvarchar(4) COLLATE Latin1_General_100_BIN2);
        INSERT @district SELECT DISTINCT i.Id,loc.District FROM @adjustment i JOIN CommercialExposureVersion v ON v.Id=i.ExposureVersionId
          JOIN CommercialExposureVersion own ON own.BookId=v.BookId AND own.PolicyId=v.PolicyId AND own.ProcessedAt<=i.AssessedAt
            AND own.EffectiveAt<v.TermEndsAt AND own.TermStartsAt<v.TermEndsAt
            AND TRY_CONVERT(datetimeoffset,JSON_VALUE(i.DecisionJson,'$.from'))<own.TermEndsAt
          JOIN CommercialExposureLocation loc ON loc.ExposureVersionId=own.Id;
        INSERT @district SELECT i.Id,'*' FROM @adjustment i WHERE NOT EXISTS(SELECT 1 FROM @district d WHERE d.DecisionId=i.Id);
        IF EXISTS(SELECT DecisionId,District FROM @district EXCEPT SELECT DecisionId,District FROM @dated)
          OR EXISTS(SELECT DecisionId,District FROM @dated EXCEPT SELECT DecisionId,District FROM @district)
          THROW 51945,'Commercial adjustment assessment must include every previous and proposed own district.',1;
        IF EXISTS(SELECT 1 FROM @dated r JOIN @adjustment i ON i.Id=r.DecisionId JOIN CommercialExposureVersion v ON v.Id=r.ExposureId
          JOIN CommercialExposureVersion source ON source.BookId=v.BookId AND source.ProcessedAt<=i.AssessedAt
          CROSS APPLY(VALUES(source.TermStartsAt),(source.TermEndsAt),(source.EffectiveAt)) point(At)
          WHERE r.StartsAt<point.At AND point.At<r.EndsAt)
          OR EXISTS(SELECT 1 FROM @dated r JOIN @adjustment i ON i.Id=r.DecisionId JOIN CommercialExposureVersion v ON v.Id=r.ExposureId
            JOIN CommercialExposureLimitVersion limit ON limit.BookId=v.BookId AND limit.PublishedAt<=i.AssessedAt
              AND (limit.District='*' OR EXISTS(SELECT 1 FROM @district d WHERE d.DecisionId=i.Id AND d.District=limit.District))
            CROSS APPLY(VALUES(limit.EffectiveFrom),(limit.EffectiveTo)) point(At)
            WHERE r.StartsAt<point.At AND point.At<r.EndsAt)
          THROW 51945,'Commercial adjustment intervals must include every known book and applicable limit boundary.',1;
        IF EXISTS(SELECT 1 FROM @dated r JOIN @adjustment i ON i.Id=r.DecisionId JOIN CommercialExposureVersion v ON v.Id=r.ExposureId
         LEFT JOIN CommercialExposureLimitVersion l ON l.Id=r.LimitId
         OUTER APPLY(SELECT SUM(loc.SumInsured) OwnSum,COUNT(DISTINCT winners.PolicyId) OwnCount
           FROM(SELECT x.Id,x.PolicyId,x.TransactionKind,ROW_NUMBER() OVER(PARTITION BY x.PolicyId ORDER BY x.TermStartsAt DESC,x.TermId,x.EffectiveAt DESC,x.TransactionSequence DESC,x.SliceOrdinal DESC,x.VersionId) rn
             FROM CommercialExposureVersion x WHERE x.BookId=v.BookId AND x.PolicyId=v.PolicyId AND x.ProcessedAt<=i.AssessedAt
              AND x.TermStartsAt<=r.StartsAt AND r.StartsAt<x.TermEndsAt AND x.EffectiveAt<=r.StartsAt) winners
           JOIN CommercialExposureLocation loc ON loc.ExposureVersionId=winners.Id AND loc.District=r.District AND loc.SumInsured>0
           WHERE winners.rn=1 AND winners.TransactionKind<>'cancellation') owned
        OUTER APPLY(SELECT SUM(loc.SumInsured) OtherSum,COUNT(DISTINCT winners.PolicyId) OtherCount
           FROM(SELECT x.Id,x.PolicyId,x.TransactionKind,ROW_NUMBER() OVER(PARTITION BY x.PolicyId ORDER BY x.TermStartsAt DESC,x.TermId,x.EffectiveAt DESC,x.TransactionSequence DESC,x.SliceOrdinal DESC,x.VersionId) rn
             FROM CommercialExposureVersion x WHERE x.BookId=v.BookId AND x.PolicyId<>v.PolicyId AND x.ProcessedAt<=i.AssessedAt
              AND x.TermStartsAt<=r.StartsAt AND r.StartsAt<x.TermEndsAt AND x.EffectiveAt<=r.StartsAt) winners
           JOIN CommercialExposureLocation loc ON loc.ExposureVersionId=winners.Id AND loc.District=r.District AND loc.SumInsured>0
           WHERE winners.rn=1 AND winners.TransactionKind<>'cancellation') others
        CROSS APPLY(SELECT owned.OwnSum,others.OtherSum,owned.OwnCount+others.OtherCount PolicyCount) actual
         WHERE r.District IS NULL OR r.StartsAt IS NULL OR r.EndsAt IS NULL OR r.StartsAt>=r.EndsAt
         OR r.StartsAt<TRY_CONVERT(datetimeoffset,JSON_VALUE(i.DecisionJson,'$.from')) OR r.EndsAt>v.TermEndsAt
         OR r.OwnSum IS NULL OR r.OtherSum IS NULL OR r.Total IS NULL OR r.PolicyCount IS NULL OR r.LimitAmount IS NULL OR r.Headroom IS NULL OR r.LimitHash IS NULL
         OR r.OwnSum<>COALESCE(actual.OwnSum,0) OR r.OtherSum<>COALESCE(actual.OtherSum,0) OR r.PolicyCount<>actual.PolicyCount
         OR r.Total<>r.OwnSum+r.OtherSum OR r.Headroom<>r.LimitAmount-r.Total OR r.Headroom<0 OR r.Blocker IS NOT NULL
         OR l.Id IS NULL OR l.BookId<>v.BookId OR (l.District<>'*' AND l.District<>r.District) OR l.PublishedAt>i.AssessedAt
         OR r.StartsAt<l.EffectiveFrom OR r.EndsAt>l.EffectiveTo OR r.LimitHash<>l.ContentHash OR r.LimitAmount<>l.Amount
         OR r.Total>TRY_CONVERT(decimal(38,2),JSON_VALUE(i.DecisionJson,'$.actorDistrictLimit'))
         OR r.Total>TRY_CONVERT(decimal(38,2),JSON_VALUE(i.DecisionJson,'$.binderDistrictLimit')))
         THROW 51945,'Commercial adjustment intervals must match the whole issued book and pinned limits.',1;
        IF EXISTS(SELECT 1 FROM(SELECT r.*,LAG(r.EndsAt,1,TRY_CONVERT(datetimeoffset,JSON_VALUE(i.DecisionJson,'$.from'))) OVER(PARTITION BY r.DecisionId,r.District ORDER BY r.StartsAt) PreviousEnd
         FROM @dated r JOIN @adjustment i ON i.Id=r.DecisionId) ordered WHERE StartsAt<>PreviousEnd)
         OR EXISTS(SELECT r.DecisionId,r.District FROM @dated r JOIN CommercialExposureVersion v ON v.Id=r.ExposureId GROUP BY r.DecisionId,r.District,v.TermEndsAt HAVING MAX(r.EndsAt)<>v.TermEndsAt)
         THROW 51945,'Commercial adjustment intervals must cover each assessed district without gaps.',1;
        END;
        """;

    // Retained baseline: 20260918151032_RenewalPreparationVersions.cs, TR_RenewalPreparationVersion_Source.
    private const string Previous8="""
        CREATE TRIGGER TR_RenewalPreparationVersion_Source ON RenewalPreparationVersion AFTER INSERT AS
        BEGIN
          SET NOCOUNT ON;
          IF EXISTS(SELECT 1 FROM inserted i
            JOIN ServicingDraft d ON d.Id=i.DraftId JOIN Policy p ON p.Id=i.PolicyId JOIN PolicyTerm t ON t.Id=i.BaseTermId
            JOIN ProductVersion pv ON pv.Id=i.ProductVersionId JOIN BinderVersion b ON b.Id=i.BinderVersionId
            JOIN AgencyTermsVersion a ON a.Id=i.AgencyTermsVersionId JOIN SettingVersion s ON s.Id=i.RuleSettingVersionId
            WHERE d.Kind<>'renewal' OR d.State<>'draft' OR i.CreatedAt<d.CreatedAt OR i.StartsAt<>t.EndsAt
              OR a.AgencyId<>p.AgencyId OR a.EffectiveFrom>CONVERT(date,i.StartsAt AT TIME ZONE 'GMT Standard Time')
              OR pv.State<>'published' OR b.State<>'published' OR pv.EffectiveFrom>i.StartsAt OR pv.EffectiveTo<i.EndsAt
              OR b.EffectiveFrom>i.StartsAt OR b.EffectiveTo<i.EndsAt
              OR s.Scope<>'renewal-preparation' OR s.EffectiveFrom>i.CreatedAt
              OR EXISTS(SELECT 1 FROM SettingVersion newer WHERE newer.Scope=s.Scope AND newer.EffectiveFrom<=i.CreatedAt AND newer.Version>s.Version)
              OR NOT EXISTS(SELECT 1 FROM OPENJSON(s.[Values],'$.allowedTermMonths') m WHERE TRY_CONVERT(int,m.[value])=i.TermMonths)
              OR i.Sequence<>1+(SELECT COUNT(*) FROM RenewalPreparationVersion r WHERE r.DraftId=i.DraftId AND r.Sequence<i.Sequence)
              OR EXISTS(SELECT 1 FROM RenewalPreparationVersion r WHERE r.DraftId=i.DraftId AND r.Sequence<i.Sequence AND r.CreatedAt>i.CreatedAt)
              OR DATEADD(month,i.TermMonths,CONVERT(datetime2,i.StartsAt AT TIME ZONE 'GMT Standard Time'))<>CONVERT(datetime2,i.EndsAt AT TIME ZONE 'GMT Standard Time')
              OR COALESCE(JSON_VALUE(i.TermIntentJson,'$.kind'),'')<>CASE WHEN i.TermMonths=12 THEN 'annual' ELSE 'short-period' END
              OR COALESCE(JSON_VALUE(i.TermIntentJson,'$.timeZone'),'')<>'Europe/London'
              OR COALESCE(JSON_VALUE(i.TermIntentJson,'$.localStartDate'),'')<>CONVERT(char(10),i.StartsAt AT TIME ZONE 'GMT Standard Time',23)
              OR COALESCE(JSON_VALUE(i.TermIntentJson,'$.localStartTime'),'')<>CONVERT(char(5),CONVERT(time,i.StartsAt AT TIME ZONE 'GMT Standard Time'),108)
              OR COALESCE(TRY_CONVERT(int,JSON_VALUE(i.TermIntentJson,'$.utcOffsetMinutes')),-999)<>DATEPART(tzoffset,i.StartsAt AT TIME ZONE 'GMT Standard Time')
              OR (i.EndUtcOffsetMinutes IS NOT NULL AND i.EndUtcOffsetMinutes<>DATEPART(tzoffset,i.EndsAt AT TIME ZONE 'GMT Standard Time'))
              OR (i.TermMonths<>12 AND (COALESCE(JSON_VALUE(i.TermIntentJson,'$.localEndDate'),'')<>CONVERT(char(10),i.EndsAt AT TIME ZONE 'GMT Standard Time',23)
                OR COALESCE(JSON_VALUE(i.TermIntentJson,'$.localEndTime'),'')<>CONVERT(char(5),CONVERT(time,i.EndsAt AT TIME ZONE 'GMT Standard Time'),108)))
              OR EXISTS(SELECT 1 FROM PolicyTerm other WHERE other.PolicyId=i.PolicyId AND other.Id<>i.BaseTermId AND other.StartsAt<i.EndsAt AND i.StartsAt<other.EndsAt)
              OR NOT EXISTS(SELECT 1 FROM PolicyVersion v JOIN PolicyTransaction pt ON pt.Id=v.TransactionId
                WHERE v.Id=i.BaseVersionId AND pt.Kind IN ('new-business','adjustment','renewal') AND v.EffectiveAt<t.EndsAt AND v.ProcessedAt<=i.CreatedAt AND pt.ProcessedAt<=i.CreatedAt)
              OR i.BaseVersionId<>(SELECT TOP(1) v.Id FROM PolicyVersion v JOIN PolicyTransaction pt ON pt.Id=v.TransactionId
                WHERE v.TermId=i.BaseTermId AND v.PolicyId=i.PolicyId AND v.EffectiveAt<t.EndsAt AND v.ProcessedAt<=i.CreatedAt AND pt.ProcessedAt<=i.CreatedAt
                AND pt.Kind IN ('new-business','adjustment','renewal','cancellation') ORDER BY v.EffectiveAt DESC,pt.Sequence DESC,v.SliceOrdinal DESC,v.Id))
            THROW 51721,'Renewal preparation requires current owned configuration and the known uncancelled term-end risk.',1;
        END;
        """;

    // Retained baseline: RenewalLapseGuards.cs, TR_RenewalLapseEvent_Source.
    private const string Previous9="""
        CREATE TRIGGER TR_RenewalLapseEvent_Source ON RenewalLapseEvent AFTER INSERT AS BEGIN
        SET NOCOUNT ON;
        IF EXISTS(SELECT 1 FROM inserted i JOIN PolicyTerm t WITH(UPDLOCK,HOLDLOCK) ON t.Id=i.TermId
            JOIN SettingVersion s ON s.Id=i.RuleSettingVersionId JOIN OutboxWork w ON w.Id=i.WorkId
            WHERE i.PolicyId<>t.PolicyId OR i.EffectiveAt<>t.EndsAt OR s.Scope<>'renewal-preparation' OR s.EffectiveFrom>i.CreatedAt
            OR ISNULL(JSON_VALUE(s.[Values],'$.demo'),'')<>'true' OR ISNULL(TRY_CONVERT(int,JSON_VALUE(s.[Values],'$.lapseDaysAfterExpiry')),-1) NOT BETWEEN 0 AND 365
            OR i.AutoLapseAt<>SWITCHOFFSET(DATEADD(day,TRY_CONVERT(int,JSON_VALUE(s.[Values],'$.lapseDaysAfterExpiry')),
                CONVERT(datetime2,t.EndsAt AT TIME ZONE 'GMT Standard Time')) AT TIME ZONE 'GMT Standard Time','+00:00')
            OR w.Kind<>'renewal-lapse-notification' OR w.SubjectRecordId<>i.Id OR w.ScenarioVersionId<>i.RuleSettingVersionId
            OR w.SubjectRecordId IS NULL OR w.ScenarioVersionId IS NULL
            OR w.OperationKey<>'renewal-lapse/'+LOWER(REPLACE(CONVERT(varchar(36),i.TermId),'-',''))
            OR ISNULL(JSON_VALUE(w.Payload,'$.format'),'')<>'renewal-lapse-notification-1' OR ISNULL(JSON_VALUE(w.Payload,'$.demo'),'')<>'true'
            OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(w.Payload,'$.eventId')) IS NULL
            OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(w.Payload,'$.eventId'))<>i.Id
            OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(w.Payload,'$.termId')) IS NULL
            OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(w.Payload,'$.termId'))<>i.TermId
            OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(w.Payload,'$.policyId')) IS NULL
            OR TRY_CONVERT(uniqueidentifier,JSON_VALUE(w.Payload,'$.policyId'))<>i.PolicyId
            OR ISNULL(JSON_VALUE(w.Payload,'$.reason'),'')<>i.Reason
            OR TRY_CONVERT(datetimeoffset,JSON_VALUE(w.Payload,'$.effectiveAt')) IS NULL
            OR TRY_CONVERT(datetimeoffset,JSON_VALUE(w.Payload,'$.effectiveAt'))<>i.EffectiveAt
            OR JSON_QUERY(w.Payload,'$.recipients') IS NULL
            OR CONVERT(varbinary(max),JSON_QUERY(w.Payload,'$.recipients'))<>CONVERT(varbinary(max),i.RecipientSnapshotJson)
            OR EXISTS(SELECT 1 FROM PolicyTerm next WITH(UPDLOCK,HOLDLOCK) WHERE next.PolicyId=i.PolicyId AND next.Id<>t.Id AND next.StartsAt=t.EndsAt)
            OR EXISTS(SELECT 1 FROM ServicingDraft d WITH(UPDLOCK,HOLDLOCK) JOIN ServicingCycle c ON c.Id=d.CurrentCycleId
                WHERE d.BaseTermId=t.Id AND d.Kind='renewal' AND d.State='draft' AND c.State='rated' AND c.CurrentAcceptanceId IS NOT NULL)
            OR (SELECT TOP(1) tx.Kind FROM PolicyVersion v JOIN PolicyTransaction tx ON tx.Id=v.TransactionId
                WHERE v.TermId=t.Id AND v.EffectiveAt<t.EndsAt AND v.ProcessedAt<=i.CreatedAt
                ORDER BY v.EffectiveAt DESC,tx.Sequence DESC,v.SliceOrdinal DESC)='cancellation')
            THROW 51762,'Lapse requires the expiring term, exact configured deadline and durable notification, without an accepted or issued renewal.',1;
        END;
        """;

}
