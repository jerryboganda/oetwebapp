using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant.Indexing;
using OetLearner.Api.Services.Entitlements;

namespace OetLearner.Api.Services.Companion;

public interface ICompanionVocabularyIndexer
{
    Task<CompanionIndexResult> IndexAsync(bool embed, CancellationToken ct);
}

/// <summary>
/// Publishes the curated vocabulary recall sets into the companion corpus
/// (Manifest 1.C).
///
/// <para>
/// Testing Pack 4 scenario 7 asks about the recall sets. The terms are real,
/// curated, provenance-tracked platform content — and the companion could not
/// see a single one, so "what does this term mean, and has it come up before?"
/// was answered from general knowledge with no way to say which recall
/// collection a term belongs to.
/// </para>
///
/// <para>
/// <b>Two sources, not one, and the split is the entitlement boundary.</b>
/// Terms flagged <see cref="VocabularyTerm.IsFreePreview"/> are already shown to
/// unsubscribed visitors, so they index unscoped. Everything else is Recalls
/// module content and indexes with <see cref="ModuleKeys.Recalls"/> as its
/// required scope, which the retriever's prefilter drops for a learner whose
/// package does not include it — before any search runs, so no phrasing reaches
/// it. Publishing one merged source would have made the paid set retrievable by
/// everybody, which is exactly the failure the packs test.
/// </para>
///
/// <para>
/// <b>Chunked per category, not per term.</b> There are thousands of terms; one
/// chunk each would let a single vague query fill the whole evidence budget with
/// near-identical fragments, and would make the per-source chunk cap meaningless.
/// A category is also how learners ask — "cardiology vocabulary", not "term
/// 4417".
/// </para>
/// </summary>
public sealed class CompanionVocabularyIndexer(
    LearnerDbContext db,
    IEmbeddingService embeddings,
    ILogger<CompanionVocabularyIndexer> logger) : ICompanionVocabularyIndexer
{
    internal const string FreeSourceKey = "vocabulary:recall-sets:preview";
    internal const string PaidSourceKey = "vocabulary:recall-sets";
    internal const string Version = "2026-09-09.1";

    /// <summary>
    /// Terms per chunk. Sized so one chunk stays well under the retriever's
    /// 1200-character per-source verbatim cap once definitions are included,
    /// rather than being truncated mid-term.
    /// </summary>
    private const int TermsPerChunk = 12;

    public async Task<CompanionIndexResult> IndexAsync(bool embed, CancellationToken ct)
    {
        var warnings = new List<string>();

        var terms = await db.VocabularyTerms
            .AsNoTracking()
            .Where(t => t.Status == "active")
            .Select(t => new TermRow(
                t.Term, t.Definition, t.ExampleSentence, t.Category,
                t.ProfessionId, t.IsFreePreview, t.RecallSetCodesJson))
            .ToListAsync(ct);

        if (terms.Count == 0)
        {
            warnings.Add(
                "No active vocabulary terms found; the recall sets were not indexed. Sami will say it has no " +
                "verified vocabulary rather than inventing definitions.");
            return new CompanionIndexResult(0, 0, 0, 0, warnings);
        }

        var free = await WriteAsync(FreeSourceKey, terms.Where(t => t.IsFreePreview).ToList(), scope: null, embed, warnings, ct);
        var paid = await WriteAsync(PaidSourceKey, terms.Where(t => !t.IsFreePreview).ToList(), ModuleKeys.Recalls, embed, warnings, ct);

        return new CompanionIndexResult(
            free.SourcesWritten + paid.SourcesWritten,
            free.ChunksWritten + paid.ChunksWritten,
            free.ChunksUnchanged + paid.ChunksUnchanged,
            free.ChunksEmbedded + paid.ChunksEmbedded,
            warnings);
    }

    private async Task<CompanionIndexResult> WriteAsync(
        string sourceKey,
        List<TermRow> terms,
        string? scope,
        bool embed,
        List<string> warnings,
        CancellationToken ct)
    {
        if (terms.Count == 0) return new CompanionIndexResult(0, 0, 0, 0, warnings);

        var isPaid = scope is not null;

        return await CompanionIndexWriter.WriteAsync(
            db, embeddings, logger, sourceKey, Version,
            source =>
            {
                source.SourceType = "vocabulary";
                source.Title = isPaid
                    ? "Vocabulary recall sets"
                    : "Vocabulary recall sets — free preview";
                source.AuthorityClass = CompanionAuthorityClass.CourseMaterial;
                source.State = CompanionSourceState.Approved;
                source.ExamTypeCode = "OET";
                // Terms carry their own profession inline; the set as a whole
                // spans professions, so scoping the source would hide the
                // general medical vocabulary every candidate needs.
                source.ProfessionId = null;
                source.SubtestCode = null;
                source.IsProprietary = isPaid;
                source.RequiredEntitlementScope = scope;
                source.PackageScope = null;
                source.StorageLocator = "VocabularyTerms";
                source.ApprovedAt ??= DateTimeOffset.UtcNow;
            },
            BuildChunks(terms, isPaid), embed, warnings, ct);
    }

    internal static IReadOnlyList<CompanionChunkDraft> BuildChunks(IReadOnlyList<TermRow> terms, bool isPaid)
    {
        var chunks = new List<CompanionChunkDraft>();

        var setCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in terms)
        {
            foreach (var code in ParseSetCodes(term.RecallSetCodesJson))
            {
                setCounts[code] = setCounts.GetValueOrDefault(code) + 1;
            }
        }

        var overview = new StringBuilder();
        overview.AppendLine(
            $"The vocabulary recall sets are curated medical-English terms drawn from what candidates report seeing. " +
            $"This {(isPaid ? "collection" : "free preview")} holds {terms.Count} active terms across " +
            $"{terms.Select(t => t.Category).Distinct(StringComparer.OrdinalIgnoreCase).Count()} categories.");

        if (setCounts.Count > 0)
        {
            overview.AppendLine("Collections and how many of these terms appear in each:");
            foreach (var meta in RecallSetCodes.Metadata)
            {
                if (!setCounts.TryGetValue(meta.Code, out var count)) continue;
                overview.AppendLine($"- {meta.DisplayName} ({meta.ShortLabel}): {count} terms.");
            }

            overview.AppendLine(
                "A collection label is a practice grouping, not a claim that a term is guaranteed to appear in a " +
                "future exam. Never tell a learner a word will be on their test.");
        }

        overview.AppendLine("Learners practise these with spaced repetition in the Recalls area.");
        chunks.Add(new CompanionChunkDraft(
            isPaid ? "Vocabulary recall sets — overview" : "Vocabulary free preview — overview",
            overview.ToString().Trim()));

        foreach (var group in terms
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Category) ? "general" : t.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group
                .OrderBy(t => t.Term, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var offset = 0; offset < ordered.Count; offset += TermsPerChunk)
            {
                var slice = ordered.Skip(offset).Take(TermsPerChunk).ToList();
                var sb = new StringBuilder();
                sb.AppendLine($"Vocabulary — {group.Key} (terms {offset + 1}–{offset + slice.Count} of {ordered.Count}).");

                foreach (var term in slice)
                {
                    sb.Append("- ").Append(term.Term);
                    if (!string.IsNullOrWhiteSpace(term.Definition)) sb.Append(": ").Append(term.Definition);
                    if (!string.IsNullOrWhiteSpace(term.ProfessionId)) sb.Append(" [").Append(term.ProfessionId).Append(']');
                    sb.AppendLine();

                    if (!string.IsNullOrWhiteSpace(term.ExampleSentence))
                    {
                        sb.Append("  e.g. ").AppendLine(term.ExampleSentence);
                    }
                }

                var page = ordered.Count > TermsPerChunk ? $" ({offset / TermsPerChunk + 1})" : string.Empty;
                chunks.Add(new CompanionChunkDraft($"Vocabulary — {group.Key}{page}", sb.ToString().Trim()));
            }
        }

        return chunks;
    }

    private static IEnumerable<string> ParseSetCodes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) yield break;

        List<string>? codes = null;
        try
        {
            codes = JsonSerializer.Deserialize<List<string>>(json);
        }
        catch (JsonException)
        {
            // A malformed provenance field on one term is not worth failing the
            // whole index for; it costs that term its collection label.
        }

        foreach (var code in codes ?? []) yield return code;
    }

    internal readonly record struct TermRow(
        string Term,
        string? Definition,
        string? ExampleSentence,
        string Category,
        string? ProfessionId,
        bool IsFreePreview,
        string? RecallSetCodesJson);
}
