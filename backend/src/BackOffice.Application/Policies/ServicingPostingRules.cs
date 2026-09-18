namespace BackOffice.Application.Policies;

public sealed record ServicingPostingMovement(string Code,int Ordinal,decimal Amount,DateTimeOffset StartsAt,DateTimeOffset EndsAt,Guid? OriginalComponentId=null);
public sealed record ServicingPostingInput(string Purpose,string Collector,string Settlement,IReadOnlyList<ServicingPostingMovement> Movements);
public sealed record ServicingPostingLine(string Code,int Ordinal,string AccountCode,decimal Debit,decimal Credit,DateTimeOffset StartsAt,DateTimeOffset EndsAt);
public sealed record ServicingPosting(decimal Premium,decimal Tax,decimal Fee,decimal Commission,decimal FeeShare,decimal GrossDue,decimal InvoiceDue,
    decimal NetDue,decimal BrokerPayable,string DebtorKind,string Settlement,IReadOnlyList<ServicingPostingMovement> Movements,IReadOnlyList<ServicingPostingLine> Lines);

public static class ServicingPostingRules
{
    private static readonly string[] Codes=["premium","tax","fee","commission","fee-share"];

    // The owning transaction supplies server-derived movements. Original source,
    // owner and cumulative-return checks additionally require the locked ledger.
    public static ServicingPosting Calculate(ServicingPostingInput input)
    {
        if(input is null || input.Purpose is not("adjustment" or "renewal" or "cancellation") || input.Collector is not("agency" or "mga") ||
            input.Settlement is not("net-remittance" or "separate-payment") || input.Movements is null || input.Movements.Count is <5 or >1505)
            throw Invalid();
        var rows=input.Movements.ToArray();var keys=new HashSet<(string,int)>();var originals=new HashSet<Guid>();
        foreach(var row in rows)
        {
            if(row is null || !Codes.Contains(row.Code,StringComparer.Ordinal) || row.Ordinal is <1 or >1000 || !keys.Add((row.Code,row.Ordinal)) ||
                row.StartsAt.Offset!=TimeSpan.Zero || row.EndsAt.Offset!=TimeSpan.Zero || row.StartsAt>=row.EndsAt ||
                row.Amount is >IssuePostingRules.MaximumAmount or < -IssuePostingRules.MaximumAmount || decimal.Round(row.Amount,2)!=row.Amount ||
                row.OriginalComponentId==Guid.Empty || row.OriginalComponentId is {} original && !originals.Add(original))throw Invalid();
            if(input.Purpose=="cancellation" ? row.Amount!=0 && row.OriginalComponentId is null : row.OriginalComponentId is not null)throw Invalid();
            if(input.Purpose=="renewal" && row.Amount<0)throw Invalid();
        }
        if(Codes.Any(code=>!rows.Any(x=>x.Code==code)))throw Invalid();
        decimal Sum(string code)=>rows.Where(x=>x.Code==code).Sum(x=>x.Amount);
        var premium=Sum("premium");var tax=Sum("tax");var fee=Sum("fee");var commission=Sum("commission");var share=Sum("fee-share");
        var gross=premium+tax+fee;var net=gross-commission-share;var netted=input.Collector=="agency" && input.Settlement=="net-remittance";
        var invoice=netted?net:gross;var remuneration=netted?0:commission+share;
        foreach(var amount in new[]{premium,tax,fee,commission,share,gross,net,invoice,remuneration})
            if(amount is >IssuePostingRules.MaximumAmount or < -IssuePostingRules.MaximumAmount)throw Invalid();
        var debtor=input.Collector=="agency"?"agency-receivable":"relationship-receivable";
        var payable=netted?debtor:"broker-remuneration-payable";
        var lines=new List<ServicingPostingLine>();
        foreach(var row in rows)
        {
            if(row.Amount==0)continue;
            var debit=row.Code is "premium" or "tax" or "fee"?debtor:row.Code=="commission"?"insurer-payable":"fee-income";
            var credit=row.Code is "premium" or "tax"?"insurer-payable":row.Code=="fee"?"fee-income":payable;
            if(row.Amount<0)(debit,credit)=(credit,debit);
            var amount=decimal.Abs(row.Amount);
            lines.Add(new(row.Code,row.Ordinal,debit,amount,0,row.StartsAt,row.EndsAt));
            lines.Add(new(row.Code,row.Ordinal,credit,0,amount,row.StartsAt,row.EndsAt));
        }
        return new(premium,tax,fee,commission,share,gross,invoice,net,remuneration,input.Collector=="agency"?"agency":"relationship",
            netted?"net-remittance":"separate-payment",Array.AsReadOnly(rows),lines.AsReadOnly());
    }

    private static ArgumentException Invalid()=>new("Servicing posting requires bounded exact signed components with unique owned lineage and UTC intervals.");
}
