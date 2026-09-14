using BackOffice.Application.Parties;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ContactRulesTests
{
    private static readonly DateTimeOffset Now=new(2026,9,14,0,0,0,TimeSpan.Zero);
    private static ContactWrite Valid()=>new("Director",false,new("not-asked",false,false,Now,"Demo declaration"),FullName:" Fictional Alex van Example ");
    [Fact]
    public void FullNameIsPreservedWithoutGuessingComponents()
    {
        var result=ContactRules.Validate(Valid(),Now);
        Assert.Equal("Fictional Alex van Example",result.FullName);Assert.Null(result.FirstName);Assert.Null(result.Surname);
        Assert.Equal("FICTIONAL ALEX VAN EXAMPLE",result.NormalizedName);
        var explicitNames=ContactRules.Validate(Valid() with {FullName=null,FirstName="Fictional Alex",Surname="van Example"},Now);
        Assert.Equal(result.FullName,explicitNames.FullName);Assert.Equal("van Example",explicitNames.Surname);
        Assert.Throws<PartyValidationException>(()=>ContactRules.Validate(Valid() with {FullName=null,FirstName="Only"},Now));
    }
    [Theory]
    [InlineData("given",true,false,true)]
    [InlineData("given",false,true,true)]
    [InlineData("given",true,true,true)]
    [InlineData("given",false,false,false)]
    [InlineData("withheld",false,false,true)]
    [InlineData("withheld",true,false,false)]
    [InlineData("not-asked",false,false,true)]
    [InlineData("not-asked",false,true,false)]
    [InlineData("unknown",false,false,false)]
    public void ConsentStateAndChannelsMustAgree(string state,bool email,bool telephone,bool valid)
    {
        var input=Valid() with {MarketingConsent=new(state,email,telephone,Now,"Demo declaration")};
        if(valid)Assert.Equal(state,ContactRules.Validate(input,Now).MarketingConsent.State);
        else Assert.Throws<PartyValidationException>(()=>ContactRules.Validate(input,Now));
    }
    [Fact]
    public void DeclaredConsentTimeNormalizesToUtcWithoutInventingAuditTime()
    {
        var result=ContactRules.Validate(Valid() with {MarketingConsent=new("given",true,false,Now.ToOffset(TimeSpan.FromHours(1))," Demo form ")},Now);
        Assert.Equal(TimeSpan.Zero,result.MarketingConsent.RecordedAt.Offset);Assert.Equal(Now,result.MarketingConsent.RecordedAt);
        Assert.Equal("Demo form",result.MarketingConsent.Source);
        Assert.Throws<PartyValidationException>(()=>ContactRules.Validate(Valid() with {MarketingConsent=new("given",true,false,Now.AddMinutes(1),"Demo")},Now));
        Assert.Throws<PartyValidationException>(()=>ContactRules.Validate(Valid() with {MarketingConsent=null},Now));
    }
    [Fact]
    public void BoundedContactFieldsRejectInvalidDeclarationsWithoutEchoingThem()
    {
        var invalid=Valid() with {FullName=new string('x',201),Email="not-an-address",Telephone="bad\nphone",Role=" ",PersonId=Guid.Empty};
        var error=Assert.Throws<PartyValidationException>(()=>ContactRules.Validate(invalid,Now));
        foreach(var path in new[]{"/fullName","/email","/telephone","/role","/personId"})Assert.Contains(error.Issues,x=>x.Path==path);
        Assert.DoesNotContain("bad",error.Message);
        Assert.Throws<PartyValidationException>(()=>ContactRules.Validate(Valid() with {Email="Name <fictional@example.test>"},Now));
        Assert.Throws<PartyValidationException>(()=>ContactRules.Validate(Valid() with {Telephone=""},Now));
        Assert.Equal("fictional@example.test",ContactRules.Validate(Valid() with {Email="fictional@example.test"},Now).Email);
    }
    [Fact]
    public void PrimaryLifecyclePreservesANonemptyActiveSet()
    {
        Assert.True(ContactRules.PrimaryOnCreate(0,false));Assert.False(ContactRules.PrimaryOnCreate(1,false));Assert.True(ContactRules.PrimaryOnCreate(2,true));
        Assert.False(ContactRules.CanDemote(true,false));Assert.True(ContactRules.CanDemote(false,true));
        Assert.False(ContactRules.CanEnd(true,2));Assert.True(ContactRules.CanEnd(true,1));Assert.True(ContactRules.CanEnd(false,3));
        Assert.Throws<ArgumentOutOfRangeException>(()=>ContactRules.PrimaryOnCreate(-1,false));
    }
}
