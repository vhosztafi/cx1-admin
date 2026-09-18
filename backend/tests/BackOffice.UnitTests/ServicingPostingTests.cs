using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ServicingPostingTests
{
    private static readonly DateTimeOffset Start=new(2026,9,14,23,0,0,TimeSpan.Zero),Second=new(2026,9,30,23,0,0,TimeSpan.Zero),End=new(2027,1,1,0,0,0,TimeSpan.Zero);
    private static ServicingPostingMovement[] Mixed()=>[
        new("premium",1,177.53m,Start,End),new("tax",1,21.30m,Start,End),new("commission",1,17.75m,Start,End),
        new("premium",2,-75.62m,Second,End),new("tax",2,-9.07m,Second,End),new("commission",2,-7.56m,Second,End),
        new("fee",1,15m,Start,End),new("fee-share",1,0m,Start,End)];

    [Fact]
    public void MixedDatedMovementsRetainIntervalsAndBalanceIndependentWorkedTotals()
    {
        var result=ServicingPostingRules.Calculate(new("adjustment","agency","net-remittance",Mixed()));
        Assert.Equal(101.91m,result.Premium);Assert.Equal(12.23m,result.Tax);Assert.Equal(10.19m,result.Commission);
        Assert.Equal(129.14m,result.GrossDue);Assert.Equal(118.95m,result.InvoiceDue);Assert.Equal(118.95m,result.NetDue);Assert.Equal(0m,result.BrokerPayable);
        Assert.Equal(14,result.Lines.Count);Assert.Equal(result.Lines.Sum(x=>x.Debit),result.Lines.Sum(x=>x.Credit));
        var negative=result.Lines.Where(x=>x.Code=="premium"&&x.Ordinal==2).ToArray();
        Assert.Contains(negative,x=>x.AccountCode=="insurer-payable"&&x.Debit==75.62m&&x.Credit==0);
        Assert.Contains(negative,x=>x.AccountCode=="agency-receivable"&&x.Credit==75.62m&&x.Debit==0);
        Assert.All(negative,x=>Assert.Equal(Second,x.StartsAt));Assert.All(result.Lines,x=>Assert.True(x.Debit>=0 && x.Credit>=0));
    }

    [Theory]
    [InlineData("agency","separate-payment","agency-receivable")]
    [InlineData("mga","net-remittance","relationship-receivable")]
    public void SeparateRemunerationAndDirectCollectionRetainActualDebtor(string collector,string settlement,string account)
    {
        var result=ServicingPostingRules.Calculate(new("adjustment",collector,settlement,Mixed()));
        Assert.Equal(129.14m,result.InvoiceDue);Assert.Equal(10.19m,result.BrokerPayable);Assert.Equal("separate-payment",result.Settlement);
        Assert.Equal(129.14m,result.Lines.Where(x=>x.AccountCode==account).Sum(x=>x.Debit-x.Credit));
        Assert.Equal(10.19m,result.Lines.Where(x=>x.AccountCode=="broker-remuneration-payable").Sum(x=>x.Credit-x.Debit));
    }

    [Fact]
    public void CancellationReversesBothPositiveAndNegativeOriginalMovementsWithoutClaimingCash()
    {
        var start=new DateTimeOffset(2026,10,14,23,0,0,TimeSpan.Zero);var movements=new List<ServicingPostingMovement>();
        foreach(var item in new[]{("premium",1,-256.44m),("tax",1,-30.77m),("commission",1,-25.64m),("premium",2,-128.22m),("tax",2,-15.38m),("commission",2,-12.82m),("premium",3,64.11m),("tax",3,7.69m),("commission",3,6.41m)})
            movements.Add(new(item.Item1,item.Item2,item.Item3,start,End,Guid.NewGuid()));
        movements.Add(new("fee",1,0,start,End));movements.Add(new("fee-share",1,0,start,End));
        var result=ServicingPostingRules.Calculate(new("cancellation","agency","net-remittance",movements));
        Assert.Equal(-320.55m,result.Premium);Assert.Equal(-38.46m,result.Tax);Assert.Equal(-32.05m,result.Commission);
        Assert.Equal(-326.96m,result.InvoiceDue);Assert.Equal(-359.01m,result.GrossDue);Assert.Equal(18,result.Lines.Count);
        Assert.All(result.Lines,x=>Assert.True(x.Debit>0 && x.Credit==0 || x.Credit>0 && x.Debit==0));
    }

    [Fact]
    public void RejectsMissingCodesDuplicatesInvalidIntervalsAndUnboundedOrFractionalAmounts()
    {
        void Reject(ServicingPostingMovement[] rows)=>Assert.Throws<ArgumentException>(()=>ServicingPostingRules.Calculate(new("adjustment","agency","net-remittance",rows)));
        Reject(Mixed().Where(x=>x.Code!="fee").ToArray());Reject([..Mixed(),Mixed()[0]]);
        foreach(var bad in new[]{Mixed()[0] with{Code="cash"},Mixed()[0] with{Ordinal=0},Mixed()[0] with{Amount=0.001m},Mixed()[0] with{Amount=10000000000000m},Mixed()[0] with{StartsAt=End},Mixed()[0] with{StartsAt=Start.ToOffset(TimeSpan.FromHours(1))},Mixed()[0] with{OriginalComponentId=Guid.NewGuid()}})
            Reject([bad,..Mixed().Skip(1)]);
        Assert.Throws<ArgumentException>(()=>ServicingPostingRules.Calculate(new("cancellation","agency","net-remittance",Mixed())));
        Assert.Throws<ArgumentException>(()=>ServicingPostingRules.Calculate(new("renewal","agency","net-remittance",Mixed())));
    }

    [Fact]
    public void SnapshotsCallerMovementsAndRejectsRepeatedOriginalReturnWithinOnePosting()
    {
        var rows=Mixed();var result=ServicingPostingRules.Calculate(new("adjustment","agency","net-remittance",rows));rows[0]=rows[0] with{Amount=1};
        Assert.Equal(177.53m,result.Movements[0].Amount);
        var id=Guid.NewGuid();var returns=Mixed().Select(x=>x with{OriginalComponentId=x.Amount==0?null:id}).ToArray();
        Assert.Throws<ArgumentException>(()=>ServicingPostingRules.Calculate(new("cancellation","agency","net-remittance",returns)));
    }
}
