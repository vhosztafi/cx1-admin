using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class ServicingDraftStorage
{
    private static void AddServicingGuards(MigrationBuilder migration)
    {
        migration.Sql("""
            CREATE TRIGGER TR_ServicingRevision_AppendOnly ON ServicingRevision AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM deleted) THROW 51210,'Servicing revisions are append-only.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingRevision_Source ON ServicingRevision AFTER INSERT AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM inserted r JOIN ServicingDraft d ON d.Id=r.DraftId
              WHERE d.State<>'draft' OR r.ContentHash<>HASHBYTES('SHA2_256',CONVERT(varchar(max),r.ProposalJson COLLATE Latin1_General_100_BIN2_UTF8))
                OR COALESCE(TRY_CONVERT(uniqueidentifier,JSON_VALUE(r.ProposalJson,'$.baseVersionId')),'00000000-0000-0000-0000-000000000000')<>d.BaseVersionId)
              THROW 51211,'Revision requires its active draft, exact base and content hash.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_ServicingDraft_Owner ON ServicingDraft AFTER UPDATE,DELETE AS BEGIN
            SET NOCOUNT ON;
            IF EXISTS(SELECT 1 FROM deleted) AND (NOT EXISTS(SELECT 1 FROM inserted) OR UPDATE(Id) OR UPDATE(PolicyId) OR UPDATE(BaseTermId) OR UPDATE(BaseVersionId) OR UPDATE(Kind) OR UPDATE(CreatedAt) OR UPDATE(CreatedBy))
              THROW 51212,'Servicing draft provenance is immutable.',1;
            IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE d.State='abandoned' AND (i.State<>d.State OR ISNULL(i.CurrentRevisionId,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.CurrentRevisionId,'00000000-0000-0000-0000-000000000000')))
              THROW 51213,'Abandoned drafts cannot be reopened or revised.',1;
            IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id JOIN ServicingRevision old ON old.Id=d.CurrentRevisionId
              LEFT JOIN ServicingRevision newer ON newer.Id=i.CurrentRevisionId
              WHERE newer.Id IS NULL OR newer.Sequence<old.Sequence)
              THROW 51214,'Current revision cannot move backwards.',1;
            END;
            """);
    }
}
