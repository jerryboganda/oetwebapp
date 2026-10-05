// Used ONLY while backend/src/OetLearner.Api/Services/Content/PdfTextEngine.cs does not exist (see Fleet.Agent.csproj).
// When the API track adds that shared file, the agent link-compiles it instead and this file drops out of the build.
// LayoutRevision is bumped by a human whenever PdfPigPdfTextExtractor.cs changes (protocol 6.1.4 item 3); the CI
// golden-hash test (Golden/pdf-extractor-history.json) fails if the extractor changes without a bump.
namespace OetLearner.Api.Services.Content;

public static class PdfTextEngine
{
    public const int LayoutRevision = 1;
}
