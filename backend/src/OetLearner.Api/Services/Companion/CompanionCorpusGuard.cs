using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services.Companion;

/// <summary>One contaminated chunk, named without repeating its content.</summary>
public sealed record CompanionContaminationFinding(string SourceKey, string? Heading, string Marker);

/// <summary>
/// Keeps acceptance-test material out of the production knowledge corpus.
///
/// <para>
/// The Knowledge Base Master Manifest §5 puts the four Final Testing PDFs — their
/// prompts, PASS CHECKs, scorecards and expected results — at the top of the
/// never-index list, and the packs themselves state that if the assistant can
/// retrieve the test prompts or pass criteria, <b>the result is invalid</b>.
/// That makes this a correctness control for the test itself, not a content
/// preference: a contaminated corpus does not produce a worse score, it produces
/// a meaningless one.
/// </para>
///
/// <para>
/// The realistic contamination path is not malice, it is convenience. Rulebook
/// content reaches the corpus through <c>POST /v1/admin/rulebooks/import</c>,
/// which accepts arbitrary JSON; published rows are served by
/// <c>DbBackedRulebookLoader</c> and indexed by
/// <see cref="CompanionRulebookIndexer"/>. An admin pasting a testing pack in to
/// "check what Sami should say" would silently void every subsequent run. This
/// screen sits at the corpus boundary so every present and future indexer
/// inherits it.
/// </para>
/// </summary>
public static class CompanionCorpusGuard
{
    /// <summary>
    /// Phrases that appear in the acceptance packs and essentially nowhere in
    /// genuine teaching material.
    ///
    /// <para>
    /// Each is matched as a whole phrase, case-insensitively, and any single hit
    /// rejects the source. The phrases are deliberately structural — headings and
    /// scaffolding the packs use throughout — rather than topical words like
    /// "pass" or "check", which occur constantly in legitimate OET teaching and
    /// would make this a nuisance instead of a guard.
    /// </para>
    /// </summary>
    private static readonly string[] Markers =
    [
        "PASS CHECK",
        "FINAL TESTING PACK",
        "TESTER SCORECARD",
        "PROMPT TO SEND",
        "SETUP / SEQUENCE",
        "RELEASE BLOCKER",
        "DO NOT ADD THESE TESTING PDFS",
        "PRE-CANDIDATE TECHNICAL RELEASE GATE",
        "NOTES / DEFECT ID",
    ];

    /// <summary>
    /// Returns the marker that disqualifies <paramref name="text"/>, or
    /// <c>null</c> when it is safe to index.
    /// </summary>
    public static string? FindDisqualifyingMarker(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        foreach (var marker in Markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return marker;
            }
        }

        return null;
    }

    /// <summary>
    /// True when the text carries acceptance-pack scaffolding and must not enter
    /// the corpus.
    /// </summary>
    public static bool IsDisqualified(string? text) =>
        FindDisqualifyingMarker(text) is not null;

    /// <summary>
    /// Scans the live corpus for material that should never have entered it.
    ///
    /// <para>
    /// The ingest-time screen is the real control; this is the audit that proves
    /// it held. It matters because the screen can only guard the paths that go
    /// through an indexer, and a corpus can also be written to by a migration, a
    /// restore from a contaminated backup, or a direct database edit. The
    /// Manifest asks for a contamination check before every acceptance run for
    /// exactly that reason, and asking the companion whether it can retrieve a
    /// PASS CHECK proves nothing when it merely fails to surface one.
    /// </para>
    ///
    /// <para>
    /// Matching happens in memory rather than as a SQL predicate: there are a
    /// handful of markers and this runs on an operator action, so a readable
    /// scan is worth more than a clever query.
    /// </para>
    /// </summary>
    public static async Task<IReadOnlyList<CompanionContaminationFinding>> FindContaminatedChunksAsync(
        LearnerDbContext db,
        CancellationToken ct)
    {
        var findings = new List<CompanionContaminationFinding>();

        var rows = await db.CompanionChunks
            .AsNoTracking()
            .Join(db.CompanionSources.AsNoTracking(), c => c.SourceId, s => s.Id,
                (c, s) => new { s.SourceKey, c.Heading, c.Text })
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            var marker = FindDisqualifyingMarker(row.Heading) ?? FindDisqualifyingMarker(row.Text);
            if (marker is null) continue;

            // The marker is named but the surrounding text is not echoed: if this
            // really is acceptance-pack content, copying it into an API response
            // would publish the thing being complained about.
            findings.Add(new CompanionContaminationFinding(row.SourceKey, row.Heading, marker));
        }

        return findings;
    }
}
