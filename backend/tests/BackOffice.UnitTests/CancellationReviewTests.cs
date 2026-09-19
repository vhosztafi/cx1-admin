using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class CancellationReviewTests
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
    private static DateTimeOffset Day(int year, int month, int day) => new(TimeZoneInfo.ConvertTimeToUtc(new DateTime(year, month, day), London));
    private static readonly DateTimeOffset Start = Day(2026, 1, 1), End = Day(2027, 1, 1);
    private static CancellationPostedComponent Row(string code, decimal amount, DateTimeOffset? start = null) =>
        new(Guid.NewGuid(), code, amount, start ?? Start, End, null);
    private static CancellationPostedComponent[] Original() =>
        [Row("premium", 1200), Row("tax", 144), Row("commission", 120), Row("fee", 35), Row("fee-share", 7)];
    private static CancellationReturnPreview Preview(CancellationPostedComponent[] rows, DateTimeOffset effective,
        string collector = "agency", string settlement = "net-remittance") =>
        CancellationReviewRules.Calculate(Start, End, effective, collector, settlement, rows);

    [Fact]
    public void UnadjustedReturnUsesPostedComponentsAndRetainsBothFeeAndShare()
    {
        var result = Preview(Original(), Day(2026, 9, 15));
        Assert.Equal(-355.07m, result.Posting.Premium);
        Assert.Equal(-42.61m, result.Posting.Tax);
        Assert.Equal(-35.51m, result.Posting.Commission);
        Assert.Equal(-362.17m, result.Posting.InvoiceDue);
        Assert.Equal(35m, result.RetainedFee);
        Assert.Equal(7m, result.RetainedFeeShare);
        Assert.Equal(0m, result.Posting.Fee);
        Assert.Equal(0m, result.Posting.FeeShare);
    }

    [Fact]
    public void PositiveAdjustmentIsReturnedOverItsOwnPostedInterval()
    {
        var at = Day(2026, 9, 15);
        var result = Preview([..Original(), Row("premium",177.53m,at), Row("tax",21.30m,at), Row("commission",17.75m,at), Row("fee",15m,at)], Day(2026,10,1));
        Assert.Equal(-453.70m,result.Posting.Premium);
        Assert.Equal(-54.44m,result.Posting.Tax);
        Assert.Equal(-45.37m,result.Posting.Commission);
        Assert.Equal(-462.77m,result.Posting.InvoiceDue);
        Assert.Equal(50m,result.RetainedFee);
    }

    [Fact]
    public void MixedPositiveAndNegativeAdjustmentsProduce32696CreditWithUniqueLineage()
    {
        var at=Day(2026,9,15);var second=Day(2026,10,1);
        var rows=new[]{Original(),new[]{Row("premium",177.53m,at),Row("tax",21.30m,at),Row("commission",17.75m,at),Row("fee",15m,at)},
            new[]{Row("premium",-75.62m,second),Row("tax",-9.07m,second),Row("commission",-7.56m,second)}}.SelectMany(x=>x).ToArray();
        var result=Preview(rows,Day(2026,10,15));
        Assert.Equal(-320.55m,result.Posting.Premium);Assert.Equal(-38.46m,result.Posting.Tax);
        Assert.Equal(-32.05m,result.Posting.Commission);Assert.Equal(-326.96m,result.Posting.InvoiceDue);
        Assert.Contains(result.Posting.Movements,x=>x.Code=="premium"&&x.Amount==64.11m);
        Assert.Equal(rows.Length,result.Posting.Movements.Count);
        Assert.Equal(rows.Select(x=>x.Id).Order(),result.Posting.Movements.Select(x=>x.OriginalComponentId!.Value).Order());
    }

    [Theory]
    [InlineData("agency","separate-payment")]
    [InlineData("mga","net-remittance")]
    public void SeparateCollectionKeepsGrossDebtorCreditAndBrokerClawbackDistinct(string collector,string settlement)
    {
        var result=Preview(Original(),Day(2026,9,15),collector,settlement);
        Assert.Equal(-397.68m,result.Posting.InvoiceDue);Assert.Equal(-35.51m,result.Posting.BrokerPayable);
        Assert.Equal(-362.17m,result.Posting.NetDue);
    }

    [Fact]
    public void CalendarDaysHandleLeapYearAndBothDstChanges()
    {
        var start=Day(2024,1,1);var end=Day(2025,1,1);
        var rows=Original().Select(x=>x with{StartsAt=start,EndsAt=end,Amount=x.Code=="premium"?366m:x.Amount}).ToArray();
        foreach(var (date,amount) in new[]{(Day(2024,3,31),-276m),(Day(2024,10,27),-66m)})
            Assert.Equal(amount,CancellationReviewRules.Calculate(start,end,date,"agency","net-remittance",rows).Posting.Premium);
        Assert.Equal(-1200m,Preview(Original(),Start).Posting.Premium);
    }

    [Fact]
    public void RejectsDuplicateAlreadyReturnedUnknownFractionalAndOutOfTermSources()
    {
        var rows=Original();var at=Day(2026,9,15);
        void Reject(CancellationPostedComponent[] source)=>Assert.Throws<ArgumentException>(()=>Preview(source,at));
        Reject([..rows,rows[0]]);
        foreach(var bad in new[]{rows[0] with{Id=Guid.Empty},rows[0] with{OriginalComponentId=Guid.NewGuid()},
            rows[0] with{Code="cash"},rows[0] with{Amount=1.001m},rows[0] with{StartsAt=End},
            rows[0] with{StartsAt=Start.AddDays(-1)},rows[0] with{EndsAt=End.AddDays(1)},rows[0] with{StartsAt=Start.ToOffset(TimeSpan.FromHours(1))}})
            Reject([bad,..rows.Skip(1)]);
        Reject(rows.Where(x=>x.Code!="premium").ToArray());
        Assert.Throws<ArgumentException>(()=>Preview(rows,End));
        Assert.Throws<ArgumentException>(()=>Preview(rows,Start.AddTicks(-1)));
        Assert.Throws<ArgumentException>(()=>CancellationReviewRules.Calculate(Start,End,at,"agency","net-remittance",rows,new HashSet<Guid>{rows[0].Id}));
    }

    [Fact]
    public void RejectsFutureSlicesRatherThanSilentlyRebasingThem()
    {
        Assert.Throws<ArgumentException>(()=>Preview([..Original(),Row("premium",50,Day(2026,10,1))],Day(2026,9,15)));
    }

    [Theory]
    [InlineData("insured-request",false,false)]
    [InlineData("trade-ceased",false,false)]
    [InlineData("non-payment",true,true)]
    [InlineData("non-disclosure",true,true)]
    [InlineData("insurer-instruction",true,true)]
    public void EveryReasonRequiresCurrentGrantAndHigherRiskReasonsRequireDistinctSenior(string code,bool senior,bool distinct)
    {
        var requester=Guid.NewGuid();var approver=Guid.NewGuid();
        Assert.False(CancellationDecisionRules.CanApprove(code,requester,approver,false,true));
        Assert.Equal(!senior,CancellationDecisionRules.CanApprove(code,requester,approver,true,false));
        Assert.Equal(!distinct,CancellationDecisionRules.CanApprove(code,requester,requester,true,true));
        Assert.True(CancellationDecisionRules.CanApprove(code,requester,approver,true,true));
    }

    [Fact]
    public void NoticeRequiresAcceptedEvidenceAndActualDeliveryAcrossDstBoundary()
    {
        var at=Day(2026,10,26);var now=Day(2026,10,19).AddHours(12);
        var notice=new CancellationNoticeEvidence(Guid.NewGuid(),"cancellation-notice",true,now);
        CancellationDecisionAssessment Assess(CancellationNoticeEvidence row,DateTimeOffset effective)=>
            CancellationDecisionRules.Assess("non-payment",Start,End,Start,effective,now,false,false,[row]);
        Assert.Empty(Assess(notice,at).Blockers);
        Assert.Equal(new DateOnly(2026,10,26),Assess(notice,at).NoticeEffectiveFrom);
        Assert.Contains("notice-period-incomplete",Assess(notice,Day(2026,10,25)).Blockers);
        Assert.Contains("cancellation-notice-delivery-required",Assess(notice with{DeliveredAt=null},at).Blockers);
        Assert.Contains("cancellation-notice-delivery-required",Assess(notice with{DeliveredAt=now.AddSeconds(1)},at).Blockers);
        Assert.Contains("evidence-required:cancellation-notice",Assess(notice with{Accepted=false},at).Blockers);
    }

    [Fact]
    public void NonDisclosureNeedsBothEvidencePurposesAndFutureSlicesAndLaterTermsStayBlocked()
    {
        var now=Day(2026,9,15);var requested=Day(2026,10,15);var boundary=Day(2026,11,1);
        var result=CancellationDecisionRules.Assess("non-disclosure",Start,End,boundary,requested,now,true,false,
            [new(Guid.NewGuid(),"cancellation-notice",true,now)]);
        Assert.Contains("evidence-required:cancellation-reason",result.Blockers);
        Assert.Contains("later-term-issued",result.Blockers);
        Assert.Contains("before-latest-issued-slice",result.Blockers);
        Assert.Equal(boundary,result.SupportedFrom);
        result=CancellationDecisionRules.Assess("insured-request",Start,End,Start,now.AddDays(-1),now,false,false,
            [new(Guid.NewGuid(),"cancellation-request",true,null)]);
        Assert.Contains("cancellation-backdate-authority-required",result.Blockers);
    }

    [Fact]
    public void UnsupportedOrAmbiguousCancellationConfigurationCannotDefaultToAuthority()
    {
        var config=CancellationConfiguration.Parse(CancellationConfiguration.DemoJson);
        Assert.NotNull(config);Assert.Contains("demo-senior-1",config.SeniorAuthorityVersions);
        foreach(var invalid in new[]{"{}","null","[]",CancellationConfiguration.DemoJson.Replace("demo-servicing-1","unimplemented-rule-2"),
            CancellationConfiguration.DemoJson.Replace("\"demo\":true","\"demo\":true,\"demo\":false"),
            CancellationConfiguration.DemoJson.Replace("\"demo\":true","\"demo\":true,\"approveAnyone\":true"),
            CancellationConfiguration.DemoJson.Replace("\"seniorAuthorityVersions\":[\"demo-senior-1\"]","\"seniorAuthorityVersions\":[\"unpermitted-authority\"]")})
            Assert.Null(CancellationConfiguration.Parse(invalid));
    }
}
