namespace BackOffice.Application.Operations;

public sealed record RenderedPolicyDocument(byte[] Bytes, string Sha256, int PageCount, string RendererVersion, string ProjectionVersion, string FontVersion);

public interface IPolicyDocumentRenderer
{
    RenderedPolicyDocument Render(DocumentRenderInput input);
}
