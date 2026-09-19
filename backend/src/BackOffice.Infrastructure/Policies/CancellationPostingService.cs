using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

internal static class CancellationPostingService
{
    private sealed class Movement
    {
        public Guid OriginalComponentId { get; set; }
        public string Code { get; set; }="";
        public int Ordinal { get; set; }
        public decimal? Amount { get; set; }
        public DateTimeOffset CoverageStartsAt { get; set; }
        public DateTimeOffset CoverageEndsAt { get; set; }
    }
    internal static async Task<ServicingPostingReceipt> Write(BackOfficeDbContext db,PolicyTransaction transaction,CancellationIssueDecision decision,CancellationToken token)
    {
        if(db.Database.CurrentTransaction is null || transaction.Kind!="cancellation")throw new InvalidOperationException("Cancellation posting requires the held issue transaction.");
        var basis=await(from o in db.Set<IssueFinancialObligation>() join t in db.Set<PolicyTransaction>() on o.TransactionId equals t.Id
            where o.PolicyId==decision.PolicyId && o.TermId==decision.BaseTermId && o.Purpose!="cancellation" orderby t.Sequence
            select o).AsNoTracking().FirstAsync(token);
        var rows=await db.Database.SqlQuery<Movement>($"SELECT OriginalComponentId,Code,Ordinal,Amount,CoverageStartsAt,CoverageEndsAt FROM CancellationExpectedReturnMovement WHERE DecisionId={decision.Id}").ToArrayAsync(token);
        if(rows.Any(x=>x.Amount is null))throw new QuoteOperationException(409,"cancellation-posting-invalid");
        var posting=ServicingPostingRules.Calculate(new("cancellation",basis.DebtorKind=="agency"?"agency":"mga",basis.Settlement,
            rows.Select(x=>new ServicingPostingMovement(x.Code,x.Ordinal,x.Amount!.Value,x.CoverageStartsAt,x.CoverageEndsAt,x.OriginalComponentId)).ToArray()));
        var period=await AccountingPeriods.HoldAsync(db,transaction.ProcessedAt,token);
        var obligation=new IssueFinancialObligation{PolicyId=decision.PolicyId,TermId=decision.BaseTermId,TransactionId=transaction.Id,Purpose="cancellation",
            AgencyId=basis.AgencyId,ClientId=basis.ClientId,RelationshipId=basis.RelationshipId,ProviderId=basis.ProviderId,AgencyTermsVersionId=basis.AgencyTermsVersionId,
            DebtorKind=basis.DebtorKind,DebtorAgencyId=basis.DebtorAgencyId,DebtorRelationshipId=basis.DebtorRelationshipId,Settlement=basis.Settlement,
            Premium=posting.Premium,Tax=posting.Tax,Fee=posting.Fee,Commission=posting.Commission,FeeShare=posting.FeeShare,GrossDue=posting.GrossDue,
            InvoiceDue=posting.InvoiceDue,NetDue=posting.NetDue,BrokerPayable=posting.BrokerPayable,TermsSnapshotJson=basis.TermsSnapshotJson,
            CreatedAt=transaction.ProcessedAt,CreatedBy=transaction.CreatedBy};
        db.Add(obligation);await db.SaveChangesAsync(token);
        var components=posting.Movements.Select(x=>new IssueFinancialComponent{ObligationId=obligation.Id,TransactionId=transaction.Id,Code=x.Code,Ordinal=x.Ordinal,
            Amount=x.Amount,CoverageStartsAt=x.StartsAt,CoverageEndsAt=x.EndsAt,OriginalComponentId=x.OriginalComponentId,
            CreatedAt=transaction.ProcessedAt,CreatedBy=transaction.CreatedBy}).ToArray();
        db.AddRange(components);await db.SaveChangesAsync(token);
        var journal=new Journal{TransactionId=transaction.Id,ObligationId=obligation.Id,Purpose="cancellation",AccountingPeriodId=period.PeriodId,
            PostingDate=period.PostingDate,CreatedAt=transaction.ProcessedAt,CreatedBy=transaction.CreatedBy};
        db.Add(journal);await db.SaveChangesAsync(token);
        foreach(var line in posting.Lines)
            db.Add(new JournalLine{JournalId=journal.Id,TransactionId=transaction.Id,SourceComponentId=components.Single(x=>x.Code==line.Code&&x.Ordinal==line.Ordinal).Id,
                ComponentCode=line.Code,AccountCode=line.AccountCode,Debit=line.Debit,Credit=line.Credit,CoverageStartsAt=line.StartsAt,CoverageEndsAt=line.EndsAt,
                PartyKind=line.AccountCode=="fee-income"?"internal":line.AccountCode=="insurer-payable"?"provider":line.AccountCode=="relationship-receivable"?"relationship":"agency",
                PartyId=line.AccountCode=="fee-income"?null:line.AccountCode=="insurer-payable"?basis.ProviderId:line.AccountCode=="relationship-receivable"?basis.RelationshipId:basis.AgencyId,
                CreatedAt=transaction.ProcessedAt,CreatedBy=transaction.CreatedBy});
        await db.SaveChangesAsync(token);journal.PostedAt=transaction.ProcessedAt;await db.SaveChangesAsync(token);
        return new(obligation.Id,journal.Id,period.PeriodId,period.PostingDate,posting.GrossDue,posting.InvoiceDue,posting.NetDue,posting.BrokerPayable);
    }
}
