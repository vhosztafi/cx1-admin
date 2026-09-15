using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteCanonicalJsonTests
{
    private static readonly QuoteVersionPins Pins = new(
        Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001"),
        Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002"), "1.0", "questions-1", "references-1");
    private static CanonicalQuoteInput Canonical(string json) => QuoteCanonicalJson.Create(json, Pins);

    [Fact]
    public void PropertyOrderWhitespaceAndEscapedStringsDoNotCreateRevisions()
    {
        var left = Canonical("""{ "b": {"z":true,"a":"café"}, "a":"hello" }""");
        var right = Canonical("""{"a":"\u0068ello","b":{"a":"caf\u00e9","z":true}}""");
        Assert.Equal(left, right);
        Assert.Equal("""{"a":"hello","b":{"a":"café","z":true}}""", left.Json);
        Assert.Matches("^[a-f0-9]{64}$", left.ContentHash);
    }

    [Theory]
    [InlineData("1", "1.00")]
    [InlineData("1e3", "1000")]
    [InlineData("-0", "0.0")]
    [InlineData("0.000125", "125e-6")]
    [InlineData("-120.50", "-1205e-1")]
    [InlineData("9007199254740993", "9007199254740993.0")]
    [InlineData("123456789012345678901234567890.1", "1234567890123456789012345678901e-1")]
    public void NumbersAreNormalizedExactlyWithoutFloatingPointRounding(string left, string right)
    {
        var result = Canonical("{\"n\":" + left + "}");
        Assert.Equal(result, Canonical("{\"n\":" + right + "}"));
        Assert.Equal(result, Canonical(result.Json));
    }

    [Theory]
    [InlineData("{}", "{\"flag\":false}")]
    [InlineData("{\"value\":0}", "{\"value\":false}")]
    [InlineData("{\"value\":1}", "{\"value\":\"1\"}")]
    [InlineData("{\"rows\":[]}", "{}")]
    [InlineData("{\"rows\":[1,2]}", "{\"rows\":[2,1]}")]
    [InlineData("{\"value\":9007199254740992}", "{\"value\":9007199254740993}")]
    [InlineData("{\"name\":\"Alex\"}", "{\"name\":\" Alex \"}")]
    [InlineData("{\"termIntent\":{\"localStartDate\":\"2026-09-15\"}}", "{\"termIntent\":{\"localStartDate\":\"2026-09-16\"}}")]
    public void MaterialDifferencesPreserveTheirIdentity(string left, string right)
    {
        Assert.NotEqual(Canonical(left).ContentHash, Canonical(right).ContentHash);
    }

    [Fact]
    public void EveryTrustedVersionPinChangesTheContentHash()
    {
        var original = Canonical("{}").ContentHash;
        foreach (var changed in new[] {
            Pins with { ProductVersionId = Guid.NewGuid() }, Pins with { AgencyTermsVersionId = Guid.NewGuid() },
            Pins with { SchemaVersion = "2.0" }, Pins with { QuestionSetVersion = "questions-2" },
            Pins with { ReferenceVersion = "references-2" } })
            Assert.NotEqual(original, QuoteCanonicalJson.Create("{}", changed).ContentHash);
    }

    [Theory]
    [InlineData("{\"a\":1,\"a\":2}")]
    [InlineData("{\"a\":1,\"\\u0061\":1}")]
    [InlineData("{\"items\":[{\"id\":1,\"id\":1}]}")]
    public void DuplicateDecodedPropertiesAreRejectedAtEveryLevel(string json)
    {
        Assert.Equal("quote-duplicate-property", Assert.Throws<QuoteInputException>(() => Canonical(json)).Code);
    }

    [Theory]
    [InlineData("[]", "quote-object-required")]
    [InlineData("null", "quote-object-required")]
    [InlineData("{}{}", "quote-invalid-json")]
    [InlineData("{\"x\":1,}", "quote-invalid-json")]
    [InlineData("{/*comment*/}", "quote-invalid-json")]
    [InlineData("{\"n\":1e1025}", "quote-number-exponent-out-of-range")]
    [InlineData("{\"n\":1e-1025}", "quote-number-exponent-out-of-range")]
    [InlineData("{\"n\":1e99999999999999999999}", "quote-number-exponent-out-of-range")]
    [InlineData("{\"n\":NaN}", "quote-invalid-json")]
    public void InvalidInputsReturnSafeStableCodes(string json, string code)
    {
        var error = Assert.Throws<QuoteInputException>(() => Canonical(json));
        Assert.Equal(code, error.Code); Assert.Equal(code, error.Message);
    }

    [Fact]
    public void ByteBoundsUseUtf8AndDepthIsBounded()
    {
        var maximum = "{\"v\":\"" + new string('a', QuoteCanonicalJson.MaximumBytes - 8) + "\"}";
        Assert.Equal(QuoteCanonicalJson.MaximumBytes, Canonical(maximum).Json.Length);
        Assert.Equal("quote-input-too-large", Assert.Throws<QuoteInputException>(() => Canonical(maximum + " ")).Code);
        var unicode = "{\"v\":\"" + new string('é', QuoteCanonicalJson.MaximumBytes / 2) + "\"}";
        Assert.Equal("quote-input-too-large", Assert.Throws<QuoteInputException>(() => Canonical(unicode)).Code);
        var nested = string.Concat(Enumerable.Repeat("{\"v\":", 65)) + "0" + new string('}', 65);
        Assert.Equal("quote-invalid-json", Assert.Throws<QuoteInputException>(() => Canonical(nested)).Code);
    }

    [Fact]
    public void UnpairedUnicodeAndInvalidTrustedPinsAreRejected()
    {
        Assert.Equal("quote-invalid-unicode", Assert.Throws<QuoteInputException>(() => Canonical("{\"v\":\"\ud800\"}")).Code);
        Assert.Throws<ArgumentException>(() => QuoteCanonicalJson.Create("{}", Pins with { ProductVersionId = Guid.Empty }));
        Assert.Throws<ArgumentException>(() => QuoteCanonicalJson.Create("{}", Pins with { ReferenceVersion = " reference " }));
    }

    [Fact]
    public void NormalizationCannotAmplifyNumbersBeyondTheStorageLimit()
    {
        var json = "{\"rows\":[" + string.Join(',', Enumerable.Repeat("1e1024", 1100)) + "]}";
        Assert.True(json.Length < QuoteCanonicalJson.MaximumBytes);
        Assert.Equal("quote-input-too-large", Assert.Throws<QuoteInputException>(() => Canonical(json)).Code);
        var small = "{\"n\":0." + new string('0', 1024) + "1}";
        Assert.Equal("quote-number-exponent-out-of-range", Assert.Throws<QuoteInputException>(() => Canonical(small)).Code);
    }
}
