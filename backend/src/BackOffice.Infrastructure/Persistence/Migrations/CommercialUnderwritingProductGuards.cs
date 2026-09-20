using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class CommercialUnderwritingDefinitions
{
    private static void AddCommercialProductGuards(MigrationBuilder migration)
    {
        // A capture-only CC edition may coexist with its separately adopted
        // underwriting edition. Two editions of either capability still may not overlap.
        migration.Sql(ProductValidity(true));
        migration.Sql("""
            CREATE TRIGGER TR_ProductVersion_CommercialImmutable ON ProductVersion AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM deleted WHERE JSON_VALUE(Definition,'$.kind') IN ('commercial-combined-capture','commercial-combined-underwriting'))
            BEGIN
              IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE JSON_VALUE(d.Definition,'$.kind') IN ('commercial-combined-capture','commercial-combined-underwriting') AND i.Id IS NULL)
                THROW 51400, 'Commercial product history cannot be deleted.', 1;
              IF UPDATE(Id) OR UPDATE(ProductId) OR UPDATE(Version) OR UPDATE(ProviderId) OR UPDATE(EffectiveFrom) OR UPDATE(EffectiveTo) OR UPDATE(Definition) OR UPDATE(JsonSchemaVersion) OR UPDATE(QuestionSetVersion) OR UPDATE(CreatedAt) OR UPDATE(CreatedBy)
                THROW 51401, 'Commercial product definition is immutable.', 1;
              IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE JSON_VALUE(d.Definition,'$.kind') IN ('commercial-combined-capture','commercial-combined-underwriting') AND (i.State NOT IN ('published','retired') OR (d.State='retired' AND i.State<>'retired')))
                THROW 51402, 'Commercial product retirement is final.', 1;
            END;
            END;
            """);
    }
    private static void RemoveCommercialProductGuards(MigrationBuilder migration)
    {
        migration.Sql("DROP TRIGGER TR_ProductVersion_CommercialImmutable;");
        migration.Sql(ProductValidity(false));
    }
    private static string ProductValidity(bool commercial)
    {
        var exception = commercial ? """
            AND NOT (
              COALESCE(JSON_VALUE(i.Definition,'$.productCode'),'')='commercial-combined'
              AND COALESCE(JSON_VALUE(p.Definition,'$.productCode'),'')='commercial-combined'
              AND ((COALESCE(JSON_VALUE(i.Definition,'$.kind'),'')='commercial-combined-capture' AND COALESCE(JSON_VALUE(p.Definition,'$.kind'),'')='commercial-combined-underwriting')
                OR (COALESCE(JSON_VALUE(p.Definition,'$.kind'),'')='commercial-combined-capture' AND COALESCE(JSON_VALUE(i.Definition,'$.kind'),'')='commercial-combined-underwriting'))
              AND i.ProviderId=p.ProviderId AND i.JsonSchemaVersion=p.JsonSchemaVersion AND i.QuestionSetVersion=p.QuestionSetVersion
              AND COALESCE(JSON_VALUE(i.Definition,'$.referenceVersion'),'')='commercial-reference-1'
              AND COALESCE(JSON_VALUE(p.Definition,'$.referenceVersion'),'')='commercial-reference-1'
              AND COALESCE(JSON_VALUE(i.Definition,'$.captureFormat'),'')='commercial-combined-capture-1'
              AND COALESCE(JSON_VALUE(p.Definition,'$.captureFormat'),'')='commercial-combined-capture-1')
            """ : "";
        return $$"""
            CREATE OR ALTER TRIGGER TR_ProductVersion_PublishedValidity ON ProductVersion AFTER INSERT,UPDATE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted i JOIN ProductVersion p WITH(UPDLOCK,HOLDLOCK) ON p.ProductId=i.ProductId AND p.Id<>i.Id
              WHERE i.State='published' AND p.State='published'
              AND (p.EffectiveTo IS NULL OR i.EffectiveFrom<p.EffectiveTo) AND (i.EffectiveTo IS NULL OR p.EffectiveFrom<i.EffectiveTo)
              {{exception}})
              THROW 51001, 'Published product validity ranges overlap.', 1;
            END;
            """;
    }
}
