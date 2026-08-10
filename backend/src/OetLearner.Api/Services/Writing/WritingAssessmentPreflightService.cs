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
public sealed class WritingAssessmentPreflightService(LearnerDbContext db) : IWritingAssessmentPreflightService
{
    private static readonly HashSet<string> SupportedProfessions = new(StringComparer.Ordinal)
    {
        "medicine", "nursing", "dentistry", "pharmacy", "physiotherapy", "veterinary",
        "optometry", "radiography", "occupational_therapy", "speech_pathology", "podiatry",
        "dietetics", "other_allied_health",
    };

    private static readonly HashSet<string> DetailedPackRequiredLetterTypes = new(StringComparer.Ordinal)
    {
        "transfer",
        "referral_to_gp",
        "gp_referral",
    };

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
        var letterType = NormalizeLetterType(scenario.LetterType);
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(scenario.InternalCode)) missing.Add("test_id");
        if (string.IsNullOrWhiteSpace(taskSnapshot)) missing.Add("written_task");
        if (string.IsNullOrWhiteSpace(submission.LetterContent)) missing.Add("candidate_letter");
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
            return BlockedMissing(profession, letterType, missing, taskSnapshot, caseNotesSnapshot);
        }

        var understanding = WritingTaskUnderstandingService.Understand(
            taskSnapshot,
            caseNotesSnapshot,
            letterType);
        if (understanding.ConflictingEvidence)
        {
            return new WritingAssessmentPreflightResult(
                false,
                WritingAssessmentV11Status.RequiresReview,
                [],
                ["task_classification_conflict"],
                [],
                profession,
                letterType,
                string.Empty,
                taskSnapshot,
                caseNotesSnapshot,
                understanding);
        }

        if (string.Equals(understanding.RecipientCategory, "unknown", StringComparison.Ordinal))
        {
            missing.Add("recipient");
        }

        if (string.IsNullOrWhiteSpace(understanding.DiagnosisOrPlanEvidence))
        {
            missing.Add("diagnosis_or_request");
        }

        if (missing.Count > 0)
        {
            return BlockedMissing(profession, letterType, missing, taskSnapshot, caseNotesSnapshot);
        }

        var pack = await db.WritingAssessmentPackVersions.AsNoTracking()
            .Where(x => x.Profession == profession
                && x.LetterType == letterType
                && x.Status == WritingAssessmentReleaseStatus.Approved
                && x.CandidateFacing)
            .OrderByDescending(x => x.ApprovedAt)
            .ThenByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

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

    private static string NormalizeLetterType(string? value)
    {
        var normalized = Normalize(value);
        return normalized switch
        {
            "routine" => "routine_referral",
            "urgent" => "urgent_referral",
            "non_medical" => "non_medical_referral",
            "referral_gp" => "referral_to_gp",
            "gp" => "referral_to_gp",
            _ => normalized,
        };
    }
}
