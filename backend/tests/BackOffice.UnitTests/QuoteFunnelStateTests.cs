using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Quotes;
using Xunit;

namespace BackOffice.UnitTests;
public sealed class QuoteFunnelStateTests
{
    [Fact]
    public void RawNullFalseAndNestedDraftsAreAllowed()
    {
        QuoteFunnelState.Validate("""{"version":1,"step":6,"formData":{"drivers":[{"firstName":null,"coverPersonalVehicle":false}]},"pendingForms":{"driver-details":{"surname":"Unsaved"}}}""");
    }
    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"version\":1,\"step\":0,\"formData\":{}}")]
    [InlineData("{\"version\":1,\"step\":15,\"formData\":{}}")]
    [InlineData("{\"version\":1,\"step\":1,\"formData\":[]}")]
    [InlineData("{\"version\":\"1\",\"step\":1,\"formData\":{}}")]
    [InlineData("{")]
    public void InvalidCaptureEnvelopeIsRejected(string json) => Assert.Throws<QuoteOperationException>(()=>QuoteFunnelState.Validate(json));
    [Fact]
    public void Utf8ByteLimitIsEnforced() => Assert.Throws<QuoteOperationException>(()=>QuoteFunnelState.Validate("{\"version\":1,\"step\":1,\"formData\":{\"note\":\""+new string('é',600000)+"\"}}"));
}
