using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackOffice.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CancellationPostingCalculation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION dbo.CancellationReturnAmount(@amount decimal(15,2),@startsAt datetimeoffset,@endsAt datetimeoffset,
                    @effectiveAt datetimeoffset,@code varchar(30)) RETURNS decimal(15,2) AS BEGIN
                IF @amount IS NULL OR @startsAt IS NULL OR @endsAt IS NULL OR @effectiveAt IS NULL OR @code IS NULL
                  OR @code COLLATE Latin1_General_100_BIN2 NOT IN ('premium','tax','commission','fee','fee-share')
                  OR DATEPART(TZOFFSET,@startsAt)<>0 OR DATEPART(TZOFFSET,@endsAt)<>0 OR DATEPART(TZOFFSET,@effectiveAt)<>0
                  OR @startsAt>=@endsAt OR @effectiveAt<@startsAt RETURN NULL;
                DECLARE @start date=CONVERT(date,@startsAt AT TIME ZONE 'GMT Standard Time'),
                    @end date=CONVERT(date,@endsAt AT TIME ZONE 'GMT Standard Time'),
                    @effective date=CONVERT(date,@effectiveAt AT TIME ZONE 'GMT Standard Time');
                DECLARE @days int=DATEDIFF(day,@start,@end),@remaining int=DATEDIFF(day,@effective,@end);
                IF @days<=0 RETURN NULL;
                IF @remaining<=0 OR @code IN ('fee','fee-share') RETURN 0;
                -- Integer pennies and remainder comparison avoid rounding a
                -- near-half intermediate decimal before the actual money step.
                DECLARE @numerator decimal(38,0)=CONVERT(decimal(28,0),ABS(@amount)*100)*@remaining;
                DECLARE @remainder decimal(38,0)=@numerator%@days;
                DECLARE @pennies decimal(38,0)=(@numerator-@remainder)/@days;
                IF @remainder*2>=@days SET @pennies=@pennies+1;
                RETURN CONVERT(decimal(15,2),-SIGN(@amount)*@pennies/100);
                END;
                """);
            migrationBuilder.Sql("""
                CREATE VIEW dbo.CancellationExpectedReturnMovement AS
                SELECT d.Id AS DecisionId,c.Id AS OriginalComponentId,c.Code,
                  CONVERT(int,ROW_NUMBER() OVER(PARTITION BY d.Id,c.Code ORDER BY c.CoverageStartsAt,
                    CONVERT(varchar(36),c.Id) COLLATE Latin1_General_100_BIN2)) AS Ordinal,
                  dbo.CancellationReturnAmount(c.Amount,c.CoverageStartsAt,c.CoverageEndsAt,d.EffectiveAt,c.Code) AS Amount,
                  d.EffectiveAt AS CoverageStartsAt,term.EndsAt AS CoverageEndsAt
                FROM CancellationIssueDecision d JOIN PolicyTerm term ON term.Id=d.BaseTermId AND term.PolicyId=d.PolicyId
                  JOIN IssueFinancialObligation o ON o.PolicyId=d.PolicyId AND o.TermId=d.BaseTermId AND o.Purpose<>'cancellation'
                  JOIN PolicyTransaction t ON t.Id=o.TransactionId AND t.PolicyId=d.PolicyId AND t.TermId=d.BaseTermId
                  JOIN Journal j ON j.ObligationId=o.Id AND j.TransactionId=t.Id AND j.PostedAt IS NOT NULL
                  JOIN IssueFinancialComponent c ON c.ObligationId=o.Id AND c.TransactionId=t.Id
                WHERE t.ProcessedAt<=d.CreatedAt;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM PolicyTransaction WHERE Kind='cancellation') THROW 51825,'Cancellation posting history cannot be downgraded.',1;");
            migrationBuilder.Sql("DROP VIEW dbo.CancellationExpectedReturnMovement;");
            migrationBuilder.Sql("DROP FUNCTION dbo.CancellationReturnAmount;");

        }
    }
}
