using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing;

/// <summary>
/// Full-catalogue reconciliation for the Writing Submit-for-Grading release
/// gate (spec §22–§23, §15, §32). Enumerates EVERY published
/// candidate-facing <see cref="WritingScenario"/> from the authoritative
/// application model — never exported files — and runs the same resolvers
/// candidate grading depends on (pack resolution, recipient/task
/// interpretation, canonical inputs, Model Answer state).
///
/// Read-only except for <see cref="QuarantineInvalidPublishedAsync"/>, which
/// archives (never deletes) published tasks that cannot safely grade, so no
/// learner can discover them at submit time. Ambiguous business/content
/// decisions are reported for human confirmation, never auto-guessed.
/// </summary>
public sealed record WritingCatalogueScenarioRow(
    Guid ScenarioId,
    string TaskTitle,
    string PublicationStatus,
    bool CandidateVisible,
    string Profession,
    string ProfessionResolutionStatus,
    string LetterType,
    bool OtherLetters,
    Guid? RulePackId,
    string RulePackVersion,
    string RulePackApprovalStatus,
    string SharedRulesVersion,
    bool CanonicalCaseNotesReady,
    int CaseNoteSentenceCount,
    bool ExactWritingTaskReady,
    bool RecipientMetadataReady,
    string RecipientCategory,
    bool SavedModelAnswerReady,
    string? ModelAnswerStatus,
    bool PublishReady,
    IReadOnlyList<string> BlockingCodes,
    string RecommendedAction);

public sealed record WritingCatalogueCompatibilityReport(
    DateTimeOffset GeneratedAt,
    int PublishedScenarios,
    int PublishReady,
    int Invalid,
    IReadOnlyList<WritingCatalogueScenarioRow> Rows);

public sealed record WritingCatalogueQuarantineResult(
    int Scanned,
    int Quarantined,
    int Skipped,
    IReadOnlyList<WritingCatalogueQuarantinedItem> Items);

public sealed record WritingCatalogueQuarantinedItem(
    Guid ScenarioId,
    string Title,
    IReadOnlyList<string> Reasons,
    string Outcome);

public interface IWritingCataloguePreflightService
{
    Task<WritingCatalogueCompatibilityReport> ScanPublishedCatalogueAsync(CancellationToken ct = default);
    Task<WritingCatalogueQuarantineResult> QuarantineInvalidPublishedAsync(bool dryRun, CancellationToken ct = default);
}

public sealed class WritingCataloguePreflightService(
    LearnerDbContext db,
    TimeProvider clock,
    ILogger<WritingCataloguePreflightService>? logger = null) : IWritingCataloguePreflightService
{
    /// <summary>
    /// Version of the shared/global Writing rules applied to every grading
    /// alongside the profession pack. Source of truth:
    /// <c>rulebooks/writing/common/assessment-criteria.json</c> ("version").
    /// </summary>
    public const string SharedRulesVersion = "writing-common/assessment-criteria:1.0.0";

    public async Task<WritingCatalogueCompatibilityReport> ScanPublishedCatalogueAsync(CancellationToken ct = default)
    {
        var scenarios = await db.WritingScenarios.AsNoTracking()
            .Where(s => s.Status == "published")
            .OrderBy(s => s.Title)
            .ToListAsync(ct);
        var rows = await BuildRowsAsync(scenarios, ct);
        var invalid = rows.Count(r => !r.PublishReady);
        return new WritingCatalogueCompatibilityReport(
            clock.GetUtcNow(),
            scenarios.Count,
            rows.Count(r => r.PublishReady),
            invalid,
            rows);
    }

    public async Task<WritingCatalogueQuarantineResult> QuarantineInvalidPublishedAsync(bool dryRun, CancellationToken ct = default)
    {
        var report = await ScanPublishedCatalogueAsync(ct);
        var items = new List<WritingCatalogueQuarantinedItem>();
        var quarantined = 0;

        foreach (var row in report.Rows.Where(r => !r.PublishReady))
        {
            if (dryRun)
            {
                items.Add(new WritingCatalogueQuarantinedItem(
                    row.ScenarioId, row.TaskTitle, row.BlockingCodes, "would_archive"));
                continue;
            }

            var entity = await db.WritingScenarios.FirstOrDefaultAsync(s => s.Id == row.ScenarioId, ct);
            if (entity is null || entity.Status != "published")
            {
                items.Add(new WritingCatalogueQuarantinedItem(
                    row.ScenarioId, row.TaskTitle, row.BlockingCodes, "skipped_not_published"));
                continue;
            }

            // Archive (unpublish), never delete: historical attempts, grades,
            // and audit history are preserved; only candidate visibility is
            // removed until an admin repairs and re-publishes.
            entity.Status = "archived";
            entity.UpdatedAt = clock.GetUtcNow();
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                ActorId = "system",
                ActorName = "system",
                Action = "writing.catalogue.quarantined",
                ResourceType = "WritingScenario",
                ResourceId = entity.Id.ToString("D"),
                Details = $"Auto-archived by catalogue preflight: {string.Join(",", row.BlockingCodes)}",
                OccurredAt = clock.GetUtcNow(),
            });
            quarantined++;
            items.Add(new WritingCatalogueQuarantinedItem(
                row.ScenarioId, row.TaskTitle, row.BlockingCodes, "archived"));
            logger?.LogWarning(
                "Writing catalogue quarantine archived published scenario {ScenarioId} ({Title}): {Reasons}",
                row.ScenarioId, row.TaskTitle, string.Join(",", row.BlockingCodes));
        }

        if (!dryRun && quarantined > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return new WritingCatalogueQuarantineResult(
            report.PublishedScenarios,
            quarantined,
            report.Invalid - quarantined,
            items);
    }

    private async Task<IReadOnlyList<WritingCatalogueScenarioRow>> BuildRowsAsync(
        IReadOnlyList<WritingScenario> scenarios,
        CancellationToken ct)
    {
        var ids = scenarios.Select(s => s.Id).ToList();
        var sentenceRows = await db.WritingScenarioStructuredSentences.AsNoTracking()
            .Where(x => ids.Contains(x.ScenarioId))
            .OrderBy(x => x.Ordinal)
            .ToListAsync(ct);
        var sentenceGroups = sentenceRows
            .GroupBy(x => x.ScenarioId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Ordinal).ToList());
        var answers = await db.WritingTaskModelAnswers.AsNoTracking()
            .Where(a => ids.Contains(a.ScenarioId))
            .ToDictionaryAsync(a => a.ScenarioId, ct);

        // One batched released-pack query (no N+1 across the catalogue).
        var normalizedProfessions = scenarios
            .Select(s => NormalizeProfession(s.Profession))
            .Where(p => p.Length > 0)
            .Distinct()
            .ToList();
        var releasedPacks = await db.WritingAssessmentPackVersions.AsNoTracking()
            .Where(x => normalizedProfessions.Contains(x.Profession)
                && x.Status == WritingAssessmentReleaseStatus.Approved
                && x.CandidateFacing)
            .OrderByDescending(x => x.ApprovedAt)
            .ThenByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
        var packsByProfession = releasedPacks
            .GroupBy(x => x.Profession)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<WritingCatalogueScenarioRow>(scenarios.Count);
        foreach (var s in scenarios)
        {
            sentenceGroups.TryGetValue(s.Id, out var sentences);
            sentences ??= new List<WritingScenarioStructuredSentence>();
            var caseNotesText = string.Join("\n", sentences
                .Select(x => (x.SentenceText ?? string.Empty).Trim())
                .Where(x => x.Length > 0));

            var profession = NormalizeProfession(s.Profession);
            var professionSupported = RulebookProfessionParser.TryParse(s.Profession, out _);
            var packLetterType = WritingLetterTypeTaxonomy.ToPackLetterType(s.LetterType);
            var pack = ResolveFromBatch(packsByProfession, profession, packLetterType);

            var understanding = WritingTaskUnderstandingService.Understand(
                s.TaskPromptMarkdown ?? string.Empty,
                caseNotesText,
                packLetterType);
            var recipientReady = !string.Equals(understanding.RecipientCategory, "unknown", StringComparison.Ordinal);

            answers.TryGetValue(s.Id, out var answer);
            // Addendum Rev8 §14/§19.6: "saved Model Answer ready" means verified
            // with zero violations under the CURRENT validator, not merely an
            // old Ready + visible flag (that stale flag is how "224 clean" was
            // reported while live letters still broke known rules).
            var modelAnswerReady = WritingTaskModelAnswerService.IsVerifiedForCandidates(answer);

            var blocking = new List<string>();
            if (string.IsNullOrWhiteSpace(s.Title)) blocking.Add("title_required");
            if (string.IsNullOrWhiteSpace(s.Profession)) blocking.Add("profession_required");
            else if (!professionSupported) blocking.Add("profession_unsupported");
            if (string.IsNullOrWhiteSpace(s.LetterType)) blocking.Add("letter_type_required");
            else if (!WritingLetterTypeTaxonomy.IsValidCatalogueLetterType(s.LetterType))
                blocking.Add("letter_type_unsupported");
            if (string.IsNullOrWhiteSpace(s.TaskPromptMarkdown)) blocking.Add("written_task_required");
            if (sentences.Count == 0)
                blocking.Add(string.IsNullOrWhiteSpace(s.StimulusPdfMediaAssetId)
                    ? "case_note_pages"
                    : "case_note_pages_unreadable");
            if (professionSupported && pack is null)
                blocking.Add(packLetterType is "transfer" or "referral_to_gp"
                    ? "letter_type_pack_not_approved"
                    : "profession_pack_not_approved");
            if (!string.IsNullOrWhiteSpace(s.TaskPromptMarkdown) && sentences.Count > 0)
            {
                if (!recipientReady) blocking.Add("recipient_unresolved");
                if (string.IsNullOrWhiteSpace(understanding.DiagnosisOrPlanEvidence))
                    blocking.Add("diagnosis_or_request_unresolved");
                if (understanding.ConflictingEvidence) blocking.Add("task_classification_conflict");
            }

            if (!modelAnswerReady) blocking.Add("model_answer_not_approved");

            rows.Add(new WritingCatalogueScenarioRow(
                s.Id,
                s.Title,
                s.Status,
                CandidateVisible: s.Status == "published",
                s.Profession,
                professionSupported ? "supported" : "unsupported_or_unresolved",
                s.LetterType,
                string.Equals(packLetterType, "other", StringComparison.Ordinal),
                pack?.Id,
                pack?.VersionKey ?? string.Empty,
                pack is null ? "unapproved_or_missing" : pack.Status.ToString(),
                SharedRulesVersion,
                sentences.Count > 0,
                sentences.Count,
                !string.IsNullOrWhiteSpace(s.TaskPromptMarkdown),
                recipientReady,
                understanding.RecipientCategory,
                modelAnswerReady,
                answer?.Status.ToString(),
                blocking.Count == 0,
                blocking,
                blocking.Count == 0
                    ? "ok"
                    : blocking.All(c => c is "model_answer_not_approved" or "case_note_pages" or "case_note_pages_unreadable")
                        ? "repair_deterministic_backfill"
                        : "block_unpublish_pending_admin"));
        }

        return rows;
    }

    private static WritingAssessmentPackVersion? ResolveFromBatch(
        Dictionary<string, List<WritingAssessmentPackVersion>> packsByProfession,
        string normalizedProfession,
        string packLetterType)
    {
        if (string.IsNullOrWhiteSpace(normalizedProfession)
            || !packsByProfession.TryGetValue(normalizedProfession, out var packs)
            || packs.Count == 0)
            return null;
        var exact = packs.FirstOrDefault(x => string.Equals(x.LetterType, packLetterType, StringComparison.Ordinal));
        if (exact is not null) return exact;
        if (packLetterType is "transfer" or "referral_to_gp") return null;
        return packs.FirstOrDefault(x => string.Equals(x.LetterType, "other", StringComparison.Ordinal))
            ?? packs.FirstOrDefault(x => string.Equals(x.LetterType, "routine_referral", StringComparison.Ordinal))
            ?? packs.FirstOrDefault();
    }

    private static string NormalizeProfession(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
}
