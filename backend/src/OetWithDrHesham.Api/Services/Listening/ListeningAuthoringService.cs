using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services;

namespace OetWithDrHesham.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening authoring service — admin-side CRUD for the 42-item question map.
//
// Until the relational ListeningPart/ListeningQuestion entities ship (Phase 2),
// the canonical home of authored Listening structure is a JSON document under
//   ContentPaper.ExtractedTextJson["listeningQuestions"]
// which is exactly what ListeningLearnerService.ExtractQuestions consumes at
// runtime to deliver the player + grader. This service is the *only* admin-side
// way to read/write that document — it deserializes the surrounding JSON object,
// rewrites the listeningQuestions key in place (preserving any other keys an
// AI-extraction or import pipeline may have set), normalizes the items into the
// runtime shape, stamps an updated timestamp on the paper and writes an audit
// event.
//
// All admin callers (ListeningAuthoringAdminEndpoints) are gated by the
// AdminContentWrite policy and the PerUserWrite rate limit upstream.
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningAuthoringService
{
    Task<ListeningAuthoredQuestionList> GetStructureAsync(string paperId, CancellationToken ct);

    Task<ListeningAuthoredQuestionList> ReplaceStructureAsync(
        string paperId,
        IReadOnlyList<ListeningAuthoredQuestion> questions,
        string adminId,
        CancellationToken ct);

    /// <summary>
    /// Phase 5 tail: read paper-level extract metadata
    /// (<c>listeningExtracts</c>) — accent code, speakers, extract title /
    /// kind / audio window. Empty list when not yet authored.
    /// </summary>
    Task<IReadOnlyList<ListeningAuthoredExtract>> GetExtractsAsync(string paperId, CancellationToken ct);

    /// <summary>
    /// Phase 5 tail: replace paper-level extract metadata
    /// (<c>listeningExtracts</c>) atomically. Preserves sibling JSON keys
    /// (e.g. <c>listeningQuestions</c>, <c>listeningTranscriptSegments</c>).
    /// </summary>
    Task<IReadOnlyList<ListeningAuthoredExtract>> ReplaceExtractsAsync(
        string paperId,
        IReadOnlyList<ListeningAuthoredExtract> extracts,
        string adminId,
        CancellationToken ct);

    /// <summary>
    /// Gap B6: PATCH a single authored question, mutating only fields
    /// that are non-null in <paramref name="patch"/>. Returns the full,
    /// re-tallied structure on success. Throws <see cref="ApiException"/>
    /// (NotFound) when the question id is unknown for this paper.
    /// </summary>
    Task<ListeningAuthoredQuestionList> PatchQuestionAsync(
        string paperId,
        string questionId,
        ListeningQuestionPatch patch,
        string adminId,
        CancellationToken ct);

    /// <summary>
    /// Gap B6: PATCH a single extract by its part code (A1 | A2 | B | C1 | C2),
    /// mutating only fields that are non-null in <paramref name="patch"/>.
    /// Throws <see cref="ApiException"/> (NotFound) when the part code is
    /// not present in the authored extract list.
    /// </summary>
    Task<IReadOnlyList<ListeningAuthoredExtract>> PatchExtractAsync(
        string paperId,
        string extractCode,
        ListeningExtractPatch patch,
        string adminId,
        CancellationToken ct);

    /// <summary>
    /// WS5: import a complete Listening test from a spec §19 JSON manifest
    /// (<c>testTitle</c> / <c>partA</c> / <c>partB</c> / <c>partC</c>). Normalises
    /// the manifest into the authored question + extract shapes and writes them
    /// through <see cref="ReplaceStructureAsync"/> + <see cref="ReplaceExtractsAsync"/>.
    /// Mirrors Reading's <c>ImportManifestAsync</c>: rejects when learner attempts
    /// already exist, and (when <paramref name="replaceExisting"/> is false) rejects
    /// rather than clobbering a paper that already has authored questions. Returns
    /// the re-read structure plus the publish-gate report.
    /// </summary>
    Task<ListeningStructureImportResult> ImportManifestAsync(
        string paperId,
        ListeningStructureManifest manifest,
        bool replaceExisting,
        string adminId,
        CancellationToken ct);

    /// <summary>
    /// Ensure a Part A sub-part (A1 | A2) has answer-key question slots matching
    /// its authored gap count, so the answer-key column populates. Creates the
    /// missing short-answer questions with canonical OET numbering (A1 → 1..12,
    /// A2 → 13..24) and empty answers for the operator to fill; never touches
    /// Part B/C or existing answers. No-op when the slots already exist. Rejects
    /// when learner attempts exist. Returns the re-read structure.
    /// </summary>
    Task<ListeningAuthoredQuestionList> EnsurePartASlotsAsync(
        string paperId,
        string subCode,
        int count,
        string adminId,
        CancellationToken ct);

    /// <summary>
    /// WS5: export the current authored structure (questions + extract metadata)
    /// as a spec §19 manifest suitable for round-tripping back through
    /// <see cref="ImportManifestAsync"/>.
    /// </summary>
    Task<ListeningStructureManifest> ExportManifestAsync(string paperId, CancellationToken ct);
}

/// <summary>
/// Per-question PATCH body. Every field is nullable so admins can ship a
/// minimal diff; null fields are left untouched on the existing row.
/// </summary>
public sealed record ListeningQuestionPatch(
    string? PartCode = null,
    string? Type = null,
    string? Stem = null,
    IReadOnlyList<string>? Options = null,
    string? CorrectAnswer = null,
    IReadOnlyList<string>? AcceptedAnswers = null,
    string? Explanation = null,
    string? SkillTag = null,
    string? TranscriptExcerpt = null,
    string? DistractorExplanation = null,
    int? Points = null,
    IReadOnlyList<string?>? OptionDistractorWhy = null,
    IReadOnlyList<string?>? OptionDistractorCategory = null,
    string? SpeakerAttitude = null,
    int? TranscriptEvidenceStartMs = null,
    int? TranscriptEvidenceEndMs = null);

/// <summary>
/// Per-extract PATCH body. Every field is nullable so admins can ship a
/// minimal diff; null fields are left untouched on the existing row.
/// </summary>
public sealed record ListeningExtractPatch(
    int? DisplayOrder = null,
    string? Kind = null,
    string? Title = null,
    string? AccentCode = null,
    IReadOnlyList<ListeningAuthoredSpeaker>? Speakers = null,
    int? AudioStartMs = null,
    int? AudioEndMs = null,
    int? TimeLimitSeconds = null,
    [property: JsonPropertyName("notesBody")]
    string? NotesBodyMarkdown = null,
    // Phase 6: Part A authoring method ("wysiwyg" | "pdf_overlay") + the
    // normalized PDF-overlay blank placements (JSON). Patched together with the
    // method when the operator authors via the PDF-overlay editor.
    string? AuthoringMethod = null,
    string? PartAOverlayBlanksJson = null,
    // Part B/C printed scenario/intro line ("You hear a nurse briefing…"), shown
    // once per extract on the learner card so the question-paper PDF can be dropped.
    string? ContextIntro = null);

/// <summary>Server-side admin DTO for an authored Listening item. Carries the
/// correct answer + accepted synonyms, so it MUST NOT be returned by any
/// learner-facing endpoint.</summary>
public sealed record ListeningAuthoredQuestion(
    string Id,
    int Number,
    string PartCode,           // A1 | A2 | B | C1 | C2  (also accepts legacy A / C)
    string Type,               // "short_answer" | "multiple_choice_3"
    string Stem,
    IReadOnlyList<string>? Options,
    string CorrectAnswer,
    IReadOnlyList<string>? AcceptedAnswers,
    string? Explanation,
    string? SkillTag,
    string? TranscriptExcerpt,
    string? DistractorExplanation,
    int Points,
    // Phase 4: per-option (Part B/C) "why wrong" + distractor category enum
    // (too_strong | too_weak | wrong_speaker | opposite_meaning | reused_keyword | out_of_scope).
    IReadOnlyList<string?>? OptionDistractorWhy = null,
    IReadOnlyList<string?>? OptionDistractorCategory = null,
    // Phase 4: speaker attitude tag for Part C
    // (concerned | optimistic | doubtful | critical | neutral | other).
    string? SpeakerAttitude = null,
    // Phase 5: time-coded transcript evidence (start/end ms in section audio).
    int? TranscriptEvidenceStartMs = null,
    int? TranscriptEvidenceEndMs = null);

public sealed record ListeningAuthoredQuestionList(
    IReadOnlyList<ListeningAuthoredQuestion> Questions,
    ListeningValidationCounts Counts);

/// <summary>
/// Phase 5 tail: paper-level extract metadata. One row per extract
/// (A1, A2, B clip, C1, C2). Drives accent / speakers / audio-window UI
/// surfaces in the player + review.
/// </summary>
public sealed record ListeningAuthoredExtract(
    string PartCode,                                   // A1 | A2 | B1..B6 | C1 | C2
    int DisplayOrder,
    string Kind,                                       // consultation | workplace | presentation
    string Title,
    string? AccentCode,                                // e.g. en-GB | en-AU | en-IE | en-US
    IReadOnlyList<ListeningAuthoredSpeaker> Speakers,
    int? AudioStartMs,
    int? AudioEndMs,
    int? TimeLimitSeconds = null,                      // single per-sub-section countdown (seconds)
    // OET Listening Part A only — the full note-completion document for this
    // consultation extract (intro line, ## headings, - bullets, ____ gap markers).
    // Forced null for Part B/C. The wire/JSON key is camelCase `notesBody`.
    [property: JsonPropertyName("notesBody")]
    string? NotesBodyMarkdown = null,
    // Phase 6: Part A authoring method ("wysiwyg" default | "pdf_overlay") and,
    // for pdf_overlay, the normalized blank placements over the question-paper PDF.
    string? AuthoringMethod = null,
    string? PartAOverlayBlanksJson = null,
    // Part B/C printed scenario/intro line ("You hear a nurse briefing…"), shown
    // once per extract on the learner card so the question-paper PDF can be dropped.
    string? ContextIntro = null);

public sealed record ListeningAuthoredSpeaker(
    string Id,
    string Role,
    string? Gender,                                    // m | f | nb | null
    string? Accent);

// ── WS5: spec §19 import/export manifest ────────────────────────────────────
//
// Mirrors `ReadingStructureManifest` but follows the §19 Listening shape:
// a test-level header plus per-part extract arrays. Part A extracts carry
// note-completion gap questions; Part B/C extracts carry single MCQ-3 items.
// The manifest is the round-trip contract between the admin import UI and the
// authored question + extract documents under ContentPaper.ExtractedTextJson.

public sealed record ListeningStructureManifest(
    string? TestTitle,
    IReadOnlyList<string>? ModeSupport,
    bool? StrictMock,
    ListeningPartManifest? PartA,
    ListeningPartManifest? PartB,
    ListeningPartManifest? PartC);

public sealed record ListeningPartManifest(
    IReadOnlyList<ListeningExtractManifest> Extracts);

public sealed record ListeningExtractManifest(
    int ExtractNumber,                                 // 1-based within the part
    string? QuestionNumber,                            // Part B convenience (e.g. "25")
    string? QuestionRange,                             // Part C convenience (e.g. "31-36")
    string? PatientName,                               // Part A
    string? ProfessionalRole,                          // Part A
    string? Context,                                   // Part B
    string? Topic,                                     // Part C
    string? Format,                                    // Part C — interview | presentation
    string? AudioFile,
    int? ReadingTimeSeconds,
    string? Transcript,
    string? AccentCode,
    string? SpeakerAttitude,                           // Part C — concerned | optimistic | …
    IReadOnlyList<ListeningTranscriptSegmentManifest>? TranscriptSegments,
    IReadOnlyList<ListeningAuthoredSpeaker>? Speakers,
    IReadOnlyList<ListeningQuestionManifest> Questions,
    string? NotesBody = null);                          // Part A note-completion body (round-trips)

public sealed record ListeningTranscriptSegmentManifest(
    int StartMs,
    int EndMs,
    string? SpeakerId,
    string? Text);

public sealed record ListeningQuestionManifest(
    int Number,
    string? Type,                                      // gap_fill | short_answer | multiple_choice_3
    string? NoteTextBeforeGap,                         // Part A note-completion lead-in
    string? Stem,                                      // Part B/C question stem
    ListeningOptionsManifest? Options,                 // Part B/C A/B/C options
    string? CorrectAnswer,
    IReadOnlyList<string>? AcceptedAnswers,
    string? Explanation,
    string? DistractorExplanation,
    string? SkillTag,
    string? Timestamp,                                 // legacy single timestamp ("mm:ss")
    int? TranscriptEvidenceStartMs,
    int? TranscriptEvidenceEndMs,
    string? TranscriptExcerpt,
    IReadOnlyList<string?>? OptionDistractorWhy,
    IReadOnlyList<string?>? OptionDistractorCategory);

public sealed record ListeningOptionsManifest(
    string? A,
    string? B,
    string? C);

public sealed record ListeningStructureImportResult(
    ListeningAuthoredQuestionList Structure,
    ListeningValidationReport Report);

public sealed class ListeningAuthoringService(
    LearnerDbContext db,
    IListeningBackfillService backfill) : IListeningAuthoringService
{
    private const string QuestionsKey = "listeningQuestions";

    /// <summary>
    /// Gap W4: when relational ListeningQuestion rows already exist for a
    /// paper, re-run the backfill so the projected rows track the JSON blob
    /// after every Approve / Replace / Patch. No-op when no relational rows
    /// exist yet. If learner attempts already exist, backfill refuses to
    /// rewrite relational rows; surface that as a conflict so JSON and
    /// relational runtime state cannot silently diverge.
    /// </summary>
    private async Task ResyncRelationalIfNeededAsync(string paperId, string adminId, CancellationToken ct)
    {
        var hasRelational = await HasRelationalProjectionAsync(paperId, ct);
        if (!hasRelational) return;
        var report = await backfill.BackfillPaperAsync(paperId, adminId, ct);
        if (!report.Success)
        {
            throw ApiException.Conflict(
                "listening_relational_resync_blocked",
                report.Reason ?? "Listening relational projection could not be refreshed; create a new paper revision before editing this paper.");
        }
    }

    private async Task EnsureRelationalResyncCanRunBeforeMutationAsync(string paperId, CancellationToken ct)
    {
        var hasRelational = await HasRelationalProjectionAsync(paperId, ct);
        if (!hasRelational) return;
        var hasAttempts = await db.ListeningAttempts.AnyAsync(a => a.PaperId == paperId, ct);
        if (!hasAttempts) return;
        throw ApiException.Conflict(
            "listening_relational_resync_blocked",
            "This Listening paper already has learner attempts. Create a new paper revision before changing authored questions or extract metadata.");
    }

    private async Task<bool> HasRelationalProjectionAsync(string paperId, CancellationToken ct)
    {
        if (await db.ListeningQuestions.AnyAsync(q => q.PaperId == paperId, ct)) return true;
        return await db.ListeningParts.AnyAsync(part => part.PaperId == paperId, ct);
    }

    public async Task<ListeningAuthoredQuestionList> GetStructureAsync(string paperId, CancellationToken ct)
    {
        var paper = await db.ContentPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");

        var raw = ReadQuestionsArray(paper.ExtractedTextJson);
        var items = raw.Select(NormalizeFromStorage).ToList();
        return new ListeningAuthoredQuestionList(items, Tally(items));
    }

    public async Task<ListeningAuthoredQuestionList> ReplaceStructureAsync(
        string paperId,
        IReadOnlyList<ListeningAuthoredQuestion> questions,
        string adminId,
        CancellationToken ct)
    {
        await EnsureRelationalResyncCanRunBeforeMutationAsync(paperId, ct);
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        try
        {
        var paper = await db.ContentPapers.FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");

        // H6: Enforce SourceProvenance at content-mutation time (not only at publish).
        // This prevents content from being written without an attestation of origin.
        if (string.IsNullOrWhiteSpace(paper.SourceProvenance))
        {
            throw ApiException.Validation(
                "listening_provenance_required",
                "SourceProvenance must be set before uploading or modifying paper content. " +
                "Set the content source attestation on the paper first.");
        }

        // Deserialize whatever ExtractedTextJson currently holds so we don't
        // clobber sibling keys (e.g. an AI-extraction pipeline may have stored
        // metadata, transcripts, audio offsets etc. alongside listeningQuestions).
        Dictionary<string, JsonElement> root;
        if (string.IsNullOrWhiteSpace(paper.ExtractedTextJson))
        {
            root = new Dictionary<string, JsonElement>();
        }
        else
        {
            try
            {
                root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(paper.ExtractedTextJson)
                       ?? new Dictionary<string, JsonElement>();
            }
            catch (JsonException)
            {
                // Existing payload is corrupt — start clean rather than refuse the save.
                root = new Dictionary<string, JsonElement>();
            }
        }

        var normalized = questions
            .Select(NormalizeForStorage)
            .OrderBy(q => q.Number)
            .ToList();

        var serialized = JsonSerializer.SerializeToElement(normalized, CamelJson);
        root[QuestionsKey] = serialized;

        paper.ExtractedTextJson = JsonSerializer.Serialize(root);
        paper.UpdatedAt = DateTimeOffset.UtcNow;
        paper.RowVersion++;

        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit_{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = adminId,
            ActorAuthAccountId = await db.ResolveActorAuthAccountIdAsync(adminId, ct),
            ActorName = adminId,
            Action = "ListeningStructureUpdated",
            ResourceType = "ContentPaper",
            ResourceId = paper.Id,
            Details = JsonSerializer.Serialize(new
            {
                count = normalized.Count,
                partA = normalized.Count(q => NormalizePartCode(q.PartCode).StartsWith('A')),
                partB = normalized.Count(q => NormalizePartCode(q.PartCode).StartsWith('B')),
                partC = normalized.Count(q => NormalizePartCode(q.PartCode).StartsWith('C')),
            }),
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ApiException.Conflict("content_paper_concurrent_update",
                "This paper was modified by another admin. Reload and retry.");
        }

        // Gap W4: keep relational ListeningQuestion / ListeningQuestionOption
        // rows in sync with the JSON blob so the publish gate (relational-first)
        // doesn't read stale tallies after every Approve/Replace.
        await ResyncRelationalIfNeededAsync(paperId, adminId, ct);

        if (tx is not null) await tx.CommitAsync(ct);

        return new ListeningAuthoredQuestionList(normalized, Tally(normalized));
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<ListeningAuthoredQuestionList> EnsurePartASlotsAsync(
        string paperId,
        string subCode,
        int count,
        string adminId,
        CancellationToken ct)
    {
        var code = (subCode ?? string.Empty).Trim().ToUpperInvariant();
        if (code != "A1" && code != "A2")
            throw ApiException.Validation("listening_parta_bad_subcode", "subCode must be A1 or A2.");
        // Each Part A consultation is exactly 12 gaps in OET; cap defensively so
        // A2 numbering never spills past Q24 into Part B's range.
        if (count < 1 || count > 12)
            throw ApiException.Validation("listening_parta_bad_count", "count must be between 1 and 12.");

        // Structure can't change once learners have attempted the paper.
        if (await db.ListeningAttempts.AnyAsync(a => a.PaperId == paperId, ct))
        {
            throw ApiException.Conflict(
                "listening_manifest_attempts_exist",
                "Learner attempts already exist for this paper, so its structure can't be changed.");
        }

        var current = await GetStructureAsync(paperId, ct);
        var questions = current.Questions.ToList();
        var baseNumber = code == "A1" ? 1 : 13;

        var existingForSub = questions
            .Where(q => NormalizePartCode(q.PartCode) == code)
            .Select(q => q.Number)
            .ToHashSet();
        var allNumbers = questions.Select(q => q.Number).ToHashSet();

        var added = false;
        for (var i = 0; i < count; i++)
        {
            var number = baseNumber + i;
            // Skip if this sub-part already has the slot, or the number is taken
            // by any other question (the (PaperId, Number) unique index).
            if (existingForSub.Contains(number) || allNumbers.Contains(number)) continue;
            questions.Add(new ListeningAuthoredQuestion(
                Id: $"lq_{Guid.NewGuid():N}",
                Number: number,
                PartCode: code,
                Type: "short_answer",
                Stem: string.Empty,
                Options: Array.Empty<string>(),
                CorrectAnswer: string.Empty,
                AcceptedAnswers: Array.Empty<string>(),
                Explanation: null,
                SkillTag: null,
                TranscriptExcerpt: null,
                DistractorExplanation: null,
                Points: 1));
            allNumbers.Add(number);
            added = true;
        }

        if (!added) return current;

        // ReplaceStructureAsync enforces SourceProvenance (H6). Manual authoring
        // may not have set it yet — stamp a default so slot creation isn't blocked.
        var paper = await db.ContentPapers.FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw ApiException.NotFound("listening_paper_not_found", "Paper not found.");
        if (string.IsNullOrWhiteSpace(paper.SourceProvenance))
        {
            paper.SourceProvenance = "Manual Part A authoring";
            paper.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return await ReplaceStructureAsync(paperId, questions, adminId, ct);
    }

    // ── WS5: spec §19 manifest import / export ───────────────────────────

    public async Task<ListeningStructureImportResult> ImportManifestAsync(
        string paperId,
        ListeningStructureManifest manifest,
        bool replaceExisting,
        string adminId,
        CancellationToken ct)
    {
        if (manifest is null)
            throw ApiException.Validation("listening_manifest_required", "Listening manifest is required.");

        var paper = await db.ContentPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw ApiException.NotFound("listening_paper_not_found", "Paper not found.");
        if (!string.Equals(paper.SubtestCode, "listening", StringComparison.OrdinalIgnoreCase))
            throw ApiException.Validation(
                "listening_wrong_subtest",
                $"Paper subtest is '{paper.SubtestCode}', expected 'listening'.");

        var (questions, extracts) = NormalizeManifest(manifest);
        if (questions.Count == 0)
            throw ApiException.Validation(
                "listening_manifest_empty",
                "Listening manifest must contain at least one question across partA / partB / partC.");

        // Mirror Reading: a full-test manifest cannot be re-imported on top of a
        // paper that learners have already attempted — retire it and import into
        // a new revision instead.
        var hasAttempts = await db.ListeningAttempts.AnyAsync(a => a.PaperId == paperId, ct);
        if (hasAttempts)
            throw ApiException.Conflict(
                "listening_manifest_attempts_exist",
                "Cannot import a Listening manifest after learner attempts exist. Retire this paper and import into a new revision instead.");

        // Additive-vs-reject contract (Reading parity): the Listening manifest is
        // a whole-test document and ReplaceStructureAsync rewrites the entire
        // question array, so when replaceExisting is false we refuse rather than
        // clobber an already-authored paper. With replaceExisting=true we proceed
        // and the replace path overwrites the structure wholesale.
        if (!replaceExisting)
        {
            var existing = ReadQuestionsArray(paper.ExtractedTextJson);
            if (existing.Count > 0)
                throw ApiException.Conflict(
                    "listening_manifest_already_authored",
                    "This paper already has an authored Listening structure. Enable \"replace existing\" to overwrite it with this manifest.");
        }

        // ReplaceStructureAsync + ReplaceExtractsAsync each manage their own
        // transaction, enforce SourceProvenance, bump RowVersion, write their own
        // audit events, and resync the relational projection. Calling them in
        // sequence keeps the §19 import path on exactly the same write contract as
        // hand-editing the structure + extracts.
        var structure = await ReplaceStructureAsync(paperId, questions, adminId, ct);
        await ReplaceExtractsAsync(paperId, extracts, adminId, ct);

        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit_{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = adminId,
            ActorAuthAccountId = await db.ResolveActorAuthAccountIdAsync(adminId, ct),
            ActorName = adminId,
            Action = "ListeningManifestImported",
            ResourceType = "ContentPaper",
            ResourceId = paperId,
            Details = JsonSerializer.Serialize(new
            {
                replaceExisting,
                testTitle = manifest.TestTitle,
                questions = questions.Count,
                extracts = extracts.Count,
            }),
        });
        await db.SaveChangesAsync(ct);

        var report = await new ListeningStructureService(db).ValidatePaperAsync(paperId, ct);
        return new ListeningStructureImportResult(structure, report);
    }

    public async Task<ListeningStructureManifest> ExportManifestAsync(string paperId, CancellationToken ct)
    {
        var paper = await db.ContentPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw ApiException.NotFound("listening_paper_not_found", "Paper not found.");

        var structure = await GetStructureAsync(paperId, ct);
        var extracts = await GetExtractsAsync(paperId, ct);
        return BuildManifest(paper.Title, structure.Questions, extracts);
    }

    // ── WS5: manifest ↔ authored-shape normalisation ─────────────────────

    /// <summary>
    /// Flatten a §19 manifest into the authored question + extract lists the
    /// existing replace paths consume. Part A note-completion gaps fold their
    /// <c>noteTextBeforeGap</c> into the stem with a trailing <c>____</c> marker;
    /// Part B/C options A/B/C become a 3-element option list and the correct
    /// answer is normalised to a single letter.
    /// </summary>
    private static (List<ListeningAuthoredQuestion> Questions, List<ListeningAuthoredExtract> Extracts)
        NormalizeManifest(ListeningStructureManifest manifest)
    {
        var questions = new List<ListeningAuthoredQuestion>();
        var extracts = new List<ListeningAuthoredExtract>();

        void Project(ListeningPartManifest? part, string partGroup)
        {
            if (part?.Extracts is null) return;
            // partGroup is "A" | "B" | "C". Each manifest extract entry maps to a
            // sub-section code (A1/A2, B1..B6, C1/C2 by extract ordinal) and emits
            // its own extract row, so every sub-section carries its own audio /
            // timer / questions. Part B is no longer collapsed to one row.
            var ordered = part.Extracts.OrderBy(e => e.ExtractNumber).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var extract = ordered[i];
                var partCode = ResolvePartCode(partGroup, extract.ExtractNumber, i);
                foreach (var qm in extract.Questions ?? [])
                {
                    // Part C carries speaker-attitude at the extract level; fold it
                    // onto each of the extract's questions so the per-question field
                    // (the authored-shape home for attitude) is populated.
                    questions.Add(NormalizeManifestQuestion(
                        qm, partCode, partCode.StartsWith('C') ? extract.SpeakerAttitude : null));
                }
                extracts.Add(NormalizeManifestExtract(extract, partCode, i));
            }
        }

        Project(manifest.PartA, "A");
        Project(manifest.PartB, "B");
        Project(manifest.PartC, "C");

        return (questions, extracts);
    }

    /// <summary>A/C split into A1/A2 and C1/C2 by extract ordinal; B is flat.
    /// Prefer the authored <c>extractNumber</c> (1 → first slot) but fall back to
    /// positional order so a manifest that numbers Part C 1/2 or 31/37 still
    /// maps cleanly.</summary>
    private static string ResolvePartCode(string partGroup, int extractNumber, int positionalIndex)
    {
        if (partGroup == "B")
        {
            // Part B splits into B1..B6 (one clip each). Prefer the authored
            // 1-based extractNumber; fall back to positional order; clamp 1..6.
            var bSlot = extractNumber is >= 1 and <= 6 ? extractNumber : positionalIndex + 1;
            return $"B{Math.Min(Math.Max(bSlot, 1), 6)}";
        }
        var slot = extractNumber is 1 or 2 ? extractNumber - 1 : positionalIndex;
        var which = slot <= 0 ? 1 : 2;
        return $"{partGroup}{which}";
    }

    private static ListeningAuthoredQuestion NormalizeManifestQuestion(
        ListeningQuestionManifest qm, string partCode, string? speakerAttitude)
    {
        var isMcq = partCode.StartsWith('B') || partCode.StartsWith('C');
        var type = NormalizeManifestQuestionType(qm.Type, isMcq);

        // Part A: fold the note lead-in into the stem with a gap marker so the
        // note-completion player has a renderable prompt. Part B/C: use the stem.
        string stem;
        if (!isMcq)
        {
            var lead = (qm.NoteTextBeforeGap ?? qm.Stem ?? string.Empty).Trim();
            stem = string.IsNullOrEmpty(lead)
                ? "____"
                : lead.Contains("____", StringComparison.Ordinal) ? lead : $"{lead} ____";
        }
        else
        {
            stem = (qm.Stem ?? qm.NoteTextBeforeGap ?? string.Empty).Trim();
        }

        var options = isMcq && qm.Options is not null
            ? new List<string>
            {
                qm.Options.A ?? string.Empty,
                qm.Options.B ?? string.Empty,
                qm.Options.C ?? string.Empty,
            }
            : new List<string>();

        // Evidence start/end: prefer explicit ms; otherwise derive a start from a
        // legacy "mm:ss" timestamp so round-tripped excerpts keep a cue point.
        var evidenceStart = qm.TranscriptEvidenceStartMs ?? ParseTimestampMs(qm.Timestamp);
        var evidenceEnd = qm.TranscriptEvidenceEndMs;

        return new ListeningAuthoredQuestion(
            Id: $"lq-{qm.Number}",
            Number: qm.Number,
            PartCode: partCode,
            Type: type,
            Stem: stem,
            Options: options,
            CorrectAnswer: NormalizeManifestCorrectAnswer(qm.CorrectAnswer, isMcq),
            AcceptedAnswers: (qm.AcceptedAnswers ?? []).ToList(),
            Explanation: qm.Explanation,
            SkillTag: qm.SkillTag,
            TranscriptExcerpt: qm.TranscriptExcerpt,
            DistractorExplanation: qm.DistractorExplanation,
            Points: 1,
            OptionDistractorWhy: qm.OptionDistractorWhy?.ToList(),
            OptionDistractorCategory: qm.OptionDistractorCategory?.ToList(),
            SpeakerAttitude: speakerAttitude,
            TranscriptEvidenceStartMs: evidenceStart,
            TranscriptEvidenceEndMs: evidenceEnd);
    }

    private static ListeningAuthoredExtract NormalizeManifestExtract(
        ListeningExtractManifest extract, string partCode, int positionalIndex)
    {
        // Build a sensible title from the §19 per-part identity fields.
        var title = (extract.Topic
            ?? extract.Context
            ?? (string.IsNullOrWhiteSpace(extract.PatientName) ? null : $"{extract.PatientName} consultation")
            ?? $"{partCode} extract").Trim();

        var kind = partCode.StartsWith('B') ? "workplace"
            : partCode is "C1" or "C2" ? "presentation"
            : "consultation";

        var speakers = (extract.Speakers ?? []).ToList();

        // Part A carries the note-completion body. Prefer the explicit
        // extract-level notesBody; when absent/blank, synthesise a body from the
        // legacy per-question noteTextBeforeGap lead-ins (NormalizeForStorage
        // forces this null for Part B/C, so passing it through is safe).
        var notesBody = partCode.StartsWith('A')
            ? (string.IsNullOrWhiteSpace(extract.NotesBody)
                ? SynthesizeNotesBodyFromQuestions(extract.Questions)
                : extract.NotesBody)
            : null;

        return new ListeningAuthoredExtract(
            PartCode: partCode,
            DisplayOrder: positionalIndex,
            Kind: kind,
            Title: title,
            AccentCode: extract.AccentCode,
            Speakers: speakers,
            AudioStartMs: null,
            AudioEndMs: null,
            TimeLimitSeconds: null,
            NotesBodyMarkdown: notesBody);
    }

    /// <summary>
    /// Legacy fallback: when a Part A extract has no explicit <c>notesBody</c>
    /// but its questions carry the older per-question <c>noteTextBeforeGap</c>
    /// lead-ins, synthesise a note-completion body by joining, in question-number
    /// order, each lead-in followed by a <c>____</c> gap marker (one line per
    /// question). Returns null when no lead-in text is present.
    /// </summary>
    private static string? SynthesizeNotesBodyFromQuestions(
        IReadOnlyList<ListeningQuestionManifest>? questions)
    {
        if (questions is null || questions.Count == 0) return null;
        var lines = questions
            .OrderBy(q => q.Number)
            .Select(q =>
            {
                var lead = (q.NoteTextBeforeGap ?? string.Empty).Trim();
                return lead.Length == 0 ? "____" : $"{lead} ____";
            })
            .ToList();
        if (lines.All(line => line == "____")) return null;
        return string.Join("\n", lines);
    }

    private static string NormalizeManifestQuestionType(string? raw, bool isMcq)
    {
        var n = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (n is "multiple_choice_3" or "mcq" or "mcq3") return "multiple_choice_3";
        if (n is "short_answer" or "gap_fill" or "gapfill" or "note_completion") return "short_answer";
        return isMcq ? "multiple_choice_3" : "short_answer";
    }

    private static string NormalizeManifestCorrectAnswer(string? raw, bool isMcq)
    {
        var value = (raw ?? string.Empty).Trim();
        if (!isMcq) return value;
        // Part B/C answers are an option letter; uppercase a single A/B/C.
        return value.Length == 1 ? value.ToUpperInvariant() : value;
    }

    private static int? ParseTimestampMs(string? timestamp)
    {
        if (string.IsNullOrWhiteSpace(timestamp)) return null;
        var parts = timestamp.Trim().Split(':');
        try
        {
            return parts.Length switch
            {
                1 when int.TryParse(parts[0], out var s) => s * 1000,
                2 when int.TryParse(parts[0], out var m) && int.TryParse(parts[1], out var s) => (m * 60 + s) * 1000,
                3 when int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m) && int.TryParse(parts[2], out var s)
                    => ((h * 60 + m) * 60 + s) * 1000,
                _ => (int?)null,
            };
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// Rebuild a §19 manifest from the authored question + extract documents.
    /// Questions group under their extract by part code (A1/A2 → partA,
    /// B → partB, C1/C2 → partC); Part A questions emit <c>noteTextBeforeGap</c>,
    /// Part B/C emit the stem + A/B/C options.
    /// </summary>
    private static ListeningStructureManifest BuildManifest(
        string? testTitle,
        IReadOnlyList<ListeningAuthoredQuestion> questions,
        IReadOnlyList<ListeningAuthoredExtract> extracts)
    {
        ListeningPartManifest? BuildPart(params string[] partCodes)
        {
            var manifestExtracts = new List<ListeningExtractManifest>();
            var extractNumber = 0;
            foreach (var code in partCodes)
            {
                var extract = extracts.FirstOrDefault(e =>
                    string.Equals(e.PartCode, code, StringComparison.OrdinalIgnoreCase));
                var partQuestions = questions
                    .Where(q => string.Equals(q.PartCode, code, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(q => q.Number)
                    .ToList();
                if (extract is null && partQuestions.Count == 0) continue;

                extractNumber++;
                var isMcq = code.StartsWith('B') || code.StartsWith('C');
                var manifestQuestions = partQuestions
                    .Select(q => BuildManifestQuestion(q, isMcq))
                    .ToList();

                var numbers = partQuestions.Select(q => q.Number).ToList();
                var isPartB = code.StartsWith('B');
                manifestExtracts.Add(new ListeningExtractManifest(
                    ExtractNumber: extractNumber,
                    // Each Part B sub-section carries its single question number.
                    QuestionNumber: isPartB && numbers.Count > 0 ? numbers[0].ToString() : null,
                    QuestionRange: code.StartsWith('C') && numbers.Count > 0
                        ? $"{numbers[0]}-{numbers[^1]}"
                        : null,
                    PatientName: null,
                    ProfessionalRole: null,
                    Context: isPartB ? extract?.Title : null,
                    Topic: code.StartsWith('C') ? extract?.Title : null,
                    Format: code.StartsWith('C') ? "presentation" : null,
                    AudioFile: null,
                    ReadingTimeSeconds: code.StartsWith('C') ? 90 : isPartB ? 0 : 30,
                    Transcript: null,
                    AccentCode: extract?.AccentCode,
                    SpeakerAttitude: code.StartsWith('C')
                        ? partQuestions.Select(q => q.SpeakerAttitude).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a))
                        : null,
                    TranscriptSegments: null,
                    Speakers: extract?.Speakers,
                    Questions: manifestQuestions,
                    // Part A note-completion body round-trips back out of the
                    // authored extract; Part B/C never carry one.
                    NotesBody: code.StartsWith('A') ? extract?.NotesBodyMarkdown : null));
            }
            return manifestExtracts.Count == 0 ? null : new ListeningPartManifest(manifestExtracts);
        }

        return new ListeningStructureManifest(
            TestTitle: testTitle,
            ModeSupport: new[] { "paper", "computer" },
            StrictMock: true,
            PartA: BuildPart("A1", "A2"),
            // Part B is six independent sub-sections (B1..B6), each its own
            // manifest extract carrying a single question.
            PartB: BuildPart("B1", "B2", "B3", "B4", "B5", "B6"),
            PartC: BuildPart("C1", "C2"));
    }

    private static ListeningQuestionManifest BuildManifestQuestion(ListeningAuthoredQuestion q, bool isMcq)
    {
        var options = isMcq && q.Options is { Count: >= 1 }
            ? new ListeningOptionsManifest(
                q.Options.ElementAtOrDefault(0),
                q.Options.ElementAtOrDefault(1),
                q.Options.ElementAtOrDefault(2))
            : null;

        return new ListeningQuestionManifest(
            Number: q.Number,
            Type: q.Type,
            NoteTextBeforeGap: isMcq ? null : q.Stem,
            Stem: isMcq ? q.Stem : null,
            Options: options,
            CorrectAnswer: q.CorrectAnswer,
            AcceptedAnswers: q.AcceptedAnswers?.ToList(),
            Explanation: q.Explanation,
            DistractorExplanation: q.DistractorExplanation,
            SkillTag: q.SkillTag,
            Timestamp: null,
            TranscriptEvidenceStartMs: q.TranscriptEvidenceStartMs,
            TranscriptEvidenceEndMs: q.TranscriptEvidenceEndMs,
            TranscriptExcerpt: q.TranscriptExcerpt,
            OptionDistractorWhy: q.OptionDistractorWhy?.ToList(),
            OptionDistractorCategory: q.OptionDistractorCategory?.ToList());
    }

    // ── Phase 5 tail: extract metadata (accent + speakers) ──────────────

    private const string ExtractsKey = "listeningExtracts";

    public async Task<IReadOnlyList<ListeningAuthoredExtract>> GetExtractsAsync(string paperId, CancellationToken ct)
    {
        var paper = await db.ContentPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");

        return ReadExtractsArray(paper.ExtractedTextJson)
            .Select((e, i) => NormalizeExtractFromStorage(e, i))
            .OrderBy(e => PartCodeOrder(e.PartCode))
            .ThenBy(e => e.DisplayOrder)
            .ToList();
    }

    public async Task<IReadOnlyList<ListeningAuthoredExtract>> ReplaceExtractsAsync(
        string paperId,
        IReadOnlyList<ListeningAuthoredExtract> extracts,
        string adminId,
        CancellationToken ct)
    {
        await EnsureRelationalResyncCanRunBeforeMutationAsync(paperId, ct);
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        try
        {
        var paper = await db.ContentPapers.FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw new InvalidOperationException("Paper not found.");

        Dictionary<string, JsonElement> root;
        if (string.IsNullOrWhiteSpace(paper.ExtractedTextJson))
        {
            root = new Dictionary<string, JsonElement>();
        }
        else
        {
            try
            {
                root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(paper.ExtractedTextJson)
                       ?? new Dictionary<string, JsonElement>();
            }
            catch (JsonException)
            {
                root = new Dictionary<string, JsonElement>();
            }
        }

        var normalized = extracts
            .Select(NormalizeExtractForStorage)
            .OrderBy(e => PartCodeOrder(e.PartCode))
            .ThenBy(e => e.DisplayOrder)
            .ToList();
        var duplicateParts = normalized
            .GroupBy(e => e.PartCode, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicateParts.Count > 0)
        {
            throw ApiException.Validation(
                "listening_extract_duplicate_part",
                $"Listening extract metadata must contain one row per part. Duplicate parts: {string.Join(", ", duplicateParts)}.");
        }

        // Persist with camelCase JSON property names so the read-back path
        // (which inspects keys like "partCode", "accentCode", etc.) sees the
        // same shape an external author or AI extractor would write.
        var camelOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var serialized = JsonSerializer.SerializeToElement(normalized, camelOptions);
        root[ExtractsKey] = serialized;

        paper.ExtractedTextJson = JsonSerializer.Serialize(root);
        paper.UpdatedAt = DateTimeOffset.UtcNow;
        paper.RowVersion++;

        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit_{Guid.NewGuid():N}",
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = adminId,
            ActorAuthAccountId = await db.ResolveActorAuthAccountIdAsync(adminId, ct),
            ActorName = adminId,
            Action = "ListeningExtractsUpdated",
            ResourceType = "ContentPaper",
            ResourceId = paper.Id,
            Details = JsonSerializer.Serialize(new
            {
                count = normalized.Count,
                accents = normalized.Where(e => !string.IsNullOrWhiteSpace(e.AccentCode))
                                    .Select(e => e.AccentCode!).Distinct().ToList(),
            }),
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ApiException.Conflict("content_paper_concurrent_update",
                "This paper was modified by another admin. Reload and retry.");
        }
        await ResyncRelationalIfNeededAsync(paperId, adminId, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return normalized;
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static List<Dictionary<string, object?>> ReadExtractsArray(string? extractedTextJson)
    {
        if (string.IsNullOrWhiteSpace(extractedTextJson)) return [];
        try
        {
            var root = JsonSerializer.Deserialize<Dictionary<string, object?>>(extractedTextJson);
            var raw = root?.GetValueOrDefault(ExtractsKey);
            if (raw is null) return [];
            return JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(JsonSerializer.Serialize(raw))
                   ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static ListeningAuthoredExtract NormalizeExtractFromStorage(Dictionary<string, object?> e, int index)
    {
        string? Read(string key) => e.GetValueOrDefault(key)?.ToString();
        int? ReadInt(string key)
        {
            var s = Read(key);
            return int.TryParse(s, out var v) && v >= 0 ? v : (int?)null;
        }

        var partCode = NormalizeExtractPartCode(Read("partCode")) ?? "A1";
        var displayOrder = ReadInt("displayOrder") ?? index;
        var kind = NormalizeExtractKind(Read("kind"), partCode);

        var speakersRaw = e.GetValueOrDefault("speakers");
        var speakers = ParseAuthoredSpeakers(speakersRaw);

        return new ListeningAuthoredExtract(
            PartCode: partCode,
            DisplayOrder: displayOrder,
            Kind: kind,
            Title: Read("title") ?? $"Extract {index + 1}",
            AccentCode: Read("accentCode"),
            Speakers: speakers,
            AudioStartMs: ReadInt("audioStartMs"),
            AudioEndMs: ReadInt("audioEndMs"),
            TimeLimitSeconds: ReadInt("timeLimitSeconds"),
            NotesBodyMarkdown: Read("notesBody"),
            AuthoringMethod: Read("authoringMethod"),
            PartAOverlayBlanksJson: Read("partAOverlayBlanksJson"),
            ContextIntro: Read("contextIntro"));
    }

    private static ListeningAuthoredExtract NormalizeExtractForStorage(ListeningAuthoredExtract e)
    {
        var partCode = NormalizeExtractPartCode(e.PartCode) ?? "A1";
        var kind = NormalizeExtractKind(e.Kind, partCode);
        var displayOrder = Math.Max(0, e.DisplayOrder);
        var title = (e.Title ?? string.Empty).Trim();
        if (title.Length == 0) title = $"{partCode} extract";
        var accentCode = string.IsNullOrWhiteSpace(e.AccentCode) ? null : e.AccentCode.Trim();

        var speakers = (e.Speakers ?? [])
            .Select((s, idx) => new ListeningAuthoredSpeaker(
                Id: string.IsNullOrWhiteSpace(s.Id) ? $"s{idx + 1}" : s.Id.Trim(),
                Role: string.IsNullOrWhiteSpace(s.Role) ? "speaker" : s.Role.Trim(),
                Gender: NormalizeSpeakerGender(s.Gender),
                Accent: string.IsNullOrWhiteSpace(s.Accent) ? null : s.Accent.Trim()))
            .ToList();

        var audioStart = e.AudioStartMs is int sv && sv >= 0 ? sv : (int?)null;
        var audioEnd = e.AudioEndMs is int ev && ev >= 0 ? ev : (int?)null;
        if (audioStart is int s && audioEnd is int en && en < s)
        {
            audioEnd = null;
        }

        var timeLimitSeconds = e.TimeLimitSeconds is int t && t > 0 ? t : (int?)null;

        // Part A (A1/A2) is the only sub-section that carries a note-completion
        // body; Part B/C never do, so force it null there regardless of input.
        var notesBody = IsPartANotesCode(partCode) && !string.IsNullOrWhiteSpace(e.NotesBodyMarkdown)
            ? e.NotesBodyMarkdown
            : null;

        // Phase 6: authoring method + overlay placements are Part A only.
        var authoringMethod = IsPartANotesCode(partCode) && !string.IsNullOrWhiteSpace(e.AuthoringMethod)
            ? e.AuthoringMethod.Trim().ToLowerInvariant()
            : null;
        var overlayBlanks = IsPartANotesCode(partCode) && !string.IsNullOrWhiteSpace(e.PartAOverlayBlanksJson)
            ? e.PartAOverlayBlanksJson
            : null;

        // ContextIntro is the Part B/C scenario line — persisted for ALL parts
        // that carry one (NOT gated Part-A-only like notesBody). Blank → null.
        var contextIntro = string.IsNullOrWhiteSpace(e.ContextIntro) ? null : e.ContextIntro.Trim();

        return new ListeningAuthoredExtract(
            PartCode: partCode,
            DisplayOrder: displayOrder,
            Kind: kind,
            Title: title,
            AccentCode: accentCode,
            Speakers: speakers,
            AudioStartMs: audioStart,
            AudioEndMs: audioEnd,
            TimeLimitSeconds: timeLimitSeconds,
            NotesBodyMarkdown: notesBody,
            AuthoringMethod: authoringMethod,
            PartAOverlayBlanksJson: overlayBlanks,
            ContextIntro: contextIntro);
    }

    /// <summary>True for the two Part A consultation sub-sections (A1/A2) — the
    /// only extracts that carry a note-completion <c>notesBody</c> document.</summary>
    private static bool IsPartANotesCode(string partCode)
        => string.Equals(partCode, "A1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(partCode, "A2", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<ListeningAuthoredSpeaker> ParseAuthoredSpeakers(object? raw)
    {
        if (raw is null) return [];
        try
        {
            var list = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(JsonSerializer.Serialize(raw));
            if (list is null) return [];
            var output = new List<ListeningAuthoredSpeaker>(list.Count);
            for (var i = 0; i < list.Count; i++)
            {
                var s = list[i];
                output.Add(new ListeningAuthoredSpeaker(
                    Id: s.GetValueOrDefault("id")?.ToString() ?? $"s{i + 1}",
                    Role: s.GetValueOrDefault("role")?.ToString() ?? "speaker",
                    Gender: NormalizeSpeakerGender(s.GetValueOrDefault("gender")?.ToString()),
                    Accent: s.GetValueOrDefault("accent")?.ToString()));
            }
            return output;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? NormalizeSpeakerGender(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var n = raw.Trim().ToLowerInvariant();
        return n is "m" or "f" or "nb" ? n : null;
    }

    private static string NormalizeExtractKind(string? raw, string partCode)
    {
        var n = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (n is "consultation" or "workplace" or "presentation") return n;
        return partCode switch
        {
            "B" => "workplace",
            "C1" or "C2" => "presentation",
            _ => "consultation",
        };
    }

    private static string? NormalizeExtractPartCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var n = raw.Trim().ToUpperInvariant();
        return n switch
        {
            "A1" or "A2" or "B1" or "B2" or "B3" or "B4" or "B5" or "B6" or "C1" or "C2" => n,
            "A" => "A1",
            "B" => "B1",
            "C" => "C1",
            _ => null,
        };
    }

    private static int PartCodeOrder(string partCode) => partCode switch
    {
        "A1" => 1,
        "A2" => 2,
        "B1" => 3,
        "B2" => 4,
        "B3" => 5,
        "B4" => 6,
        "B5" => 7,
        "B6" => 8,
        "C1" => 9,
        "C2" => 10,
        _ => 99,
    };

    // ── Helpers ──────────────────────────────────────────────────────────

    private static List<Dictionary<string, object?>> ReadQuestionsArray(string? extractedTextJson)
    {
        if (string.IsNullOrWhiteSpace(extractedTextJson)) return [];
        try
        {
            var root = JsonSerializer.Deserialize<Dictionary<string, object?>>(extractedTextJson);
            var raw = root?.GetValueOrDefault(QuestionsKey);
            if (raw is null) return [];
            return JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(JsonSerializer.Serialize(raw))
                   ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static ListeningAuthoredQuestion NormalizeFromStorage(Dictionary<string, object?> q)
    {
        string? Read(string key) => q.GetValueOrDefault(key)?.ToString();
        IReadOnlyList<string> ReadList(string key)
        {
            var raw = q.GetValueOrDefault(key);
            if (raw is null) return [];
            try
            {
                var list = JsonSerializer.Deserialize<List<string>>(JsonSerializer.Serialize(raw));
                return list ?? [];
            }
            catch (JsonException) { return []; }
        }
        IReadOnlyList<string?> ReadNullableList(string key)
        {
            var raw = q.GetValueOrDefault(key);
            if (raw is null) return [];
            try
            {
                var list = JsonSerializer.Deserialize<List<string?>>(JsonSerializer.Serialize(raw));
                return list ?? [];
            }
            catch (JsonException) { return []; }
        }
        int? ReadInt(string key)
        {
            var s = Read(key);
            return int.TryParse(s, out var v) ? v : (int?)null;
        }

        var number = int.TryParse(Read("number"), out var n) ? n : 0;
        var points = int.TryParse(Read("points"), out var p) ? Math.Max(1, p) : 1;

        return new ListeningAuthoredQuestion(
            Id: Read("id") ?? $"lq-{number}",
            Number: number,
            PartCode: NormalizePartCode(Read("partCode") ?? Read("part") ?? "A"),
            Type: Read("type") ?? Read("questionType") ?? "short_answer",
            Stem: Read("text") ?? Read("stem") ?? string.Empty,
            Options: ReadList("options"),
            CorrectAnswer: Read("correctAnswer") ?? Read("answer") ?? string.Empty,
            AcceptedAnswers: ReadList("acceptedAnswers"),
            Explanation: Read("explanation"),
            SkillTag: Read("skillTag"),
            TranscriptExcerpt: Read("transcriptExcerpt"),
            DistractorExplanation: Read("distractorExplanation"),
            Points: points,
            OptionDistractorWhy: ReadNullableList("optionDistractorWhy"),
            OptionDistractorCategory: ReadNullableList("optionDistractorCategory"),
            SpeakerAttitude: Read("speakerAttitude"),
            TranscriptEvidenceStartMs: ReadInt("transcriptEvidenceStartMs"),
            TranscriptEvidenceEndMs: ReadInt("transcriptEvidenceEndMs"));
    }

    private static ListeningAuthoredQuestion NormalizeForStorage(ListeningAuthoredQuestion q)
    {
        var partCode = NormalizePartCode(q.PartCode);
        var type = string.IsNullOrWhiteSpace(q.Type)
            ? (partCode.StartsWith('A') ? "short_answer" : "multiple_choice_3")
            : q.Type.Trim();

        // Part B/C are MCQ-3; force exactly 3 options if any were provided.
        var options = (q.Options ?? []).Select(o => o ?? string.Empty).ToList();
        if (type == "multiple_choice_3" && options.Count > 3) options = options.Take(3).ToList();

        var accepted = (q.AcceptedAnswers ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return q with
        {
            Id = string.IsNullOrWhiteSpace(q.Id) ? $"lq-{q.Number}" : q.Id.Trim(),
            PartCode = partCode,
            Type = type,
            Stem = (q.Stem ?? string.Empty).Trim(),
            Options = options,
            CorrectAnswer = (q.CorrectAnswer ?? string.Empty).Trim(),
            AcceptedAnswers = accepted,
            Explanation = string.IsNullOrWhiteSpace(q.Explanation) ? null : q.Explanation.Trim(),
            SkillTag = string.IsNullOrWhiteSpace(q.SkillTag) ? null : q.SkillTag.Trim(),
            TranscriptExcerpt = string.IsNullOrWhiteSpace(q.TranscriptExcerpt) ? null : q.TranscriptExcerpt.Trim(),
            DistractorExplanation = string.IsNullOrWhiteSpace(q.DistractorExplanation) ? null : q.DistractorExplanation.Trim(),
            Points = Math.Max(1, q.Points),
            OptionDistractorWhy = NormalizeNullableStringList(q.OptionDistractorWhy, options.Count),
            OptionDistractorCategory = NormalizeDistractorCategoryList(q.OptionDistractorCategory, options.Count),
            SpeakerAttitude = NormalizeSpeakerAttitude(q.SpeakerAttitude),
            TranscriptEvidenceStartMs = q.TranscriptEvidenceStartMs is int s && s >= 0 ? s : null,
            TranscriptEvidenceEndMs = q.TranscriptEvidenceEndMs is int e && e >= 0 ? e : null,
        };
    }

    private static readonly HashSet<string> AllowedDistractorCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "too_strong", "too_weak", "wrong_speaker", "opposite_meaning", "reused_keyword", "out_of_scope",
    };

    private static readonly HashSet<string> AllowedSpeakerAttitudes = new(StringComparer.OrdinalIgnoreCase)
    {
        "concerned", "optimistic", "doubtful", "critical", "neutral", "other",
    };

    private static IReadOnlyList<string?> NormalizeNullableStringList(IReadOnlyList<string?>? list, int targetLength)
    {
        if (list is null || list.Count == 0) return [];
        var result = new List<string?>(targetLength);
        for (var i = 0; i < targetLength; i++)
        {
            var v = i < list.Count ? list[i] : null;
            result.Add(string.IsNullOrWhiteSpace(v) ? null : v!.Trim());
        }
        return result;
    }

    private static IReadOnlyList<string?> NormalizeDistractorCategoryList(IReadOnlyList<string?>? list, int targetLength)
    {
        if (list is null || list.Count == 0) return [];
        var result = new List<string?>(targetLength);
        for (var i = 0; i < targetLength; i++)
        {
            var v = i < list.Count ? list[i] : null;
            if (string.IsNullOrWhiteSpace(v)) { result.Add(null); continue; }
            var normalized = v!.Trim().ToLowerInvariant();
            result.Add(AllowedDistractorCategories.Contains(normalized) ? normalized : null);
        }
        return result;
    }

    private static string? NormalizeSpeakerAttitude(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var normalized = raw.Trim().ToLowerInvariant();
        return AllowedSpeakerAttitudes.Contains(normalized) ? normalized : null;
    }

    private static string NormalizePartCode(string raw)
    {
        var v = (raw ?? string.Empty).Trim().ToUpperInvariant();
        return v switch
        {
            "A" or "A1" or "A2"
                or "B" or "B1" or "B2" or "B3" or "B4" or "B5" or "B6"
                or "C" or "C1" or "C2" => v,
            _ => "A",
        };
    }

    private static ListeningValidationCounts Tally(IReadOnlyList<ListeningAuthoredQuestion> items)
    {
        var a = items.Count(q => q.PartCode.StartsWith('A'));
        var b = items.Count(q => q.PartCode.StartsWith('B'));
        var c = items.Count(q => q.PartCode.StartsWith('C'));
        return new ListeningValidationCounts(a, b, c, a + b + c);
    }

    // ── Gap B6: per-question + per-extract PATCH ─────────────────────────

    private static readonly JsonSerializerOptions CamelJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<ListeningAuthoredQuestionList> PatchQuestionAsync(
        string paperId,
        string questionId,
        ListeningQuestionPatch patch,
        string adminId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(patch);
        await EnsureRelationalResyncCanRunBeforeMutationAsync(paperId, ct);
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        try
        {
        var paper = await db.ContentPapers.FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw ApiException.NotFound("listening_paper_not_found", "Paper not found.");

        // Load the full authored list so we can locate by id, mutate one
        // entry, and re-serialise the whole array (preserves stable ordering
        // semantics elsewhere in the codebase).
        var raw = ReadQuestionsArray(paper.ExtractedTextJson);
        var items = raw.Select(NormalizeFromStorage).ToList();
        var index = items.FindIndex(q =>
            string.Equals(q.Id, questionId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            throw ApiException.NotFound(
                "listening_question_not_found",
                $"Listening question '{questionId}' not found on paper '{paperId}'.");
        }

        var existing = items[index];
        var beforeJson = JsonSerializer.Serialize(existing, CamelJson);
        var merged = ApplyQuestionPatch(existing, patch);
        var normalized = NormalizeForStorage(merged);
        items[index] = normalized;
        var afterJson = JsonSerializer.Serialize(normalized, CamelJson);

        // Persist the rewritten array back into the JSON blob, preserving any
        // sibling keys the AI extractor / importer may have added.
        var root = ReadRootObject(paper.ExtractedTextJson);
        root[QuestionsKey] = JsonSerializer.SerializeToElement(
            items.OrderBy(q => q.Number).ToList(), CamelJson);
        paper.ExtractedTextJson = JsonSerializer.Serialize(root);
        paper.UpdatedAt = DateTimeOffset.UtcNow;
        paper.RowVersion++;

        // Mirror into the relational ListeningQuestion row when one exists,
        // so authored learner attempts that already read from the relational
        // path see the patched content immediately. Best-effort — relational
        // backfill state is not assumed.
        var relational = await db.ListeningQuestions
            .FirstOrDefaultAsync(q => q.PaperId == paperId
                                      && q.QuestionNumber == normalized.Number, ct);
        if (relational is not null)
        {
            ApplyPatchToRelational(relational, normalized);
            relational.UpdatedAt = paper.UpdatedAt;
        }

        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit_{Guid.NewGuid():N}",
            OccurredAt = paper.UpdatedAt,
            ActorId = adminId,
            ActorAuthAccountId = await db.ResolveActorAuthAccountIdAsync(adminId, ct),
            ActorName = adminId,
            Action = "listening.question.patch",
            ResourceType = "ListeningQuestion",
            ResourceId = normalized.Id,
            Details = TruncateForAudit(JsonSerializer.Serialize(new
            {
                paperId,
                questionId = normalized.Id,
                questionNumber = normalized.Number,
                beforeJson,
                afterJson,
            })),
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ApiException.Conflict("content_paper_concurrent_update",
                "This paper was modified by another admin. Reload and retry.");
        }

        // Gap W4: refresh relational mirror so per-question patches surface
        // immediately in publish-gate counts and learner-facing relational reads.
        await ResyncRelationalIfNeededAsync(paperId, adminId, ct);

        if (tx is not null) await tx.CommitAsync(ct);

        return new ListeningAuthoredQuestionList(items, Tally(items));
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<ListeningAuthoredExtract>> PatchExtractAsync(
        string paperId,
        string extractCode,
        ListeningExtractPatch patch,
        string adminId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(patch);
        await EnsureRelationalResyncCanRunBeforeMutationAsync(paperId, ct);
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        try
        {
        var paper = await db.ContentPapers.FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw ApiException.NotFound("listening_paper_not_found", "Paper not found.");

        var partCode = NormalizeExtractPartCode(extractCode)
            ?? throw ApiException.NotFound(
                "listening_extract_not_found",
                $"Listening extract code '{extractCode}' is not a valid part code (A1, A2, B, C1, C2).");

        var current = ReadExtractsArray(paper.ExtractedTextJson)
            .Select((e, i) => NormalizeExtractFromStorage(e, i))
            .ToList();
        var index = current.FindIndex(e =>
            string.Equals(e.PartCode, partCode, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            throw ApiException.NotFound(
                "listening_extract_not_found",
                $"Listening extract '{partCode}' not found on paper '{paperId}'.");
        }

        var existing = current[index];
        var beforeJson = JsonSerializer.Serialize(existing, CamelJson);
        var merged = ApplyExtractPatch(existing, patch);
        var normalized = NormalizeExtractForStorage(merged);
        current[index] = normalized;
        var afterJson = JsonSerializer.Serialize(normalized, CamelJson);

        var root = ReadRootObject(paper.ExtractedTextJson);
        var serialised = current
            .OrderBy(e => PartCodeOrder(e.PartCode))
            .ThenBy(e => e.DisplayOrder)
            .ToList();
        root[ExtractsKey] = JsonSerializer.SerializeToElement(serialised, CamelJson);
        paper.ExtractedTextJson = JsonSerializer.Serialize(root);
        paper.UpdatedAt = DateTimeOffset.UtcNow;
        paper.RowVersion++;

        db.AuditEvents.Add(new AuditEvent
        {
            Id = $"audit_{Guid.NewGuid():N}",
            OccurredAt = paper.UpdatedAt,
            ActorId = adminId,
            ActorAuthAccountId = await db.ResolveActorAuthAccountIdAsync(adminId, ct),
            ActorName = adminId,
            Action = "listening.extract.patch",
            ResourceType = "ListeningExtract",
            ResourceId = $"{paperId}:{partCode}",
            Details = TruncateForAudit(JsonSerializer.Serialize(new
            {
                paperId,
                partCode,
                beforeJson,
                afterJson,
            })),
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ApiException.Conflict("content_paper_concurrent_update",
                "This paper was modified by another admin. Reload and retry.");
        }

        // Gap W4: same resync after extract metadata patches so accent /
        // speakers / audio window changes propagate to the relational mirror.
        await ResyncRelationalIfNeededAsync(paperId, adminId, ct);

        if (tx is not null) await tx.CommitAsync(ct);

        return serialised;
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static Dictionary<string, JsonElement> ReadRootObject(string? extractedTextJson)
    {
        if (string.IsNullOrWhiteSpace(extractedTextJson))
            return new Dictionary<string, JsonElement>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(extractedTextJson)
                   ?? new Dictionary<string, JsonElement>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, JsonElement>();
        }
    }

    private static ListeningAuthoredQuestion ApplyQuestionPatch(
        ListeningAuthoredQuestion existing, ListeningQuestionPatch p)
        => existing with
        {
            PartCode = p.PartCode ?? existing.PartCode,
            Type = p.Type ?? existing.Type,
            Stem = p.Stem ?? existing.Stem,
            Options = p.Options ?? existing.Options,
            CorrectAnswer = p.CorrectAnswer ?? existing.CorrectAnswer,
            AcceptedAnswers = p.AcceptedAnswers ?? existing.AcceptedAnswers,
            Explanation = p.Explanation ?? existing.Explanation,
            SkillTag = p.SkillTag ?? existing.SkillTag,
            TranscriptExcerpt = p.TranscriptExcerpt ?? existing.TranscriptExcerpt,
            DistractorExplanation = p.DistractorExplanation ?? existing.DistractorExplanation,
            Points = p.Points ?? existing.Points,
            OptionDistractorWhy = p.OptionDistractorWhy ?? existing.OptionDistractorWhy,
            OptionDistractorCategory = p.OptionDistractorCategory ?? existing.OptionDistractorCategory,
            SpeakerAttitude = p.SpeakerAttitude ?? existing.SpeakerAttitude,
            TranscriptEvidenceStartMs = p.TranscriptEvidenceStartMs ?? existing.TranscriptEvidenceStartMs,
            TranscriptEvidenceEndMs = p.TranscriptEvidenceEndMs ?? existing.TranscriptEvidenceEndMs,
        };

    private static ListeningAuthoredExtract ApplyExtractPatch(
        ListeningAuthoredExtract existing, ListeningExtractPatch p)
        => existing with
        {
            DisplayOrder = p.DisplayOrder ?? existing.DisplayOrder,
            Kind = p.Kind ?? existing.Kind,
            Title = p.Title ?? existing.Title,
            AccentCode = p.AccentCode ?? existing.AccentCode,
            Speakers = p.Speakers ?? existing.Speakers,
            AudioStartMs = p.AudioStartMs ?? existing.AudioStartMs,
            AudioEndMs = p.AudioEndMs ?? existing.AudioEndMs,
            TimeLimitSeconds = p.TimeLimitSeconds ?? existing.TimeLimitSeconds,
            NotesBodyMarkdown = p.NotesBodyMarkdown ?? existing.NotesBodyMarkdown,
            AuthoringMethod = p.AuthoringMethod ?? existing.AuthoringMethod,
            PartAOverlayBlanksJson = p.PartAOverlayBlanksJson ?? existing.PartAOverlayBlanksJson,
            ContextIntro = p.ContextIntro ?? existing.ContextIntro,
        };

    private static void ApplyPatchToRelational(
        ListeningQuestion row, ListeningAuthoredQuestion patched)
    {
        row.Stem = patched.Stem;
        row.Points = Math.Max(1, patched.Points);
        row.SkillTag = patched.SkillTag;
        row.ExplanationMarkdown = patched.Explanation;
        row.TranscriptEvidenceText = patched.TranscriptExcerpt;
        row.TranscriptEvidenceStartMs = patched.TranscriptEvidenceStartMs;
        row.TranscriptEvidenceEndMs = patched.TranscriptEvidenceEndMs;
        row.CorrectAnswerJson = JsonSerializer.Serialize(patched.CorrectAnswer);
        row.AcceptedSynonymsJson = patched.AcceptedAnswers is { Count: > 0 }
            ? JsonSerializer.Serialize(patched.AcceptedAnswers)
            : null;
        if (Enum.TryParse<ListeningSpeakerAttitude>(
                patched.SpeakerAttitude, ignoreCase: true, out var attitude))
        {
            row.SpeakerAttitude = attitude;
        }
        else if (string.IsNullOrWhiteSpace(patched.SpeakerAttitude))
        {
            row.SpeakerAttitude = null;
        }
    }

    /// <summary>
    /// Gap N5: AuditEvent.Details is now mapped to PostgreSQL TEXT (see
    /// <c>LearnerDbContext.OnModelCreating</c>) so before/after JSON
    /// snapshot pairs survive in full. We keep an in-code 64 KB cap as a
    /// defence-in-depth guard against pathological payloads — well above
    /// the typical Listening Part-C MCQ-3 patch envelope (~4 KB) but small
    /// enough that a runaway loop can't bloat the audit table.
    /// </summary>
    private const int MaxAuditDetailsBytes = 65_536;

    private static string TruncateForAudit(string s)
        => string.IsNullOrEmpty(s) || s.Length <= MaxAuditDetailsBytes
            ? (s ?? string.Empty)
            : s[..MaxAuditDetailsBytes];
}
