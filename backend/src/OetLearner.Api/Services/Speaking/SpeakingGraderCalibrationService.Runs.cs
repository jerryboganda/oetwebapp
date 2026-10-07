using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// The harness (owner spec 4 Oct 2026): grade every expert-marked performance several times with the grader under test and
/// compare the grades with the expert's marks (<see cref="SpeakingGraderCalibrationMetrics"/>). Each grade is one durable
/// operation run by the existing worker, so no HTTP request waits minutes for a grade and a dropped workflow loses nothing.
/// It never competes with learners: <see cref="NextAsync"/> queues work only while no learner grade is queued or running.
/// </summary>
public sealed partial class SpeakingGraderCalibrationService
{
    /// <summary>Resource type of the durable operation that runs one calibration grade.</summary>
    public const string GradeResourceType = "speaking_calibration_grade";

    private const int MaxAttemptsPerGrade = 3;
    private const int MaxRepeats = 5;
    private static readonly TimeSpan QueuedGradeTimeout = TimeSpan.FromMinutes(60);
    private static readonly JsonSerializerOptions ReportJson = new(JsonSerializerDefaults.Web);

    public async Task<SpeakingGraderCalibrationRunView> CreateRunAsync(
        string adminId, string adminName, SpeakingGraderCalibrationRunCreateRequest request, CancellationToken ct)
    {
        // The approved validation run must cover every marked performance (its coverage and thresholds are measured over the set),
        // so only an informational pilot may be limited to chosen performances.
        if (request.SampleIds is { Count: > 0 } && request.Pilot != true)
        {
            throw ApiException.Validation("speaking_calibration_sample_filter_requires_pilot",
                "Only a pilot run can be limited to chosen performances; a validation run grades every marked one.");
        }

        if (await db.SpeakingGraderCalibrationRuns.AnyAsync(r => r.Status == SpeakingGraderCalibrationRunStatus.Running, ct))
        {
            throw ApiException.Conflict("speaking_calibration_run_active",
                "A calibration run is already in progress. Finish it (or cancel it) before starting another.");
        }

        // Scope decides which marked set the run grades: single cards with the card grader, or whole Full Mocks
        // with the combined grader (speaking.score.v3-combined). A pilot never needs the approved coverage; it
        // grades whatever is marked so the owner can see the comparison on a small real sample.
        var scope = string.Equals(request.Scope?.Trim(), ScopeMock, StringComparison.OrdinalIgnoreCase)
            ? ScopeMock
            : ScopeCard;

        List<string> sampleIds;
        if (scope == ScopeMock)
        {
            var mocks = await db.SpeakingGraderCalibrationMockSamples.AsNoTracking()
                .Where(s => s.Status == SpeakingGraderCalibrationSampleStatus.Labelled
                    && s.ExpertScoresJson != null
                    && s.ExpertOverallScaled != null)
                .OrderBy(s => s.Id)
                .ToListAsync(ct);
            // A mock whose audio expired, whose learner withdrew consent or whose transcript was erased is not graded.
            var unusableMocks = await UnusableMockSampleIdsAsync(mocks, ct);
            sampleIds = mocks.Where(s => !unusableMocks.Contains(s.Id)).Select(s => s.Id).ToList();
        }
        else
        {
            var labelled = await db.SpeakingGraderCalibrationSamples.AsNoTracking()
                .Where(s => s.Status == SpeakingGraderCalibrationSampleStatus.Labelled
                    && s.ExpertScoresJson != null
                    && s.ExpertOverallScaled != null)
                .OrderBy(s => s.Id)
                .ToListAsync(ct);
            // A performance whose audio expired, whose learner withdrew consent or whose transcript was erased is not graded.
            var unusable = await UnusableSampleIdsAsync(labelled, ct);
            sampleIds = labelled.Where(s => !unusable.Contains(s.Id)).Select(s => s.Id).ToList();
        }

        // A run may be limited to named performances (an owner pilot of ONE Full Mock grades only that one, not every marked set).
        if (request.SampleIds is { Count: > 0 } only)
        {
            var wanted = only.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).ToHashSet(StringComparer.Ordinal);
            sampleIds = sampleIds.Where(wanted.Contains).ToList();
        }

        if (sampleIds.Count == 0)
        {
            throw ApiException.Conflict("speaking_calibration_nothing_to_grade",
                scope == ScopeMock
                    ? "Mark at least one Full Mock before starting a mock calibration run."
                    : "Mark at least one performance before starting a calibration run.");
        }

        var now = clock.GetUtcNow();
        var run = new SpeakingGraderCalibrationRun
        {
            Id = $"spgr_{Guid.NewGuid():N}",
            Repeats = Math.Clamp(request.Repeats ?? SpeakingGraderCalibrationMetrics.Thresholds.MinimumRepeats,
                SpeakingGraderCalibrationMetrics.Thresholds.MinimumRepeats, MaxRepeats),
            UseAudio = request.UseAudio ?? true,
            Scope = scope,
            Pilot = request.Pilot ?? false,
            Status = SpeakingGraderCalibrationRunStatus.Running,
            CreatedById = adminId,
            CreatedAt = now,
        };
        db.SpeakingGraderCalibrationRuns.Add(run);
        foreach (var sampleId in sampleIds)
        {
            for (var repeat = 1; repeat <= run.Repeats; repeat++)
            {
                db.SpeakingGraderCalibrationGrades.Add(new SpeakingGraderCalibrationGrade
                {
                    Id = $"spgg_{Guid.NewGuid():N}",
                    RunId = run.Id,
                    SampleId = sampleId,
                    Repeat = repeat,
                });
            }
        }

        db.AuditEvents.Add(AuditFor(adminId, adminName, "SpeakingGraderCalibrationRunStarted", run.Id,
            new { performances = sampleIds.Count, run.Repeats, run.UseAudio, run.Scope, run.Pilot, limited = request.SampleIds is { Count: > 0 } }));
        await db.SaveChangesAsync(ct);
        return await ToViewAsync(run, withReport: false, ct);
    }

    public async Task<IReadOnlyList<SpeakingGraderCalibrationRunView>> ListRunsAsync(CancellationToken ct)
    {
        // Ordered in memory: SQLite cannot ORDER BY a DateTimeOffset and a handful of runs ever exist.
        var runs = (await db.SpeakingGraderCalibrationRuns.AsNoTracking().ToListAsync(ct))
            .OrderByDescending(r => r.CreatedAt)
            .Take(20)
            .ToList();
        var views = new List<SpeakingGraderCalibrationRunView>(runs.Count);
        foreach (var run in runs) views.Add(await ToViewAsync(run, withReport: false, ct));
        return views;
    }

    public async Task<SpeakingGraderCalibrationRunView> GetRunAsync(string runId, CancellationToken ct)
    {
        var run = await LoadRunAsync(runId, tracking: false, ct);
        return await ToViewAsync(run, withReport: true, ct);
    }

    /// <summary>
    /// Queues the next calibration grade. <c>queued</c>: one grade is now running; <c>busy</c>: one is already running;
    /// <c>yield</c>: a learner's grade is queued or running, so nothing is started (try again shortly); <c>done</c>: every
    /// grade has finished (or used all its attempts), finalise the run; <c>complete</c>: the run is already finalised.
    /// </summary>
    public async Task<SpeakingGraderCalibrationNext> NextAsync(string runId, string adminId, CancellationToken ct)
    {
        var run = await LoadRunAsync(runId, tracking: true, ct);
        if (run.Status != SpeakingGraderCalibrationRunStatus.Running)
        {
            return new SpeakingGraderCalibrationNext("complete", null, await ProgressAsync(runId, ct));
        }

        var now = clock.GetUtcNow();
        var grades = await db.SpeakingGraderCalibrationGrades.Where(g => g.RunId == runId).ToListAsync(ct);

        // A queued grade whose operation was lost (a worker that never came back) is failed so it can be retried.
        foreach (var stuck in grades.Where(g => g.Status == SpeakingGraderCalibrationGradeStatus.Queued
                     && g.QueuedAt is { } queuedAt && now - queuedAt > QueuedGradeTimeout))
        {
            stuck.Status = SpeakingGraderCalibrationGradeStatus.Failed;
            stuck.Error = "operation_lost";
        }

        // A failed grade is tried again until it has used its attempts.
        foreach (var failed in grades.Where(g => g.Status == SpeakingGraderCalibrationGradeStatus.Failed && g.Attempts < MaxAttemptsPerGrade))
        {
            failed.Status = SpeakingGraderCalibrationGradeStatus.Pending;
        }

        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);

        if (grades.Any(g => g.Status == SpeakingGraderCalibrationGradeStatus.Queued))
        {
            return new SpeakingGraderCalibrationNext("busy", null, Progress(grades));
        }

        var next = grades
            .Where(g => g.Status == SpeakingGraderCalibrationGradeStatus.Pending)
            .OrderBy(g => g.Repeat)
            .ThenBy(g => g.SampleId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (next is null)
        {
            return new SpeakingGraderCalibrationNext("done", null, Progress(grades));
        }

        // Learners first: a calibration grade never waits in front of, or alongside, a learner's grade.
        var learnerBusy = await db.AiOperations.AsNoTracking().AnyAsync(o =>
            (o.FeatureCode == AiFeatureCodes.SpeakingGrade || o.FeatureCode == AiFeatureCodes.WritingGrade)
            && o.ResourceType != GradeResourceType
            && (o.State == AiOperationState.Queued
                || o.State == AiOperationState.Leased
                || o.State == AiOperationState.RetryScheduled), ct);
        if (learnerBusy)
        {
            return new SpeakingGraderCalibrationNext("yield", null, Progress(grades));
        }

        next.Attempts++;
        next.Status = SpeakingGraderCalibrationGradeStatus.Queued;
        next.QueuedAt = now;
        next.Error = null;
        next.OperationId = Guid.NewGuid().ToString("N");
        db.AiOperations.Add(new AiOperation
        {
            Id = next.OperationId,
            Module = "speaking",
            FeatureCode = AiFeatureCodes.SpeakingGrade,
            UserId = adminId,
            ResourceType = GradeResourceType,
            ResourceId = next.Id,
            IdempotencyKey = $"speaking.calibration.grade:{next.Id}:{next.Attempts}",
            ResourceSlotKey = AiOperationResourceSlot.Build(
                AiFeatureCodes.SpeakingGrade, "speaking", adminId, next.Id, GradeResourceType,
                resourceVersion: null, SpeakingAiAssessmentService.PromptTemplateId, null),
            State = AiOperationState.Queued,
            OperationClass = AiOperationClass.ScoringCritical,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        return new SpeakingGraderCalibrationNext("queued", next.Id, Progress(grades));
    }

    /// <summary>Runs one queued calibration grade (called by the operation worker, which holds the lease).</summary>
    public async Task ExecuteGradeAsync(string gradeId, CancellationToken ct)
    {
        var grade = await db.SpeakingGraderCalibrationGrades.FirstOrDefaultAsync(g => g.Id == gradeId, ct);
        if (grade is null) return;
        var operationId = grade.OperationId;
        if (grade.Status == SpeakingGraderCalibrationGradeStatus.Done)
        {
            await CompleteOperationAsync(operationId, AiOperationState.Completed, ct);
            return;
        }

        var run = await db.SpeakingGraderCalibrationRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == grade.RunId, ct);
        var sample = run?.Scope == ScopeMock
            ? null
            : await db.SpeakingGraderCalibrationSamples.AsNoTracking().FirstOrDefaultAsync(s => s.Id == grade.SampleId, ct);
        var mock = run?.Scope == ScopeMock
            ? await db.SpeakingGraderCalibrationMockSamples.AsNoTracking().FirstOrDefaultAsync(s => s.Id == grade.SampleId, ct)
            : null;
        try
        {
            if (assessor is null) throw new InvalidOperationException("The assessor is not available to the calibration harness.");
            if (run is null || (sample is null && mock is null))
                throw ApiException.NotFound("speaking_calibration_grade_orphaned", "The run or the performance no longer exists.");

            (SpeakingAiAssessmentService.SpeakingGradeOutcome Outcome, SpeakingAudioEvidence? Audio) result;
            if (mock is not null)
            {
                if ((await UnusableMockSampleIdsAsync([mock], ct)).Count > 0)
                {
                    throw ApiException.Conflict("speaking_calibration_sample_unavailable",
                        "The Full Mock's audio, transcript or consent is no longer available, so it cannot be graded.");
                }

                result = await assessor.GradeCombinedForCalibrationAsync(
                    mock.SpeakingExamId, mock.SessionAId, mock.SessionBId,
                    mock.TranscriptAId, mock.TranscriptBId, run.UseAudio, ct);
            }
            else
            {
                if ((await UnusableSampleIdsAsync([sample!], ct)).Count > 0)
                {
                    throw ApiException.Conflict("speaking_calibration_sample_unavailable",
                        "The performance's audio, transcript or consent is no longer available, so it cannot be graded.");
                }

                result = await assessor.GradeForCalibrationAsync(sample!.SpeakingSessionId, sample.TranscriptId, run.UseAudio, ct);
            }

            var (outcome, audio) = result;
            grade.ScoresJson = JsonSerializer.Serialize(new Dictionary<string, int>
            {
                ["intelligibility"] = outcome.Scores.Intelligibility,
                ["fluency"] = outcome.Scores.Fluency,
                ["appropriateness"] = outcome.Scores.Appropriateness,
                ["grammarExpression"] = outcome.Scores.GrammarExpression,
                ["relationshipBuilding"] = outcome.Scores.RelationshipBuilding,
                ["patientPerspective"] = outcome.Scores.PatientPerspective,
                ["structure"] = outcome.Scores.Structure,
                ["informationGathering"] = outcome.Scores.InformationGathering,
                ["informationGiving"] = outcome.Scores.InformationGiving,
            });
            grade.RawTotal = OetScoring.SpeakingRawTotal(outcome.Scores);
            grade.ReportedScaled = outcome.ReportedScaled;
            grade.IntelligibilitySource = audio is { IsAudio: true } ? "audio" : "transcript_only";
            grade.Provider = outcome.Provider;
            grade.ModelId = outcome.ModelId;
            grade.GraderVersion = outcome.GraderVersion;
            grade.DiagnosticsJson = SpeakingCalibrationDiagnosticsJson.Build(outcome, audio);
            grade.Status = SpeakingGraderCalibrationGradeStatus.Done;
            grade.CompletedAt = clock.GetUtcNow();
            grade.Error = null;
            await db.SaveChangesAsync(ct);
            await CompleteOperationAsync(operationId, AiOperationState.Completed, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            grade.Status = SpeakingGraderCalibrationGradeStatus.Failed;
            grade.Error = ex is ApiException api ? api.ErrorCode[..Math.Min(api.ErrorCode.Length, 64)] : "grade_failed";
            grade.CompletedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(CancellationToken.None);
            await CompleteOperationAsync(operationId, AiOperationState.FailedTerminal, CancellationToken.None);
        }
    }

    /// <summary>Freezes the report (numbers only) and closes the run.</summary>
    public async Task<SpeakingGraderCalibrationRunView> FinalizeAsync(
        string runId, string adminId, string adminName, CancellationToken ct)
    {
        var run = await LoadRunAsync(runId, tracking: true, ct);
        if (run.Status == SpeakingGraderCalibrationRunStatus.Complete)
        {
            return await ToViewAsync(run, withReport: false, ct);
        }

        var report = await BuildReportAsync(run, ct);
        var versions = await db.SpeakingGraderCalibrationGrades.AsNoTracking()
            .Where(g => g.RunId == runId && g.Status == SpeakingGraderCalibrationGradeStatus.Done && g.GraderVersion != null)
            .Select(g => g.GraderVersion!)
            .ToListAsync(ct);
        run.GraderVersion = versions.Count == 0
            ? string.Empty
            : versions.GroupBy(v => v, StringComparer.Ordinal).OrderByDescending(g => g.Count()).First().Key;
        run.ReportJson = JsonSerializer.Serialize(report, ReportJson);
        run.Status = SpeakingGraderCalibrationRunStatus.Complete;
        run.FinalizedAt = clock.GetUtcNow();
        db.AuditEvents.Add(AuditFor(adminId, adminName, "SpeakingGraderCalibrationRunFinalized", run.Id, new
        {
            report.Verdict.Passed,
            failures = report.Verdict.Failures.Count,
            report.Performances,
            report.Observations,
            report.Repeats,
            graderVersion = run.GraderVersion,
        }));
        await db.SaveChangesAsync(ct);
        return await ToViewAsync(run, withReport: false, ct);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<SpeakingGraderCalibrationRun> LoadRunAsync(string runId, bool tracking, CancellationToken ct)
    {
        var query = tracking ? db.SpeakingGraderCalibrationRuns : db.SpeakingGraderCalibrationRuns.AsNoTracking();
        return await query.FirstOrDefaultAsync(r => r.Id == runId, ct)
            ?? throw ApiException.NotFound("speaking_calibration_run_not_found", "That calibration run does not exist.");
    }

    private async Task<SpeakingGraderCalibrationRunView> ToViewAsync(
        SpeakingGraderCalibrationRun run, bool withReport, CancellationToken ct)
    {
        SpeakingCalibrationReport? report = null;
        if (run.Status == SpeakingGraderCalibrationRunStatus.Complete && !string.IsNullOrWhiteSpace(run.ReportJson))
        {
            report = JsonSerializer.Deserialize<SpeakingCalibrationReport>(run.ReportJson, ReportJson);
            // A list of runs carries the summary only; the grade-by-grade detail (and its diagnostics) is for the one run opened.
            if (!withReport && report is not null) report = report with { Detail = null };
        }
        else if (withReport)
        {
            report = await BuildReportAsync(run, ct);
        }

        return new SpeakingGraderCalibrationRunView(
            run.Id,
            run.Status.ToString().ToLowerInvariant(),
            run.GraderVersion,
            run.Repeats,
            run.UseAudio,
            run.CreatedAt,
            run.FinalizedAt,
            await ProgressAsync(run.Id, ct),
            report,
            string.IsNullOrWhiteSpace(run.Scope) ? ScopeCard : run.Scope,
            run.Pilot);
    }

    // Only the status of each grade is read: a grade row also carries its diagnostics, which progress never needs.
    private async Task<SpeakingGraderCalibrationRunProgress> ProgressAsync(string runId, CancellationToken ct)
        => Progress(await db.SpeakingGraderCalibrationGrades.AsNoTracking().Where(g => g.RunId == runId).Select(g => g.Status).ToListAsync(ct));

    private static SpeakingGraderCalibrationRunProgress Progress(IReadOnlyCollection<SpeakingGraderCalibrationGrade> grades)
        => Progress(grades.Select(g => g.Status).ToList());

    private static SpeakingGraderCalibrationRunProgress Progress(IReadOnlyCollection<SpeakingGraderCalibrationGradeStatus> statuses)
        => new(
            statuses.Count,
            statuses.Count(s => s == SpeakingGraderCalibrationGradeStatus.Pending),
            statuses.Count(s => s == SpeakingGraderCalibrationGradeStatus.Queued),
            statuses.Count(s => s == SpeakingGraderCalibrationGradeStatus.Done),
            statuses.Count(s => s == SpeakingGraderCalibrationGradeStatus.Failed));

    /// <summary>The report over the performances this run graded (a performance marked after the run began does not count).
    /// A mock-scope run compares the expert's ONE mark of each whole two-card test with the combined grader's grades.</summary>
    private async Task<SpeakingCalibrationReport> BuildReportAsync(SpeakingGraderCalibrationRun run, CancellationToken ct)
    {
        var grades = await db.SpeakingGraderCalibrationGrades.AsNoTracking().Where(g => g.RunId == run.Id).ToListAsync(ct);
        var inRun = grades.Select(g => g.SampleId).Distinct(StringComparer.Ordinal).ToList();
        var isMock = string.Equals(run.Scope, ScopeMock, StringComparison.Ordinal);

        var experts = new List<SpeakingCalibrationExpert>();
        if (isMock)
        {
            var mocks = await db.SpeakingGraderCalibrationMockSamples.AsNoTracking()
                .Where(s => inRun.Contains(s.Id)
                    && s.Status == SpeakingGraderCalibrationSampleStatus.Labelled
                    && s.ExpertScoresJson != null
                    && s.ExpertOverallScaled != null)
                .ToListAsync(ct);
            experts.AddRange(mocks
                .Select(s => new SpeakingCalibrationExpert(s.Id, s.HasAudio, ParseScores(s.ExpertScoresJson), s.ExpertOverallScaled!.Value)));
        }
        else
        {
            var samples = await db.SpeakingGraderCalibrationSamples.AsNoTracking()
                .Where(s => inRun.Contains(s.Id)
                    && s.Status == SpeakingGraderCalibrationSampleStatus.Labelled
                    && s.ExpertScoresJson != null
                    && s.ExpertOverallScaled != null)
                .ToListAsync(ct);
            experts.AddRange(samples
                .Select(s => new SpeakingCalibrationExpert(s.Id, s.HasAudio, ParseScores(s.ExpertScoresJson), s.ExpertOverallScaled!.Value)));
        }

        var observations = grades
            .Where(g => g.Status == SpeakingGraderCalibrationGradeStatus.Done && g.ScoresJson != null)
            .Select(g => new SpeakingCalibrationObservation(
                g.SampleId, g.Repeat, ParseScores(g.ScoresJson), g.IntelligibilitySource ?? "transcript_only",
                g.ReportedScaled, SpeakingCalibrationDiagnosticsJson.Parse(g.DiagnosticsJson)))
            .ToList();
        var report = SpeakingGraderCalibrationMetrics.Compute(experts, observations, run.UseAudio, pilot: run.Pilot);
        // The "Provisional" label is earned per exact grader version + model, so say how many grades each produced.
        var versions = grades
            .Where(g => g.Status == SpeakingGraderCalibrationGradeStatus.Done && !string.IsNullOrWhiteSpace(g.GraderVersion))
            .GroupBy(g => $"{g.GraderVersion} · {g.Provider}/{g.ModelId}", StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        return report with { GraderVersions = versions, MappingVersion = OetScoring.SpeakingMappingVersion };
    }

    private static Dictionary<string, int> ParseScores(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, int>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new Dictionary<string, int>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, int>();
        }
    }

    private async Task CompleteOperationAsync(string? operationId, AiOperationState state, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(operationId)) return;
        var operation = await db.AiOperations.FirstOrDefaultAsync(o => o.Id == operationId, ct);
        if (operation is null) return;
        operation.State = state;
        operation.NextAttemptAt = null;
        operation.LeaseOwner = null;
        operation.LeaseExpiresAt = null;
        operation.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    private AuditEvent AuditFor(string adminId, string adminName, string action, string runId, object details)
        => new()
        {
            Id = $"audit-{Guid.NewGuid():N}",
            OccurredAt = clock.GetUtcNow(),
            ActorId = adminId,
            ActorName = adminName,
            Action = action,
            ResourceType = "SpeakingGraderCalibrationRun",
            ResourceId = runId,
            Details = JsonSerializer.Serialize(details),
        };
}
