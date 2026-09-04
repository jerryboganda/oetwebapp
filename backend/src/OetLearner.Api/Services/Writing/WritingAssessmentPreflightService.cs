using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingAssessmentPreflightResult(
    bool CanScore,
    WritingAssessmentV11Status Status,
    IReadOnlyList<string> MissingInputCodes,
    IReadOnlyList<string> ReleaseBlockCodes,
    IReadOnlyList<string> AppliedRulePacks,
    string Profession,
    string LetterType,
    string RulePackVersion,
    string TaskSnapshot,
    string CaseNotesSnapshot,
    WritingTaskUnderstandingResult? TaskUnderstanding = null);

public interface IWritingAssessmentPreflightService
{
    Task<WritingAssessmentPreflightResult> ValidateAsync(WritingSubmission submission, CancellationToken ct);
}

/// <summary>
/// Fail-closed input and release gate for the v1.1 assessment route. This class
/// deliberately does not infer missing task or case-note content from the
/// candidate letter.
/// </summary>
public sealed class WritingAssessmentPreflightService(
    LearnerDbContext db,
    ILogger<WritingAssessmentPreflightService>? logger = null) : IWritingAssessmentPreflightService
{
    private static readonly HashSet<string> SupportedProfessions = new(StringComparer.Ordinal)
    {
        "medicine", "nursing", "dentistry", "pharmacy", "physiotherapy", "veterinary",
        "optometry", "radiography", "occupational_therapy", "speech_pathology", "podiatry",
        "dietetics", "other_allied_health",
    };

    /// <summary>
    /// Letter types for which the domain genuinely requires an exact,
    /// owner-approved detailed pack (no profession-level fallback). Every
    /// other letter type — including the universal <c>other</c> (Other
    /// Letters) fallback — resolves through <see cref="ResolvePackAsync"/>:
    /// exact pack first, then the profession's generic packs. Optional
    /// specialized packs enhance grading where present; they never block a
    /// valid task whose profession is released.
    /// </summary>
    private static readonly HashSet<string> DetailedPackRequiredLetterTypes = new(StringComparer.Ordinal)
    {
        "transfer",
        "referral_to_gp",
    };

    /// <summary>
    /// Ordered profession-level fallback candidates for a letter type that
    /// does not genuinely require its own detailed pack. <c>other</c> (the
    /// generic Other Letters pack) is preferred over reusing another
    /// concrete letter type's specifics.
    /// </summary>
    private static readonly string[] ProfessionFallbackLetterTypes = ["other", "routine_referral"];

    public async Task<WritingAssessmentPreflightResult> ValidateAsync(WritingSubmission submission, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var scenario = await db.WritingScenarios.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == submission.ScenarioId, ct);
        if (scenario is null)
        {
            return BlockedMissing(
                profession: string.Empty,
                letterType: string.Empty,
                missing: ["scenario"],
                taskSnapshot: string.Empty,
                caseNotesSnapshot: string.Empty);
        }

        var taskSnapshot = scenario.TaskPromptMarkdown?.Trim() ?? string.Empty;
        var facts = await db.WritingScenarioStructuredSentences.AsNoTracking()
            .Where(x => x.ScenarioId == scenario.Id)
            .OrderBy(x => x.Ordinal)
            .ToListAsync(ct);
        var caseNotesSnapshot = string.Join("\n", facts.Select(x => x.SentenceText.Trim()).Where(x => x.Length > 0));
        var profession = Normalize(scenario.Profession);
        // Single vocabulary bridge (WritingLetterTypeTaxonomy.ToPackLetterType):
        // modern LT-* catalogue codes resolve to the same legacy pack/rule
        // tokens as their legacy-token equivalents, so an LT-RR task matches a
        // routine_referral pack instead of release-blocking on "lt_rr".
        var letterType = WritingLetterTypeTaxonomy.ToPackLetterType(scenario.LetterType);
        var missing = new List<string>();
        // NOTE: InternalCode (test_id) is provenance metadata, not a grading
        // input — it is intentionally NOT a scoring gate. Tasks published
        // without it must still grade from canonical case notes + task.
        if (string.IsNullOrWhiteSpace(taskSnapshot)) missing.Add("written_task");
        // NOTE: an empty candidate letter is a valid (unscorable-content)
        // submission, not missing input. It proceeds to grading and receives
        // a deterministic zero assessment — never a pre-submission block.
        if (string.IsNullOrWhiteSpace(profession)) missing.Add("profession");
        if (string.IsNullOrWhiteSpace(letterType)) missing.Add("letter_type");
        if (!string.IsNullOrWhiteSpace(profession) && !SupportedProfessions.Contains(profession))
            missing.Add("profession_unsupported");
        if (string.IsNullOrWhiteSpace(caseNotesSnapshot))
        {
            missing.Add(string.IsNullOrWhiteSpace(scenario.StimulusPdfMediaAssetId)
                ? "case_note_pages"
                : "case_note_pages_unreadable");
        }

        if (missing.Count > 0)
        {
            logger?.LogWarning(
                "Writing preflight blocked submission {SubmissionId} scenario {ScenarioId}: {Missing}",
                submission.Id, submission.ScenarioId, string.Join(",", missing));
            return BlockedMissing(profession, letterType, missing, taskSnapshot, caseNotesSnapshot);
        }

        var understanding = WritingTaskUnderstandingService.Understand(
            taskSnapshot,
            caseNotesSnapshot,
            letterType);
        if (understanding.ConflictingEvidence)
        {
            // The admin-authored catalogue letter type is the explicit
            // classification — heuristic task-text signals never overrule it
            // into a candidate-facing failure. Grade under the configured
            // type and keep the conflict as telemetry for content review.
            logger?.LogWarning(
                "Writing preflight task-classification conflict on submission {SubmissionId} scenario {ScenarioId}: configured letterType {LetterType} kept; evidence {Evidence}",
                submission.Id, submission.ScenarioId, letterType, string.Join(";", understanding.EvidencePhrases));
        }

        // Recipient / diagnosis uncertainty is a content-authoring concern,
        // resolved at publication time (the publish gate blocks unknown
        // recipients). It must never stop a completed candidate submission:
        // grade with the best-effort interpretation instead.
        if (string.Equals(understanding.RecipientCategory, "unknown", StringComparison.Ordinal))
        {
            logger?.LogWarning(
                "Writing preflight recipient unresolved on submission {SubmissionId} scenario {ScenarioId}; grading proceeds with best-effort interpretation.",
                submission.Id, submission.ScenarioId);
        }

        if (string.IsNullOrWhiteSpace(understanding.DiagnosisOrPlanEvidence))
        {
            logger?.LogWarning(
                "Writing preflight diagnosis/request evidence thin on submission {SubmissionId} scenario {ScenarioId}; grading proceeds with best-effort interpretation.",
                submission.Id, submission.ScenarioId);
        }

        var pack = await ResolvePackAsync(db, profession, letterType, ct);

        var releaseBlocks = new List<string>();
        if (pack is null)
        {
            releaseBlocks.Add(
                DetailedPackRequiredLetterTypes.Contains(letterType)
                    ? "letter_type_pack_not_approved"
                    : "profession_pack_not_approved");
        }

        if (releaseBlocks.Count > 0)
        {
            logger?.LogWarning(
                "Writing preflight release-blocked submission {SubmissionId} scenario {ScenarioId} profession {Profession} letterType {LetterType}: {Blocks}",
                submission.Id, submission.ScenarioId, profession, letterType, string.Join(",", releaseBlocks));
            return new WritingAssessmentPreflightResult(
                false,
                WritingAssessmentV11Status.BlockedReleaseGate,
                [],
                releaseBlocks,
                [],
                profession,
                letterType,
                pack?.VersionKey ?? string.Empty,
                taskSnapshot,
                caseNotesSnapshot,
                understanding);
        }

        return new WritingAssessmentPreflightResult(
            true,
            WritingAssessmentV11Status.AwaitingPreflight,
            [],
            [],
            [$"{pack!.Profession}:{pack.LetterType}:{pack.VersionKey}"],
            profession,
            letterType,
            pack.VersionKey,
            taskSnapshot,
            caseNotesSnapshot,
            understanding);
    }

    /// <summary>
    /// Deterministic runtime pack resolution shared by candidate grading
    /// (this service) and the publication preflight
    /// (<see cref="WritingTaskAuthoringService"/>) so validator and runtime
    /// can never disagree on what "released" means.
    ///
    /// Resolution order for an (already normalized) profession + pack letter
    /// type: exact approved candidate-facing pack, then — unless the letter
    /// type genuinely requires its own detailed pack — the profession's
    /// generic packs (<c>other</c>, then <c>routine_referral</c>), then the
    /// most recently approved candidate-facing pack for the profession.
    /// Returns null only when the profession has no released pack at all: a
    /// genuine configuration gap that must block publication, never silently
    /// fall back to another profession's rules (that would mis-grade).
    ///
    /// Reads the current approved rows on every call (no process-memory
    /// cache), so a rule-pack release/retirement takes effect immediately
    /// without stale-approval drift.
    /// </summary>
    public static async Task<WritingAssessmentPackVersion?> ResolvePackAsync(
        LearnerDbContext db,
        string profession,
        string packLetterType,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (string.IsNullOrWhiteSpace(profession) || string.IsNullOrWhiteSpace(packLetterType))
            return null;

        var exact = await db.WritingAssessmentPackVersions.AsNoTracking()
            .Where(x => x.Profession == profession
                && x.LetterType == packLetterType
                && x.Status == WritingAssessmentReleaseStatus.Approved
                && x.CandidateFacing)
            .OrderByDescending(x => x.ApprovedAt)
            .ThenByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (exact is not null) return exact;

        // Letter types with a genuine detailed-pack requirement never fall
        // back: grading them under generic rules would be incorrect.
        if (DetailedPackRequiredLetterTypes.Contains(packLetterType))
            return null;

        foreach (var fallback in ProfessionFallbackLetterTypes)
        {
            if (string.Equals(fallback, packLetterType, StringComparison.Ordinal)) continue;
            var generic = await db.WritingAssessmentPackVersions.AsNoTracking()
                .Where(x => x.Profession == profession
                    && x.LetterType == fallback
                    && x.Status == WritingAssessmentReleaseStatus.Approved
                    && x.CandidateFacing)
                .OrderByDescending(x => x.ApprovedAt)
                .ThenByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (generic is not null) return generic;
        }

        return await db.WritingAssessmentPackVersions.AsNoTracking()
            .Where(x => x.Profession == profession
                && x.Status == WritingAssessmentReleaseStatus.Approved
                && x.CandidateFacing)
            .OrderByDescending(x => x.ApprovedAt)
            .ThenByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Publication-time twin of <see cref="ResolvePackAsync"/> for callers
    /// that already hold normalized catalogue values: maps the catalogue
    /// letter type through the single vocabulary bridge first.
    /// </summary>
    public static Task<WritingAssessmentPackVersion?> ResolvePackForCatalogueAsync(
        LearnerDbContext db,
        string? profession,
        string? catalogueLetterType,
        CancellationToken ct)
        => ResolvePackAsync(db, Normalize(profession), WritingLetterTypeTaxonomy.ToPackLetterType(catalogueLetterType), ct);

    private static WritingAssessmentPreflightResult BlockedMissing(
        string profession,
        string letterType,
        IReadOnlyList<string> missing,
        string taskSnapshot,
        string caseNotesSnapshot)
        => new(
            false,
            WritingAssessmentV11Status.BlockedMissingInput,
            missing,
            [],
            [],
            profession,
            letterType,
            string.Empty,
            taskSnapshot,
            caseNotesSnapshot,
            null);

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
}
