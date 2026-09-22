using BackOffice.Application.Operations;
using Xunit;
namespace BackOffice.UnitTests;
public sealed class OperationalClaimsTests
{
    private static readonly DateTimeOffset Requested=new(2026,9,22,10,0,0,TimeSpan.Zero);
    private static ClaimsAdministratorSummary Valid()=>new("CLM-DEMO-123","claims/123",Requested.AddMinutes(1),"notified",null,null);
    [Fact] public void UnknownMoneyIsAcceptedWithoutBecomingZero()
    { var summary=Valid();ClaimsRules.Summary(summary,Requested,Requested.AddMinutes(2));Assert.Null(summary.Paid);Assert.Null(summary.Reserved); }
    [Theory][InlineData("0.00")][InlineData("12345.67")]
    public void AdvisedMoneyRetainsItsExactValue(string amount)
    { var summary=Valid() with{Paid=amount,Reserved="0.00"};ClaimsRules.Summary(summary,Requested,Requested.AddMinutes(2));Assert.Equal(amount,summary.Paid); }
    [Theory][InlineData("-1.00")][InlineData("1")][InlineData("1.001")][InlineData("1e3")][InlineData("01.00")][InlineData("10000000000000.00")]
    public void MalformedOrNegativeMoneyIsRejected(string amount)
    { Assert.Throws<ClaimsRuleException>(()=>ClaimsRules.Summary(Valid() with{Paid=amount},Requested,Requested.AddMinutes(2))); }
    [Theory][InlineData("future")][InlineData("before-request")][InlineData("wrong-currency")][InlineData("local-settlement")][InlineData("missing-reference")][InlineData("missing-event")]
    public void ProviderChronologyAndIdentityAreRequired(string change)
    { var value=Valid();value=change switch{ "future"=>value with{AsOf=Requested.AddDays(1)},"before-request"=>value with{AsOf=Requested.AddSeconds(-1)},"wrong-currency"=>value with{Currency="USD"},"local-settlement"=>value with{Status="settled-locally"},"missing-reference"=>value with{ProviderReference=" "},_=>value with{EventId=""}};
      Assert.Throws<ClaimsRuleException>(()=>ClaimsRules.Summary(value,Requested,Requested.AddMinutes(2))); }
    [Theory][InlineData("draft",null,null,true)][InlineData("logged",null,null,true)][InlineData("failed","rejected","provider-rejected",true)]
    [InlineData("failed","failed","provider-timeout",false)][InlineData("failed","failed","attempts-exhausted",false)][InlineData("queued","queued",null,false)][InlineData("handed-off","acknowledged",null,false)]
    public void OnlyDefinitiveProviderRejectionAllowsCorrection(string state,string? handoff,string? outcome,bool expected)
    { Assert.Equal(expected,ClaimsRules.CanCorrect(state,handoff,outcome)); }
}
