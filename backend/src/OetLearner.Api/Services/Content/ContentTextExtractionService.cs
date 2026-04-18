using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Content;

// ═════════════════════════════════════════════════════════════════════════════
// PDF text extraction — Slice 7
//
// Design: thin IPdfTextExtractor interface so the concrete engine can be
// swapped without touching the service layer. The default implementation is
// a no-op (returns empty string) so the subsystem stays fully functional in
// test / CI without requiring a native PDF library.
//
// Production swap-in: implement this interface against PdfPig (UglyToad.PdfPig,
// MIT license, pure C#, no native deps). Adding the package is a separate,
// opt-in step that needs NuGet access during deploy — flagged in the
// operator handoff items.
// ═════════════════════════════════════════════════════════════════════════════

public interface IPdfTextExtractor
{
    /// <summary>Extract plain text from a PDF stream. Return empty string
    /// if the engine is a no-op or the PDF has no extractable text.</summary>
    Task<string> ExtractAsync(Stream pdfStream, CancellationToken ct);
}

/// <summary>No-op default. Replaced at DI time by a PdfPig-backed impl in
/// production deployments that have the package installed.</summary>
public sealed class NoOpPdfTextExtractor : IPdfTextExtractor
{
    public Task<string> ExtractAsync(Stream pdfStream, CancellationToken ct)
        => Task.FromResult(string.Empty);
}

// ═════════════════════════════════════════════════════════════════════════════
// Service + hosted worker
// ═════════════════════════════════════════════════════════════════════════════

public interface IContentTextExtractionService
{
    /// <summary>Extract text for every PDF asset on a paper and persist it
    /// into <c>ContentPaper.ExtractedTextJson</c> as a
    /// <c>{ assetId: text }</c> map.</summary>
    Task<int> ExtractForPaperAsync(string paperId, CancellationToken ct);
}

public sealed class ContentTextExtractionService(
    LearnerDbContext db,
    IFileStorage storage,
    IPdfTextExtractor extractor,
    ILogger<ContentTextExtractionService> logger) : IContentTextExtractionService
{
    public async Task<int> ExtractForPaperAsync(string paperId, CancellationToken ct)
    {
        var paper = await db.ContentPapers
            .Include(p => p.Assets)
                .ThenInclude(a => a.MediaAsset)
            .FirstOrDefaultAsync(p => p.Id == paperId, ct);
        if (paper is null) return 0;

        var existing = string.IsNullOrEmpty(paper.ExtractedTextJson)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(paper.ExtractedTextJson)
              ?? new();

        int processed = 0;
        foreach (var asset in paper.Assets)
        {
            if (asset.MediaAsset is null) continue;
            if (!string.Equals(asset.MediaAsset.Format, "pdf", StringComparison.OrdinalIgnoreCase)) continue;
            if (existing.ContainsKey(asset.Id)) continue;

            try
            {
                await using var s = await storage.OpenReadAsync(asset.MediaAsset.StoragePath, ct);
                var text = await extractor.ExtractAsync(s, ct);
                existing[asset.Id] = text ?? string.Empty;
                processed++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "PDF extraction failed for asset {AssetId}", asset.Id);
                existing[asset.Id] = string.Empty;
            }
        }

        paper.ExtractedTextJson = JsonSerializer.Serialize(existing);
        paper.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return processed;
    }
}

/// <summary>
/// Background worker that periodically walks published papers and extracts
/// text for assets that don't yet have a cached extraction. Cheap idle loop;
/// the heavy lifting only happens after a new asset is attached.
/// </summary>
public sealed class ContentTextExtractionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ContentTextExtractionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(30, 90)), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Text extraction worker tick failed."); }
            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<IContentTextExtractionService>();

        // Pick the oldest 20 non-archived papers — cheap bounded batch.
        var paperIds = await db.ContentPapers.AsNoTracking()
            .Where(p => p.Status != ContentStatus.Archived)
            .OrderBy(p => p.UpdatedAt)
            .Select(p => p.Id)
            .Take(20)
            .ToListAsync(ct);

        int total = 0;
        foreach (var id in paperIds)
        {
            total += await svc.ExtractForPaperAsync(id, ct);
        }
        return total;
    }
}
