using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.Infrastructure.Persistence.Migrations;

public partial class OperationalTasks
{
    private static void AddOperationalGuards(MigrationBuilder migration)
    {
        foreach (var table in new[] { "OperationalSubject", "OperationalTaskEvent", "OperationalTaskComment" })
            migration.Sql($"CREATE TRIGGER TR_{table}_Immutable ON [{table}] AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM deleted) THROW 51980,'Operational provenance is append-only.',1; END;");
        migration.Sql("""
            CREATE TRIGGER TR_OperationalTask_Identity ON OperationalTask AFTER UPDATE,DELETE AS
            BEGIN
              SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id
                WHERE i.Id IS NULL OR i.SubjectId<>d.SubjectId OR i.Reference<>d.Reference COLLATE Latin1_General_100_BIN2
                  OR i.CreatedBy<>d.CreatedBy OR i.CreatedAt<>d.CreatedAt OR i.EventSequence<>d.EventSequence+1)
                THROW 51981,'Task identity and creator are immutable; every change advances the event sequence.',1;
            END;
            """);
        migration.Sql("""
            CREATE TRIGGER TR_OperationalTaskChecklist_Identity ON OperationalTaskChecklist AFTER UPDATE,DELETE AS
            BEGIN
              SET NOCOUNT ON;
              IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id
                WHERE i.Id IS NULL OR i.TaskId<>d.TaskId OR i.Ordinal<>d.Ordinal
                  OR i.Label<>d.Label COLLATE Latin1_General_100_BIN2 OR i.Required<>d.Required
                  OR i.CreatedBy<>d.CreatedBy OR i.CreatedAt<>d.CreatedAt)
                THROW 51982,'Task checklist definition is immutable.',1;
            END;
            """);
    }
}
