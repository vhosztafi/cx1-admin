using System.Text;
using System.Text.Json;
using BackOffice.Application.Quotes;

namespace BackOffice.Infrastructure.Quotes;

public static class QuoteFunnelState
{
    public static void Validate(string? json)
    {
        if(json is null || Encoding.UTF8.GetByteCount(json)>1024*1024) throw new QuoteOperationException(422,"invalid-funnel-state");
        try
        {
            using var document=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=32});
            var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object || !root.TryGetProperty("version",out var version) || version.ValueKind!=JsonValueKind.Number || !version.TryGetInt32(out var number) || number!=1 ||
                !root.TryGetProperty("formData",out var form) || form.ValueKind!=JsonValueKind.Object || !root.TryGetProperty("step",out var step) || step.ValueKind!=JsonValueKind.Number || !step.TryGetInt32(out var index) || index is <1 or >14)
                throw new QuoteOperationException(422,"invalid-funnel-state");
        }
        catch(JsonException){throw new QuoteOperationException(422,"invalid-funnel-state");}
    }
}
