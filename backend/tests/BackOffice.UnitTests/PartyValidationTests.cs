using BackOffice.Application.Parties;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class PartyValidationTests
{
    private static ClientWrite Valid() => new("  Fictional O'Brien & Sons  ","limited-company",new AddressWrite("1 Fictional Road","Sheffield","s1 1aa","GB"),"demo0001");

    [Fact]
    public void DeclaredIdentityIsPreservedSeparatelyFromMatchingKey()
    {
        var result=ClientIdentity.Validate(Valid());
        Assert.Equal("Fictional O'Brien & Sons",result.LegalName);
        Assert.Equal("FICTIONAL O'BRIEN & SONS",result.NormalizedName);
        Assert.Equal("DEMO0001",result.CompanyNumber);Assert.Equal("S1 1AA",result.Address.Postcode);
        Assert.Null(result.Address.Line2);Assert.Null(result.Address.County);
        Assert.Equal("ABC TRADERS",ClientIdentity.NormalizeName("ＡＢＣ   Traders"));
        Assert.NotEqual(ClientIdentity.NormalizeName("A-B Traders"),ClientIdentity.NormalizeName("AB Traders"));
    }

    [Theory]
    [InlineData("sole-trader")]
    [InlineData("partnership")]
    [InlineData("limited-company")]
    [InlineData("llp")]
    public void SupportedIdentityDoesNotInventMissingCompanyNumber(string kind)
    {
        var result=ClientIdentity.Validate(Valid() with {EntityType=kind,CompanyNumber=null});
        Assert.Equal(kind,result.EntityType);Assert.Null(result.CompanyNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Fictional\nTraders")]
    public void InvalidLegalNameHasSafeFieldErrors(string? name)
    {
        var error=Assert.Throws<PartyValidationException>(() => ClientIdentity.Validate(Valid() with {LegalName=name}));
        Assert.Contains(error.Issues,x => x.Path=="/legalName");
        Assert.DoesNotContain("Fictional",error.Message);
    }

    [Fact]
    public void LengthCountryAndMissingAddressErrorsAreReportedWithoutInputEcho()
    {
        var input=Valid() with {LegalName=new string('x',201),EntityType="invented",CompanyNumber=new string('s',31),Address=new AddressWrite("","",new string('p',21),"US")};
        var error=Assert.Throws<PartyValidationException>(() => ClientIdentity.Validate(input));
        foreach(var path in new[] {"/legalName","/entityType","/companyNumber","/address/line1","/address/town","/address/postcode","/address/country"})
            Assert.Contains(error.Issues,x => x.Path==path);
        Assert.Throws<PartyValidationException>(() => ClientIdentity.Validate(Valid() with {Address=null}));
        Assert.Throws<PartyValidationException>(() => ClientIdentity.Validate(Valid() with {CompanyNumber=" "}));
        Assert.Throws<PartyValidationException>(() => ClientIdentity.Validate(Valid() with {Address=Valid().Address! with {Line2=""}}));
    }
}
