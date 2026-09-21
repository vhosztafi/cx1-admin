using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaskReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "TaskReferenceSequence");
            // Preserve existing friendly references if the sequence is restored
            // after a downgrade; immutable task identities are never rewritten.
            migrationBuilder.Sql("""
                DECLARE @last bigint=(SELECT COALESCE(MAX(TRY_CONVERT(bigint,SUBSTRING(Reference,5,36))),0) FROM OperationalTask WHERE Reference LIKE 'TSK-%');
                IF @last=9223372036854775807 THROW 51984,'Task reference range is exhausted.',1;
                DECLARE @next bigint=CASE WHEN @last>0 THEN @last+1 ELSE 1 END;
                DECLARE @statement nvarchar(100)=N'ALTER SEQUENCE TaskReferenceSequence RESTART WITH '+CONVERT(nvarchar(30),@next);
                EXEC(@statement);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSequence(
                name: "TaskReferenceSequence");
        }
    }
}
