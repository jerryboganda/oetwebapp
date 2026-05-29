using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening V2 — Admin Sequence Builder (WS4).
//
// An OPTIONAL, admin-authored explicit exam-sequence for a Listening paper:
// the ordered list of FSM phases (instruction / reading_time / audio_extract /
// local_check_time / global_check_time / section_transition / auto_submit)
// with a per-phase window duration. Stored as JSON on
// ContentPaper.ListeningSequenceJson.
//
// Consumed by ListeningSessionService when present; when ABSENT
// (ListeningSequenceJson is null) the FSM derives the canonical sequence from
// the effective policy via DeriveFromPolicy, which reproduces the legacy
// per-window timing byte-for-byte. The derived sequence carries BASE (pre
// extra-time) durations exactly as ListeningSessionService.ComputeWindowMs read
// them from the policy; the session service still applies the ExtraTimePct
// multiplier on top, so a null-sequence paper behaves identically to before.
//
// The canonical phase order is ListeningFsmTransitions.ForwardPath — one
// sequence item per ForwardPath state. Validation proves an authored sequence
// maps 1:1 onto those phases (no missing / extra / reordered sections), that
// every audio_extract resolves to an authored extract, and that authored
// question coverage is the canonical 42 (A=24, B=6, C=12).
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>One ordered phase in a Listening exam-sequence. <see cref="Type"/>
/// is one of: <c>instruction</c>, <c>reading_time</c>, <c>beep</c>,
/// <c>audio_extract</c>, <c>local_check_time</c>, <c>global_check_time</c>,
/// <c>section_transition</c>, <c>auto_submit</c>.</summary>
public sealed record ListeningSequenceItem(
    int Index,
    string Type,
    string? PartCode,
    int? ExtractDisplayOrder,
    int? DurationMs,
    string? Label);

/// <summary>The authored exam-sequence document. <see cref="Version"/> lets the
/// shape evolve; <see cref="Items"/> is the ordered phase list.</summary>
public sealed record ListeningSequence(
    IReadOnlyList<ListeningSequenceItem> Items,
    int Version = 1);

/// <summary>Per-part authored-question counts plus the set of authored extract
/// part codes, passed into <see cref="ListeningSequenceService.Validate"/> so a
/// sequence can be checked against the paper's actual structure without the
/// service re-reading the JSON blob itself.</summary>
public sealed record ListeningSequenceStructure(
    int PartACount,
    int PartBCount,
    int PartCCount,
    IReadOnlySet<string> AuthoredExtractPartCodes);

/// <summary>Structured validation result, mirroring the shape of
/// <see cref="ListeningValidationReport"/> (publish-gate report).</summary>
public sealed record ListeningSequenceValidationReport(
    bool IsValid,
    IReadOnlyList<ListeningValidationIssue> Issues,
    ListeningValidationCounts Counts);

public sealed class ListeningSequenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly LearnerDbContext _db;

    public ListeningSequenceService(LearnerDbContext db)
    {
        _db = db;
    }

    // ── Sequence item types ───────────────────────────────────────────────
    public const string TypeInstruction = "instruction";
    public const string TypeReadingTime = "reading_time";
    public const string TypeBeep = "beep";
    public const string TypeAudioExtract = "audio_extract";
    public const string TypeLocalCheckTime = "local_check_time";
    public const string TypeGlobalCheckTime = "global_check_time";
    public const string TypeSectionTransition = "section_transition";
    public const string TypeAutoSubmit = "auto_submit";

    private static readonly HashSet<string> KnownTypes = new(StringComparer.Ordinal)
    {
        TypeInstruction, TypeReadingTime, TypeBeep, TypeAudioExtract,
        TypeLocalCheckTime, TypeGlobalCheckTime, TypeSectionTransition, TypeAutoSubmit,
    };

    /// <summary>Canonical OET Listening coverage. Mirrors
    /// <see cref="ListeningStructureService"/>.</summary>
    public const int CanonicalPartACount = 24;
    public const int CanonicalPartBCount = 6;
    public const int CanonicalPartCCount = 12;

    // ── Read ──────────────────────────────────────────────────────────────

    /// <summary>Parse <c>ContentPaper.ListeningSequenceJson</c>. Returns
    /// <c>null</c> when the paper is missing, the column is null/blank, or the
    /// JSON is malformed — every null path makes the FSM fall back to
    /// <see cref="DeriveFromPolicy"/>, so a missing/garbage sequence can never
    /// break a live attempt.</summary>
    public async Task<ListeningSequence?> GetAsync(string paperId, CancellationToken ct)
    {
        var json = await _db.ContentPapers
            .AsNoTracking()
            .Where(p => p.Id == paperId)
            .Select(p => p.ListeningSequenceJson)
            .FirstOrDefaultAsync(ct);
        return Parse(json);
    }

    internal static ListeningSequence? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var parsed = JsonSerializer.Deserialize<ListeningSequence>(json, JsonOptions);
            if (parsed?.Items is null || parsed.Items.Count == 0) return null;
            // Re-index defensively so callers can index by State position even
            // if a hand-edited payload left gaps in Index.
            var reindexed = parsed.Items
                .Select((item, i) => item with { Index = i })
                .ToList();
            return parsed with { Items = reindexed };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── Backward-compat keystone ──────────────────────────────────────────

    /// <summary>
    /// Produce the canonical sequence whose per-state <see cref="ListeningSequenceItem.DurationMs"/>
    /// EXACTLY reproduces the legacy <c>ListeningSessionService.ComputeWindowMs</c>
    /// output for every <see cref="ListeningFsmTransitions.ForwardPath"/> state.
    ///
    /// IMPORTANT: durations are BASE values (pre extra-time). The session
    /// service applies the <c>ExtraTimePct</c> multiplier on top exactly as it
    /// did when it read the policy directly, so the resulting window is
    /// byte-identical to the prior <c>Apply(p.PreviewMsA1)</c> etc.
    /// </summary>
    public ListeningSequence DeriveFromPolicy(EffectiveListeningPolicy policy, IListeningModePolicy mode)
    {
        var items = new List<ListeningSequenceItem>(ListeningFsmTransitions.ForwardPath.Count);
        var index = 0;
        foreach (var state in ListeningFsmTransitions.ForwardPath)
        {
            items.Add(new ListeningSequenceItem(
                Index: index++,
                Type: CanonicalTypeFor(state),
                PartCode: ListeningFsmTransitions.PartFor(state),
                ExtractDisplayOrder: null,
                DurationMs: BaseWindowMs(state, policy, mode),
                Label: state));
        }
        return new ListeningSequence(items);
    }

    /// <summary>
    /// Base (pre extra-time) window for a ForwardPath state — the exact
    /// right-hand side of <c>ListeningSessionService.ComputeWindowMs</c>'s
    /// switch BEFORE its <c>Apply</c> multiplier. Keep these two in lockstep.
    /// </summary>
    internal static int BaseWindowMs(string state, EffectiveListeningPolicy p, IListeningModePolicy mode)
        => state switch
        {
            ListeningFsmTransitions.A1Preview => p.PreviewMsA1,
            ListeningFsmTransitions.A2Preview => p.PreviewMsA2,
            ListeningFsmTransitions.C1Preview => p.PreviewMsC1,
            ListeningFsmTransitions.C2Preview => p.PreviewMsC2,
            ListeningFsmTransitions.A1Review => p.ReviewMsA1,
            ListeningFsmTransitions.A2Review => p.ReviewMsA2,
            ListeningFsmTransitions.C1Review => p.ReviewMsC1,
            ListeningFsmTransitions.C2Review => p.ReviewMsC2FinalCbt,
            ListeningFsmTransitions.C2FinalReview => mode.FinalReviewAllPartsMs ?? p.ReviewMsC2FinalCbt,
            ListeningFsmTransitions.BIntro => p.BetweenSectionTransitionMs,
            _ => 0,
        };

    /// <summary>Map a ForwardPath state to its canonical sequence-item type.</summary>
    internal static string CanonicalTypeFor(string state) => state switch
    {
        ListeningFsmTransitions.Intro => TypeInstruction,
        ListeningFsmTransitions.BIntro => TypeSectionTransition,
        ListeningFsmTransitions.C2FinalReview => TypeGlobalCheckTime,
        ListeningFsmTransitions.Submitted => TypeAutoSubmit,
        _ when ListeningFsmTransitions.IsAudioState(state) => TypeAudioExtract,
        _ when state.EndsWith("_preview", StringComparison.Ordinal) => TypeReadingTime,
        _ when state.EndsWith("_review", StringComparison.Ordinal) => TypeLocalCheckTime,
        _ => TypeInstruction,
    };

    /// <summary>Resolve the effective window for a state given a sequence
    /// (authored or derived). Returns the matching item's
    /// <see cref="ListeningSequenceItem.DurationMs"/>, or <c>null</c> when the
    /// sequence has no item for the state (caller then falls back to the
    /// derived/legacy value). This is the single seam the session FSM calls.
    /// </summary>
    public static int? WindowMsForState(ListeningSequence sequence, string state)
    {
        // The canonical sequence stores the FSM state name in Label; match on
        // that first (exact, stable). This keeps the lookup independent of item
        // ordering and of the Type taxonomy.
        foreach (var item in sequence.Items)
        {
            if (string.Equals(item.Label, state, StringComparison.Ordinal))
                return item.DurationMs ?? 0;
        }
        return null;
    }

    // ── Validation ────────────────────────────────────────────────────────

    /// <summary>
    /// Validate that <paramref name="sequence"/> maps 1:1 onto the canonical
    /// OET ForwardPath phases (no missing / extra / reordered sections), that
    /// every <c>audio_extract</c> resolves to an authored extract, and that the
    /// authored question coverage in <paramref name="structure"/> is the
    /// canonical 42 (A=24, B=6, C=12). Returns a structured report mirroring
    /// <see cref="ListeningValidationReport"/>.
    /// </summary>
    public ListeningSequenceValidationReport Validate(
        ListeningSequence sequence,
        ListeningSequenceStructure structure)
    {
        var issues = new List<ListeningValidationIssue>();
        var counts = new ListeningValidationCounts(
            structure.PartACount,
            structure.PartBCount,
            structure.PartCCount,
            structure.PartACount + structure.PartBCount + structure.PartCCount);

        if (sequence.Items is null || sequence.Items.Count == 0)
        {
            issues.Add(new("listening_sequence_empty", "error",
                "The exam-sequence has no items."));
            return new ListeningSequenceValidationReport(false, issues, counts);
        }

        // Unknown item types.
        var unknownTypes = sequence.Items
            .Where(i => !KnownTypes.Contains(i.Type ?? string.Empty))
            .Select(i => i.Type ?? "(null)")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unknownTypes.Length > 0)
        {
            issues.Add(new("listening_sequence_unknown_type", "error",
                $"Sequence contains unsupported phase type(s): {string.Join(", ", unknownTypes)}."));
        }

        // 1:1 mapping onto the canonical ForwardPath phases. The authored item
        // types, in order, must equal the canonical phase-type sequence derived
        // from ForwardPath — this catches missing, extra, and reordered phases
        // in one comparison.
        var expected = ListeningFsmTransitions.ForwardPath
            .Select(CanonicalTypeFor)
            .ToArray();
        var actual = sequence.Items.Select(i => i.Type ?? string.Empty).ToArray();

        if (actual.Length != expected.Length)
        {
            issues.Add(new("listening_sequence_phase_count", "error",
                $"Sequence must have exactly {expected.Length} phases (one per OET Listening FSM state); found {actual.Length}."));
        }
        else
        {
            for (var i = 0; i < expected.Length; i++)
            {
                if (!string.Equals(actual[i], expected[i], StringComparison.Ordinal))
                {
                    issues.Add(new("listening_sequence_phase_order", "error",
                        $"Phase {i + 1} must be '{expected[i]}' (matching FSM state '{ListeningFsmTransitions.ForwardPath[i]}'); found '{actual[i]}'. Sequences cannot reorder, drop, or insert sections."));
                    break;
                }
            }
        }

        // Negative / non-positive durations on timed phases.
        foreach (var item in sequence.Items)
        {
            if (item.DurationMs is < 0)
            {
                issues.Add(new("listening_sequence_negative_duration", "error",
                    $"Phase {item.Index + 1} ('{item.Type}') has a negative duration."));
            }
        }

        // Every audio_extract must resolve to an authored extract part code.
        var authored = structure.AuthoredExtractPartCodes;
        foreach (var item in sequence.Items.Where(i => string.Equals(i.Type, TypeAudioExtract, StringComparison.Ordinal)))
        {
            var partCode = NormalizePartCode(item.PartCode);
            if (partCode is null)
            {
                issues.Add(new("listening_sequence_audio_no_part", "error",
                    $"Phase {item.Index + 1} is an audio extract but has no part code."));
                continue;
            }
            if (!ResolvesToAuthoredExtract(partCode, authored))
            {
                issues.Add(new("listening_sequence_audio_unresolved", "error",
                    $"Phase {item.Index + 1} (audio extract, part {partCode}) does not resolve to an authored extract."));
            }
        }

        // Canonical 42-question coverage (A=24, B=6, C=12).
        if (structure.PartACount != CanonicalPartACount)
        {
            issues.Add(new("listening_sequence_part_a_count", "error",
                $"Part A coverage is {structure.PartACount} item(s); OET requires {CanonicalPartACount}."));
        }
        if (structure.PartBCount != CanonicalPartBCount)
        {
            issues.Add(new("listening_sequence_part_b_count", "error",
                $"Part B coverage is {structure.PartBCount} item(s); OET requires {CanonicalPartBCount}."));
        }
        if (structure.PartCCount != CanonicalPartCCount)
        {
            issues.Add(new("listening_sequence_part_c_count", "error",
                $"Part C coverage is {structure.PartCCount} item(s); OET requires {CanonicalPartCCount}."));
        }

        var isValid = !issues.Any(i => string.Equals(i.Severity, "error", StringComparison.OrdinalIgnoreCase));
        return new ListeningSequenceValidationReport(isValid, issues, counts);
    }

    /// <summary>Convenience overload: build the <see cref="ListeningSequenceStructure"/>
    /// from a paper's authored JSON blob, then validate.</summary>
    public async Task<ListeningSequenceValidationReport> ValidateForPaperAsync(
        string paperId,
        ListeningSequence sequence,
        CancellationToken ct)
    {
        var paper = await _db.ContentPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw ApiException.NotFound("listening_paper_not_found", "Paper not found.");
        var structure = ReadStructure(paper.ExtractedTextJson);
        return Validate(sequence, structure);
    }

    // ── Write ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Replace the paper's authored sequence. Validates first (rejecting an
    /// invalid sequence), refuses when the paper is Published (publish-gate,
    /// like the structure routes), writes the JSON, bumps RowVersion, and
    /// records an <c>AuditEvent{Action="ListeningSequenceUpdated"}</c> mirroring
    /// the other authoring writes.
    /// </summary>
    public async Task<ListeningSequenceValidationReport> ReplaceAsync(
        string paperId,
        ListeningSequence sequence,
        string adminId,
        CancellationToken ct)
    {
        var paper = await _db.ContentPapers.FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw ApiException.NotFound("listening_paper_not_found", "Paper not found.");

        // Publish-gate: published papers are immutable through this surface
        // (matches the structure-route gate enforced in the endpoint layer).
        if (paper.Status == ContentStatus.Published)
        {
            throw ApiException.Conflict(
                "listening_sequence_paper_published",
                "This Listening paper is published and its exam-sequence cannot be changed. Create a new revision first.");
        }

        var structure = ReadStructure(paper.ExtractedTextJson);
        var report = Validate(sequence, structure);
        if (!report.IsValid)
        {
            throw ApiException.Validation(
                "listening_sequence_invalid",
                "The exam-sequence is invalid: " +
                string.Join(" ", report.Issues.Where(i => i.Severity == "error").Select(i => i.Message)));
        }

        // Re-index canonically on write so the stored document is always dense.
        var normalized = sequence with
        {
            Items = sequence.Items.Select((item, i) => item with { Index = i }).ToList(),
        };

        paper.ListeningSequenceJson = JsonSerializer.Serialize(normalized, JsonOptions);
        paper.UpdatedAt = DateTimeOffset.UtcNow;
        paper.RowVersion++;

        _db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit_{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = adminId,
            ActorAuthAccountId = adminId,
            ActorName = adminId,
            Action = "ListeningSequenceUpdated",
            ResourceType = "ContentPaper",
            ResourceId = paper.Id,
            Details = JsonSerializer.Serialize(new
            {
                phases = normalized.Items.Count,
                version = normalized.Version,
            }),
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ApiException.Conflict("content_paper_concurrent_update",
                "This paper was modified by another admin. Reload and retry.");
        }

        return report;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static bool ResolvesToAuthoredExtract(string partCode, IReadOnlySet<string> authored)
    {
        if (authored.Contains(partCode)) return true;
        // Accept legacy granular split: the canonical part code "A" is covered
        // by A1/A2 and "C" by C1/C2 (mirrors the structure validator buckets).
        return partCode switch
        {
            "A" => authored.Contains("A1") || authored.Contains("A2"),
            "C" => authored.Contains("C1") || authored.Contains("C2"),
            _ => false,
        };
    }

    private static string? NormalizePartCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var n = raw.Trim().ToUpperInvariant();
        return n switch
        {
            "A" or "A1" or "A2" or "B" or "C" or "C1" or "C2" => n,
            _ => null,
        };
    }

    /// <summary>Read per-part authored-question counts and the authored extract
    /// part-code set from a paper's <c>ExtractedTextJson</c>. Uses the same
    /// <c>listeningQuestions</c> / <c>listeningExtracts</c> keys and part-code
    /// normalisation as the rest of the Listening authoring stack.</summary>
    internal static ListeningSequenceStructure ReadStructure(string? extractedTextJson)
    {
        var extractParts = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(extractedTextJson))
            return new ListeningSequenceStructure(0, 0, 0, extractParts);

        int a = 0, b = 0, c = 0;
        try
        {
            using var doc = JsonDocument.Parse(extractedTextJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("listeningQuestions", out var qs)
                && qs.ValueKind == JsonValueKind.Array)
            {
                foreach (var q in qs.EnumerateArray())
                {
                    var partCode = ReadPart(q);
                    if (partCode is null) continue;
                    if (partCode.StartsWith('A')) a++;
                    else if (partCode.StartsWith('B')) b++;
                    else if (partCode.StartsWith('C')) c++;
                }
            }

            if (root.TryGetProperty("listeningExtracts", out var ex)
                && ex.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in ex.EnumerateArray())
                {
                    var partCode = ReadPart(e);
                    if (partCode is not null) extractParts.Add(partCode);
                }
            }
        }
        catch (JsonException)
        {
            return new ListeningSequenceStructure(0, 0, 0, extractParts);
        }

        return new ListeningSequenceStructure(a, b, c, extractParts);
    }

    private static string? ReadPart(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        string? raw = null;
        if (obj.TryGetProperty("partCode", out var pc) && pc.ValueKind == JsonValueKind.String)
            raw = pc.GetString();
        else if (obj.TryGetProperty("part", out var p) && p.ValueKind == JsonValueKind.String)
            raw = p.GetString();
        return NormalizePartCode(raw);
    }
}
