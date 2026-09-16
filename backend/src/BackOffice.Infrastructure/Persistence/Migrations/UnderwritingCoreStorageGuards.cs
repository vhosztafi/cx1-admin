using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class UnderwritingCoreStorage
{
    private static void AddUnderwritingGuards(MigrationBuilder migration)
    {
        foreach (var table in new[] { "QuoteRatingResult", "QuoteSubmission" })
            migration.Sql($"CREATE TRIGGER TR_{table}_AppendOnly ON [{table}] AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51100, 'Underwriting history is append-only.', 1; END;");
        foreach (var (table, columns) in new[] {
            ("RatingRuleVersion", "ProductId,Version,EffectiveFrom,EffectiveTo,DefinitionJson"),
            ("BinderVersion", "ProductId,ProviderId,Version,EffectiveFrom,EffectiveTo,DefinitionJson"),
            ("AuthorityVersion", "ProductId,ProductVersionId,BinderVersionId,Version,EffectiveFrom,EffectiveTo,DefinitionJson"),
            ("UnderwritingCycle", "QuoteId,QuoteRevisionId,AgencyId,ClientId,RelationshipId,ProductId,ProductVersionId,AgencyTermsVersionId,RatingRuleVersionId,BinderVersionId,AuthorityVersionId,Sequence,WorkId,PricingInputHash,InputJson,StartsAt,EndsAt,RequestedBy"),
            ("QuoteReferral", "CycleId,QuoteId,RatingId,Sequence,RuleCode,Dimension,RiskItemId,TargetKey,RequiredAuthorityJson,Reason"),
            ("UserAuthorityGrant", "UserId,AuthorityVersionId,EffectiveFrom,EffectiveTo,GrantedBy,Reason") })
        {
            var guard = string.Join(" OR ", ("Id,CreatedAt,CreatedBy," + columns).Split(',').Select(x => "UPDATE(" + x + ")"));
            var final = table is "RatingRuleVersion" or "BinderVersion" or "AuthorityVersion"
                ? "IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE d.State='retired' AND i.State<>'retired') THROW 51101, 'Retired underwriting versions cannot be reactivated.', 1;"
                : table == "UserAuthorityGrant" ? "IF EXISTS(SELECT 1 FROM deleted WHERE RevokedAt IS NOT NULL) AND (UPDATE(RevokedAt) OR UPDATE(RevokedBy) OR UPDATE(RevocationReason)) THROW 51102, 'Authority revocation is final.', 1;"
                : table == "UnderwritingCycle" ? "IF EXISTS(SELECT 1 FROM deleted WHERE State IN ('superseded','bound')) AND (UPDATE(State) OR UPDATE(CurrentRatingId) OR UPDATE(SupersededAt) OR UPDATE(SupersededReason)) THROW 51103, 'Completed underwriting cycles cannot be reopened.', 1;" : "";
            migration.Sql($"CREATE TRIGGER TR_{table}_InputImmutable ON [{table}] AFTER UPDATE AS BEGIN SET NOCOUNT ON; IF {guard} THROW 51104, 'Underwriting provenance is immutable.', 1; {final} END;");
            migration.Sql($"CREATE TRIGGER TR_{table}_NoDelete ON [{table}] AFTER DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51105, 'Underwriting history cannot be deleted.', 1; END;");
        }
        foreach (var table in new[] { "RatingRuleVersion", "BinderVersion", "AuthorityVersion" })
            migration.Sql($"CREATE TRIGGER TR_{table}_Product ON [{table}] AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted i JOIN Product p ON p.Id=i.ProductId WHERE COALESCE(JSON_VALUE(i.DefinitionJson,'$.productCode'),'')<>p.Code COLLATE Latin1_General_100_BIN2) THROW 51106, 'Underwriting configuration has another product.', 1; END;");
        foreach (var table in new[] { "RatingRuleVersion", "BinderVersion" })
            migration.Sql($"CREATE TRIGGER TR_{table}_PublishedInterval ON [{table}] AFTER INSERT,UPDATE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted i JOIN [{table}] p WITH(UPDLOCK,HOLDLOCK) ON p.ProductId=i.ProductId AND p.Id<>i.Id WHERE i.State='published' AND p.State='published' AND i.EffectiveFrom<p.EffectiveTo AND p.EffectiveFrom<i.EffectiveTo) THROW 51107, 'Published underwriting intervals overlap.', 1; END;");
        migration.Sql("""
            CREATE TRIGGER TR_AuthorityVersion_Binder ON AuthorityVersion AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN BinderVersion b ON b.Id=i.BinderVersionId JOIN ProductVersion p ON p.Id=i.ProductVersionId
              WHERE p.ProviderId<>b.ProviderId OR i.EffectiveFrom<b.EffectiveFrom OR i.EffectiveTo>b.EffectiveTo)
              THROW 51112, 'Authority provider or validity exceeds its binder.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_UserAuthorityGrant_Context ON UserAuthorityGrant AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN AuthorityVersion a ON a.Id=i.AuthorityVersionId JOIN [User] u ON u.Id=i.UserId
              WHERE u.AgencyId IS NOT NULL OR i.EffectiveFrom<a.EffectiveFrom OR i.EffectiveTo>a.EffectiveTo)
              THROW 51113, 'Authority grant requires internal identity and contained validity.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_UnderwritingCycle_Work ON UnderwritingCycle AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN OutboxWork w ON w.Id=i.WorkId
              WHERE w.Kind<>'quote-rating' OR w.SubjectRecordId IS NULL OR w.SubjectRecordId<>i.Id)
              THROW 51108, 'Rating work belongs to another subject or kind.', 1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ProductVersion_UnderwritingImmutable ON ProductVersion AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM deleted WHERE JSON_VALUE(Definition,'$.kind')='motor-trade-underwriting')
            BEGIN
              IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE JSON_VALUE(d.Definition,'$.kind')='motor-trade-underwriting' AND i.Id IS NULL)
                THROW 51109, 'Underwriting product history cannot be deleted.', 1;
              IF UPDATE(Id) OR UPDATE(ProductId) OR UPDATE(Version) OR UPDATE(ProviderId) OR UPDATE(EffectiveFrom) OR UPDATE(EffectiveTo) OR UPDATE(Definition) OR UPDATE(JsonSchemaVersion) OR UPDATE(QuestionSetVersion) OR UPDATE(CreatedAt) OR UPDATE(CreatedBy)
                THROW 51110, 'Underwriting product definition is immutable.', 1;
              IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE JSON_VALUE(d.Definition,'$.kind')='motor-trade-underwriting' AND (i.State NOT IN ('published','retired') OR (d.State='retired' AND i.State<>'retired')))
                THROW 51111, 'Underwriting product retirement is final.', 1;
            END;
            END;
            """);
    }

    private static void RemoveUnderwritingGuards(MigrationBuilder migration)
    {
        foreach (var table in new[] { "QuoteRatingResult", "QuoteSubmission" }) migration.Sql($"DROP TRIGGER TR_{table}_AppendOnly;");
        foreach (var table in new[] { "RatingRuleVersion", "BinderVersion", "AuthorityVersion", "UnderwritingCycle", "QuoteReferral", "UserAuthorityGrant" })
        { migration.Sql($"DROP TRIGGER TR_{table}_InputImmutable;"); migration.Sql($"DROP TRIGGER TR_{table}_NoDelete;"); }
        foreach (var table in new[] { "RatingRuleVersion", "BinderVersion", "AuthorityVersion" }) migration.Sql($"DROP TRIGGER TR_{table}_Product;");
        foreach (var table in new[] { "RatingRuleVersion", "BinderVersion" }) migration.Sql($"DROP TRIGGER TR_{table}_PublishedInterval;");
        migration.Sql("DROP TRIGGER TR_UnderwritingCycle_Work;"); migration.Sql("DROP TRIGGER TR_ProductVersion_UnderwritingImmutable;");
        migration.Sql("DROP TRIGGER TR_AuthorityVersion_Binder;"); migration.Sql("DROP TRIGGER TR_UserAuthorityGrant_Context;");
    }
}
