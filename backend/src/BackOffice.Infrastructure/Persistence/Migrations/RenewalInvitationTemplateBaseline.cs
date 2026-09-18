namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class RenewalInvitationTemplates
{
    // Frozen previous trigger; historical migrations remain unchanged.
    private const string PreviousTermsSource = """
            CREATE TRIGGER TR_ServicingTermsVersion_Source ON ServicingTermsVersion AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ServicingCycle c ON c.Id=i.CycleId
              JOIN ServicingDraft d ON d.Id=i.DraftId JOIN ServicingRatingResult r ON r.Id=i.RatingId
              JOIN TemplateVersion t ON t.Id=i.TemplateVersionId
              WHERE d.State<>'draft' OR d.CurrentCycleId IS NULL OR d.CurrentCycleId<>i.CycleId
                OR d.CurrentRevisionId IS NULL OR d.CurrentRevisionId<>i.RevisionId
                OR c.State<>'rated' OR c.CurrentRatingId IS NULL OR c.CurrentRatingId<>i.RatingId
                OR c.BaseVersionId<>i.BaseVersionId OR i.PreparedAt<r.CompletedAt OR i.PreparedAt>=r.ExpiresAt
                OR t.Kind<>'servicing-terms' OR t.State<>'published' OR t.ProductId<>c.ProductId
                OR i.PreparedAt<t.EffectiveFrom OR i.PreparedAt>=t.EffectiveTo
                OR COALESCE(JSON_VALUE(i.TermsJson,'$.format'),'')<>'servicing-contract-1'
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.TermsJson,'$.draftId')),'00000000-0000-0000-0000-000000000000')<>i.DraftId
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.TermsJson,'$.cycleId')),'00000000-0000-0000-0000-000000000000')<>i.CycleId
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.TermsJson,'$.revisionId')),'00000000-0000-0000-0000-000000000000')<>i.RevisionId
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.TermsJson,'$.baseVersionId')),'00000000-0000-0000-0000-000000000000')<>i.BaseVersionId
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.TermsJson,'$.ratingId')),'00000000-0000-0000-0000-000000000000')<>i.RatingId
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(i.TermsJson,'$.templateVersionId')),'00000000-0000-0000-0000-000000000000')<>i.TemplateVersionId
                OR COALESCE(JSON_VALUE(i.TermsJson,'$.inputHash'),'') COLLATE Latin1_General_100_BIN2<>LOWER(CONVERT(varchar(64),c.InputHash,2))
                OR COALESCE(JSON_VALUE(i.TermsJson,'$.ratingHash'),'') COLLATE Latin1_General_100_BIN2<>LOWER(CONVERT(varchar(64),r.ResultHash,2))
                OR i.Sequence<>1+COALESCE((SELECT MAX(x.Sequence) FROM ServicingTermsVersion x WHERE x.CycleId=i.CycleId AND x.Sequence<i.Sequence),0)
                OR EXISTS(SELECT 1 FROM ServicingTermsVersion x WHERE x.CycleId=i.CycleId AND x.Sequence<i.Sequence AND x.PreparedAt>i.PreparedAt)
                OR EXISTS(SELECT 1 FROM (VALUES
                  (JSON_VALUE(i.TermsJson,'$.price.premium'),r.Premium),(JSON_VALUE(i.TermsJson,'$.price.tax'),r.Tax),
                  (JSON_VALUE(i.TermsJson,'$.price.fee'),r.Fee),(JSON_VALUE(i.TermsJson,'$.price.brokerCommission'),r.BrokerCommission),
                  (JSON_VALUE(i.TermsJson,'$.price.grossPayable'),r.GrossPayable),(JSON_VALUE(i.TermsJson,'$.price.netDue'),r.NetDue)
                  ) p(Value,Expected) WHERE TRY_CONVERT(decimal(38,18),p.Value) IS NULL OR TRY_CONVERT(decimal(38,18),p.Value)<>p.Expected)
                OR COALESCE(ISJSON(JSON_QUERY(i.TermsJson,'$.effectiveDates'),ARRAY),0)<>1
                OR (SELECT COUNT(*) FROM OPENJSON(i.TermsJson,'$.effectiveDates'))<>(SELECT COUNT(*) FROM OPENJSON(c.InputJson,'$.slices'))
                OR EXISTS(SELECT 1 FROM OPENJSON(c.InputJson,'$.slices') s LEFT JOIN OPENJSON(i.TermsJson,'$.effectiveDates') e ON e.[key]=s.[key]
                  WHERE TRY_CONVERT(datetimeoffset,e.value) IS NULL OR DATEPART(TZOFFSET,TRY_CONVERT(datetimeoffset,e.value))<>0
                    OR TRY_CONVERT(datetimeoffset,e.value)<>TRY_CONVERT(datetimeoffset,JSON_VALUE(s.value,'$.effectiveAt'))))
              THROW 51500,'Servicing terms require current rated scope, template, exact dates and rated price.',1;
            END;
        """;
}
