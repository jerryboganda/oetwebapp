// RW-012 — admin-managed PDF / OCR provider selector.
//
// Pattern matches IConversationAsrProviderSelector / IConversationTtsProviderSelector
// / IPronunciationAsrProviderSelector. Selectors resolve the active provider
// row by category at call time so admins can rotate providers from the
// /admin/ai-providers console without a redeploy.

using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Content;

/// <summary>
/// One PDF / OCR extraction adapter (Azure Document Intelligence,
/// PdfPig, Tesseract, …). Implementations live alongside this file and
/// are registered in DI; the active row is chosen at call time by
/// <see cref="IPaperExtractionProviderSelector"/>.
/// </summary>
public interface IPaperExtractionProvider
{
    /// <summary>Stable code matching <see cref="AiProvider.Code"/>.</summary>
    string Code { get; }

    /// <summary>Run extraction on the given file bytes and return either
    /// the extracted text (for OCR) or the structured JSON (for PDF
    /// extraction).</summary>
    Task<PaperExtractionResult> ExtractAsync(
        ReadOnlyMemory<byte> bytes,
        string fileName,
        string? mimeType,
        CancellationToken ct);
}

public sealed record PaperExtractionResult(
    string? Text,
    string? StructuredJson,
    int? PageCount,
    string ProviderCode,
    long DurationMs);

/// <summary>
/// Selects the active extraction provider for a category at call time.
/// Mirrors IConversationAsrProviderSelector so OCR/PDF gets the same
/// admin-rotatable + audit-logged behaviour.
/// </summary>
public interface IPaperExtractionProviderSelector
{
    /// <summary>Resolve the active provider for a category. Returns null
    /// when no row is active — callers must surface a friendly error
    /// rather than silently falling back to a hard-coded provider.</summary>
    Task<IPaperExtractionProvider?> SelectAsync(
        AiProviderCategory category,
        CancellationToken ct);
}

public sealed class PaperExtractionProviderSelector(
    LearnerDbContext db,
    IEnumerable<IPaperExtractionProvider> providers) : IPaperExtractionProviderSelector
{
    public async Task<IPaperExtractionProvider?> SelectAsync(
        AiProviderCategory category,
        CancellationToken ct)
    {
        if (category != AiProviderCategory.Ocr && category != AiProviderCategory.PdfExtraction)
        {
            throw new ArgumentException(
                $"PaperExtractionProviderSelector only handles Ocr and PdfExtraction; got {category}.",
                nameof(category));
        }

        var activeRow = await db.AiProviders
            .Where(p => p.IsActive && p.Category == category)
            .OrderBy(p => p.FailoverPriority)
            .FirstOrDefaultAsync(ct);
        if (activeRow is null) return null;

        return providers.FirstOrDefault(p =>
            string.Equals(p.Code, activeRow.Code, StringComparison.OrdinalIgnoreCase));
    }
}
