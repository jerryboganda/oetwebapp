using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant.Indexing;
using OetLearner.Api.Services.Rulebook;
using Pgvector;

namespace OetLearner.Api.Services.Companion;

public interface ICompanionRulebookIndexer
{
    /// <summary>
    /// Indexes the approved rulebooks into the companion knowledge store.
    /// Idempotent: a rule whose text is unchanged keeps its existing embedding.
    /// </summary>
    Task<CompanionIndexResult> IndexAsync(
        IReadOnlyCollection<ExamProfession>? professions,
        bool embed,
        CancellationToken ct);
}

public sealed record CompanionIndexResult(
    int SourcesWritten,
    int ChunksWritten,
    int ChunksUnchanged,
    int ChunksEmbedded,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Builds the Stage 1 companion corpus from the 115 versioned rulebooks.
///
/// <para>
/// It reads through <see cref="IRulebookLoader"/> — which DI resolves to the
/// DB-backed loader — so admin edits and rulebook versions flow into the index
/// instead of the index drifting against a snapshot of the JSON files.
/// </para>
///
/// <para><b>Chunking.</b> One chunk per <see cref="OetRule"/>. The source
/// specification requires chunking "by pedagogical meaning … not arbitrary fixed
/// size", and a rule is exactly that unit: it has a stable id, a section, a
/// body, and its own exemplars. It also makes citations meaningful — the
/// companion can say <i>which rule</i> it is teaching from.</para>
///
/// <para><b>Authority.</b> Every rulebook is profession-scoped, so all chunks are
/// <see cref="CompanionAuthorityClass.ProfessionApprovedMethod"/>. That is what
/// gives them precedence over generic teaching advice while still ranking below
/// a verified official exam fact.</para>
///
/// <para><b>Proprietary.</b> Rulebooks are Dr Hesham's paid teaching method, so
/// sources are marked <see cref="CompanionSource.IsProprietary"/>. They stay
/// retrievable (the companion must be able to teach the method) but the
/// retriever enforces verbatim-span and volume caps so the assistant cannot be
/// used to reconstruct a rulebook.</para>
/// </summary>
public sealed class CompanionRulebookIndexer(
    LearnerDbContext db,
    IRulebookLoader rulebooks,
    IEmbeddingService embeddings,
    ILogger<CompanionRulebookIndexer> logger) : ICompanionRulebookIndexer
{
    /// <summary>Rulebook kinds that carry learner-facing teaching method.</summary>
    private static readonly RuleKind[] IndexableKinds =
    [
        RuleKind.Writing,
        RuleKind.Speaking,
        RuleKind.Reading,
        RuleKind.Listening,
        RuleKind.Grammar,
        RuleKind.Vocabulary,
        RuleKind.Pronunciation,
        RuleKind.Conversation,
    ];

    public async Task<CompanionIndexResult> IndexAsync(
        IReadOnlyCollection<ExamProfession>? professions,
        bool embed,
        CancellationToken ct)
    {
        var warnings = new List<string>();
        var sourcesWritten = 0;
        var chunksWritten = 0;
        var chunksUnchanged = 0;
        var chunksEmbedded = 0;

        var books = rulebooks.All()
            .Where(b => IndexableKinds.Contains(b.Kind))
            .Where(b => professions is null || professions.Count == 0 || professions.Contains(b.Profession))
            .ToList();

        if (books.Count == 0)
        {
            warnings.Add("No rulebooks matched the requested scope; nothing indexed.");
            return new CompanionIndexResult(0, 0, 0, 0, warnings);
        }

        var now = DateTimeOffset.UtcNow;

        foreach (var book in books)
        {
            ct.ThrowIfCancellationRequested();

            if (book.Rules.Count == 0)
            {
                warnings.Add($"Rulebook {book.Kind}/{book.Profession} has no rules; skipped.");
                continue;
            }

            var sourceKey = BuildSourceKey(book);
            var version = string.IsNullOrWhiteSpace(book.Version) ? "v1" : book.Version;

            var source = await db.CompanionSources
                .FirstOrDefaultAsync(s => s.SourceKey == sourceKey && s.Version == version, ct);

            if (source is null)
            {
                source = new CompanionSource
                {
                    Id = Guid.NewGuid(),
                    SourceKey = sourceKey,
                    Version = version,
                    CreatedAt = now,
                };
                db.CompanionSources.Add(source);
                sourcesWritten++;
            }

            source.SourceType = "rulebook";
            source.Title = $"{book.Kind} rulebook — {book.Profession}";
            source.AuthorityClass = CompanionAuthorityClass.ProfessionApprovedMethod;
            // Rulebooks in this repository are already the approved teaching artefact:
            // DbBackedRulebookLoader serves admin-approved DB rows in preference to
            // the embedded JSON, so reaching here means the content is publishable.
            source.State = CompanionSourceState.Approved;
            source.ExamTypeCode = "OET";
            source.ProfessionId = book.Profession.ToString().ToLowerInvariant();
            source.SubtestCode = MapSubtest(book.Kind);
            source.IsProprietary = true;
            source.RequiredEntitlementScope = null;
            source.StorageLocator = $"rulebooks/{book.Kind.ToString().ToLowerInvariant()}/{source.ProfessionId}/rulebook.{version}.json";
            source.ApprovedAt ??= now;
            source.UpdatedAt = now;

            await db.SaveChangesAsync(ct);

            var existing = await db.CompanionChunks
                .Where(c => c.SourceId == source.Id)
                .ToDictionaryAsync(c => c.Ordinal, ct);

            var ordinal = 0;
            var pending = new List<CompanionChunk>();

            foreach (var rule in book.Rules)
            {
                ct.ThrowIfCancellationRequested();

                var text = BuildChunkText(book, rule);
                if (string.IsNullOrWhiteSpace(text)) continue;

                var hash = Sha256(text);
                var heading = string.IsNullOrWhiteSpace(rule.Title)
                    ? rule.Id
                    : $"{rule.Id} — {rule.Title}";

                if (existing.TryGetValue(ordinal, out var chunk))
                {
                    if (chunk.ContentHash == hash && chunk.Embedding is not null)
                    {
                        // Unchanged and already embedded: leave it alone. This is what
                        // makes re-indexing cheap enough to run on every knowledge release.
                        chunksUnchanged++;
                        ordinal++;
                        continue;
                    }

                    // Compare BEFORE overwriting, otherwise a changed rule keeps the
                    // embedding of its previous text and retrieval silently rots.
                    var textChanged = chunk.ContentHash != hash;

                    chunk.Heading = heading;
                    chunk.Text = text;
                    chunk.ContentHash = hash;
                    chunk.UpdatedAt = now;
                    if (textChanged) chunk.Embedding = null;
                    pending.Add(chunk);
                }
                else
                {
                    chunk = new CompanionChunk
                    {
                        Id = Guid.NewGuid(),
                        SourceId = source.Id,
                        Ordinal = ordinal,
                        Heading = heading,
                        Text = text,
                        ContentHash = hash,
                        CreatedAt = now,
                        UpdatedAt = now,
                    };
                    db.CompanionChunks.Add(chunk);
                    pending.Add(chunk);
                }

                chunksWritten++;
                ordinal++;
            }

            // Drop chunks left over from a shorter previous version of this rulebook.
            var stale = existing.Where(kv => kv.Key >= ordinal).Select(kv => kv.Value).ToList();
            if (stale.Count > 0) db.CompanionChunks.RemoveRange(stale);

            await db.SaveChangesAsync(ct);

            if (embed && pending.Count > 0)
            {
                chunksEmbedded += await EmbedAsync(pending, warnings, ct);
                await db.SaveChangesAsync(ct);
            }
        }

        logger.LogInformation(
            "Companion rulebook index: {Sources} sources, {Written} chunks written, {Unchanged} unchanged, {Embedded} embedded.",
            sourcesWritten, chunksWritten, chunksUnchanged, chunksEmbedded);

        return new CompanionIndexResult(sourcesWritten, chunksWritten, chunksUnchanged, chunksEmbedded, warnings);
    }

    private async Task<int> EmbedAsync(
        List<CompanionChunk> chunks,
        List<string> warnings,
        CancellationToken ct)
    {
        // The embedding provider is optional. Without it retrieval still works
        // through the keyword path, exactly as CodebaseRetriever degrades.
        try
        {
            var vectors = await embeddings.EmbedBatchAsync(chunks.Select(c => c.Text).ToList(), ct);
            var embedded = 0;

            for (var i = 0; i < chunks.Count && i < vectors.Count; i++)
            {
                if (!db.Database.IsNpgsql()) break; // vector column is unmapped off Postgres
                chunks[i].Embedding = new Vector(vectors[i]);
                embedded++;
            }

            return embedded;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Companion embedding pass failed; chunks remain keyword-searchable.");
            warnings.Add($"Embedding failed ({ex.GetType().Name}); chunks indexed without vectors.");
            return 0;
        }
    }

    /// <summary>
    /// Flattens a rule into retrievable text. Exemplars and forbidden patterns are
    /// included because they are the part learners most often ask about.
    /// </summary>
    private static string BuildChunkText(OetRulebook book, OetRule rule)
    {
        var sb = new StringBuilder();

        var section = book.Sections.FirstOrDefault(s =>
            string.Equals(s.Id, rule.Section, StringComparison.OrdinalIgnoreCase));

        sb.Append(book.Kind).Append(" · ").Append(book.Profession);
        if (section is not null) sb.Append(" · ").Append(section.Title);
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(rule.Title)) sb.AppendLine(rule.Title);
        if (!string.IsNullOrWhiteSpace(rule.Body)) sb.AppendLine(rule.Body);

        sb.Append("Severity: ").Append(rule.Severity).AppendLine();

        if (rule.ExemplarPhrases is { Count: > 0 })
        {
            sb.AppendLine("Good examples:");
            foreach (var phrase in rule.ExemplarPhrases) sb.Append("- ").AppendLine(phrase);
        }

        if (rule.ForbiddenPatterns is { Count: > 0 })
        {
            sb.AppendLine("Avoid:");
            foreach (var pattern in rule.ForbiddenPatterns) sb.Append("- ").AppendLine(pattern);
        }

        return sb.ToString().Trim();
    }

    private static string BuildSourceKey(OetRulebook book) =>
        $"rulebook:{book.Kind.ToString().ToLowerInvariant()}:{book.Profession.ToString().ToLowerInvariant()}";

    private static string? MapSubtest(RuleKind kind) => kind switch
    {
        RuleKind.Writing => "writing",
        RuleKind.Speaking or RuleKind.Conversation or RuleKind.Pronunciation => "speaking",
        RuleKind.Reading => "reading",
        RuleKind.Listening => "listening",
        _ => null, // grammar and vocabulary apply across every subtest
    };

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
