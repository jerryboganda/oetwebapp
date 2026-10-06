using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.FreeSamples;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Speaking;

public sealed record SpeakingCorpusStage(string Name, bool Ok, string Detail);

public sealed record SpeakingCorpusCardResult(
    string CardId,
    int? DisplayCardNumber,
    string ProfessionId,
    string Title,
    bool Passed,
    string? FailedStage,
    string? Error,
    IReadOnlyList<SpeakingCorpusStage> Stages);

/// <param name="State"><c>ok</c> (free card resolved and passed every stage) |
/// <c>controlled_unavailable</c> (no eligible card: learners get the controlled
/// unavailable state) | <c>free_card_failing</c> (a card is offered but fails a stage).</param>
/// <param name="WritingState"><c>ok</c> | <c>controlled_unavailable</c>.</param>
public sealed record SpeakingCorpusProfessionRow(
    string ProfessionId,
    int PublishedCards,
    int CandidateLoadableCards,
    string? FreeSampleCardId,
    bool? FreeSampleCardPassed,
    string State,
    string? WritingFreeSampleScenarioId,
    string WritingState);

public sealed record SpeakingCorpusTotals(int Cards, int Passed, int Failed);

public sealed record SpeakingCorpusReport(
    string Mode,
    DateTimeOffset GeneratedAt,
    int Offset,
    int Limit,
    int TotalPublishedCards,
    int? NextOffset,
    bool FreeSamplesEnabled,
    SpeakingCorpusTotals Totals,
    IReadOnlyList<SpeakingCorpusProfessionRow> Professions,
    IReadOnlyList<SpeakingCorpusCardResult> Cards);

/// <summary>
/// $0 full-corpus Speaking compatibility harness (owner PDF §10B/§10C). For every
/// published role-play card it runs the real learner path (load → consent →
/// warm-up → prep → role-play → scripted candidate-first transcript → end →
/// submit → grading → result state) and names the exact card + stage that fails.
///
/// Zero provider spend, zero persistence: each card runs in its own DI scope and
/// DB transaction that is ALWAYS rolled back; no audio blob is written, no
/// transcription provider is called, live-voice readiness never generates (so
/// never calls Jev), and the grader response is a CANNED model reply fed through
/// the real parse/clamp/scale path. Results are labelled
/// <see cref="Mode"/> so they are never mistaken for real-voice evidence.
/// </summary>
public sealed class SpeakingCorpusCompatibilityService(
    IServiceScopeFactory scopes,
    ILogger<SpeakingCorpusCompatibilityService> logger)
{
    public const string Mode = "controlled-no-provider";
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;
    public const string ScriptedTranscriptProvider = "corpus-harness-scripted";

    public const string StagePublished = "card_published";
    public const string StageProjection = "learner_projection";
    public const string StageReadiness = "live_voice_readiness";
    public const string StageInstructions = "ai_patient_instructions";
    public const string StageSession = "session_state_machine";
    public const string StageGrading = "grading_contract";

    private const string CandidateFirstRule = "CANDIDATE FIRST: never speak first";
    private const string ContextOnlyLabel = "FOR CONTEXT ONLY";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly string[] ForbiddenLearnerKeyParts = ["emotion", "goal", "topic"];
    private static readonly string[] CriterionCodes =
    [
        "intelligibility", "fluency", "appropriateness", "grammarExpression",
        "relationshipBuilding", "patientPerspective", "structure",
        "informationGathering", "informationGiving",
    ];

    private sealed record CardRow(RolePlayCard Card, bool HasContentItem, ContentStatus? ItemStatus, string? ItemSubtest)
    {
        public string Profession => FreeSampleService.NormalizeProfession(Card.ProfessionId);
    }

    /// <summary>A failed check; its message becomes the stage detail.</summary>
    private sealed class HarnessCheckException(string message) : Exception(message);

    public async Task<SpeakingCorpusReport> RunAsync(
        string adminId,
        string? profession,
        string? cardId,
        int? offset,
        int? limit,
        CancellationToken ct)
    {
        var skip = Math.Max(0, offset ?? 0);
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var wantedProfession = FreeSampleService.NormalizeProfession(profession);

        List<CardRow> all;
        bool freeSamplesEnabled;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var rows = await (
                from card in db.RolePlayCards.AsNoTracking()
                join item in db.ContentItems.AsNoTracking()
                    on card.ContentItemId equals item.Id into joined
                from item in joined.DefaultIfEmpty()
                where card.Status == ContentStatus.Published
                select new
                {
                    Card = card,
                    HasContentItem = item != null,
                    ItemStatus = item != null ? (ContentStatus?)item.Status : null,
                    ItemSubtest = item != null ? item.SubtestCode : null,
                })
                .ToListAsync(ct);
            all = rows
                .Select(r => new CardRow(r.Card, r.HasContentItem, r.ItemStatus, r.ItemSubtest))
                .OrderBy(r => r.Profession, StringComparer.Ordinal)
                .ThenBy(r => r.Card.DisplayCardNumber ?? int.MaxValue)
                .ThenBy(r => r.Card.Id, StringComparer.Ordinal)
                .ToList();
            freeSamplesEnabled = (await db.FeatureFlags.AsNoTracking()
                .Where(f => f.Key == FreeSampleService.FeatureFlagKey)
                .OrderByDescending(f => f.UpdatedAt)
                .FirstOrDefaultAsync(ct))?.Enabled ?? false;
        }

        var filtered = all
            .Where(r => wantedProfession.Length == 0 || r.Profession == wantedProfession)
            .Where(r => string.IsNullOrWhiteSpace(cardId)
                || r.Card.Id == cardId.Trim()
                || r.Card.ContentItemId == cardId.Trim())
            .ToList();

        logger.LogInformation(
            "Speaking corpus harness ({Mode}) started by admin {AdminId}: profession={Profession} cardId={CardId} offset={Offset} limit={Limit} of {Total} cards.",
            Mode, adminId, wantedProfession, cardId, skip, take, filtered.Count);

        // ponytail: sequential, one scope + rolled-back transaction per card
        // (~50 cards per request); paging keeps a full 417-card pass inside the
        // request timeout. Parallelise per card only if paging becomes a chore.
        var results = new List<SpeakingCorpusCardResult>();
        foreach (var row in filtered.Skip(skip).Take(take))
        {
            results.Add(await CheckCardAsync(row, ct));
        }

        // §10C: once per full pass (first page) so paging does not re-run it.
        IReadOnlyList<SpeakingCorpusProfessionRow> professions = skip == 0
            ? await BuildProfessionRowsAsync(filtered, all, results, ct)
            : [];

        var passed = results.Count(r => r.Passed);
        var nextOffset = skip + take < filtered.Count ? skip + take : (int?)null;
        logger.LogInformation(
            "Speaking corpus harness ({Mode}) finished for admin {AdminId}: {Passed}/{Count} cards passed.",
            Mode, adminId, passed, results.Count);

        return new SpeakingCorpusReport(
            Mode,
            DateTimeOffset.UtcNow,
            skip,
            take,
            filtered.Count,
            nextOffset,
            freeSamplesEnabled,
            new SpeakingCorpusTotals(results.Count, passed, results.Count - passed),
            professions,
            results);
    }

    private async Task<IReadOnlyList<SpeakingCorpusProfessionRow>> BuildProfessionRowsAsync(
        List<CardRow> filtered,
        List<CardRow> all,
        List<SpeakingCorpusCardResult> pageResults,
        CancellationToken ct)
    {
        var rows = new List<SpeakingCorpusProfessionRow>();
        foreach (var group in filtered.GroupBy(r => r.Profession).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            string? speakingPick;
            string? writingPick;
            await using (var scope = scopes.CreateAsyncScope())
            {
                var freeSamples = new FreeSampleService(scope.ServiceProvider.GetRequiredService<LearnerDbContext>());
                speakingPick = await freeSamples.ResolvePickAsync(FreeSampleService.Speaking, group.Key, ct);
                writingPick = await freeSamples.ResolvePickAsync(FreeSampleService.Writing, group.Key, ct);
            }

            bool? pickPassed = null;
            if (speakingPick is not null)
            {
                var pickResult = pageResults.FirstOrDefault(r => r.CardId == speakingPick);
                if (pickResult is null && all.FirstOrDefault(r => r.Card.Id == speakingPick) is { } pickRow)
                {
                    pickResult = await CheckCardAsync(pickRow, ct);
                }
                pickPassed = pickResult?.Passed ?? false;
            }

            rows.Add(new SpeakingCorpusProfessionRow(
                ProfessionId: group.Key,
                PublishedCards: group.Count(),
                CandidateLoadableCards: group.Count(IsCandidateLoadable),
                FreeSampleCardId: speakingPick,
                FreeSampleCardPassed: pickPassed,
                State: speakingPick is null ? "controlled_unavailable" : pickPassed == true ? "ok" : "free_card_failing",
                WritingFreeSampleScenarioId: writingPick,
                WritingState: writingPick is null ? "controlled_unavailable" : "ok"));
        }
        return rows;
    }

    private static bool IsCandidateLoadable(CardRow row)
        => Run(StagePublished, () => CheckPublished(row)).Ok
           && Run(StageProjection, () => CheckProjection(SpeakingSessionService.ProjectLearnerCard(row.Card))).Ok;

    private async Task<SpeakingCorpusCardResult> CheckCardAsync(CardRow row, CancellationToken ct)
    {
        var card = row.Card;
        var stages = new List<SpeakingCorpusStage>
        {
            Run(StagePublished, () => CheckPublished(row)),
            Run(StageProjection, () => CheckProjection(SpeakingSessionService.ProjectLearnerCard(card))),
        };

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<LearnerDbContext>();

            var script = await db.InterlocutorScripts.AsNoTracking()
                .FirstOrDefaultAsync(s => s.RolePlayCardId == card.Id, ct);
            var readiness = LiveVoiceContentReadinessService.TryResolveExisting(card, script);
            stages.Add(Run(StageReadiness, () => CheckReadiness(script, readiness)));
            stages.Add(Run(StageInstructions, () => CheckInstructions(card, readiness)));
            stages.AddRange(await RunSessionAndGradingAsync(sp, db, card, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Scope/DB failure before the per-stage guards: still name the card.
            foreach (var name in new[] { StageReadiness, StageInstructions, StageSession, StageGrading })
            {
                if (stages.All(s => s.Name != name)) stages.Add(new SpeakingCorpusStage(name, false, Describe(ex)));
            }
        }

        var failed = stages.FirstOrDefault(s => !s.Ok);
        return new SpeakingCorpusCardResult(
            card.Id,
            card.DisplayCardNumber,
            card.ProfessionId,
            card.ScenarioTitle,
            failed is null,
            failed?.Name,
            failed?.Detail,
            stages);
    }

    // ── a. card_published ────────────────────────────────────────────────

    private static SpeakingCorpusStage CheckPublished(CardRow row)
    {
        var card = row.Card;
        if (card.Status != ContentStatus.Published)
            throw new HarnessCheckException($"card status is {card.Status}, not Published");
        if (string.IsNullOrWhiteSpace(card.ProfessionId))
            throw new HarnessCheckException("card has no profession");
        if (!row.HasContentItem)
            throw new HarnessCheckException($"linked content item '{card.ContentItemId}' does not exist");
        if (row.ItemStatus != ContentStatus.Published)
            throw new HarnessCheckException($"linked content item '{card.ContentItemId}' is {row.ItemStatus}, not Published");
        if (!string.Equals(row.ItemSubtest, FreeSampleService.Speaking, StringComparison.OrdinalIgnoreCase))
            throw new HarnessCheckException($"linked content item '{card.ContentItemId}' subtest is '{row.ItemSubtest}', not speaking");
        return new SpeakingCorpusStage(StagePublished, true,
            $"card and content item '{card.ContentItemId}' are Published (speaking, profession {card.ProfessionId})");
    }

    // ── b. learner_projection ────────────────────────────────────────────

    /// <summary>The learner card must carry profession/role, setting, background
    /// and non-empty tasks, and must never carry Emotion / Goal / Topic keys.</summary>
    internal static SpeakingCorpusStage CheckProjection(object projection)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(projection, WebJson));
        var root = doc.RootElement;

        var leaked = ForbiddenKeys(root).Distinct(StringComparer.Ordinal).ToArray();
        if (leaked.Length > 0)
            throw new HarnessCheckException($"learner card leaks internal field(s): {string.Join(", ", leaked)}");

        var missing = new List<string>();
        if (!HasText(root, "professionId") || !HasText(root, "candidateRole")) missing.Add("role/profession");
        if (!HasText(root, "setting")) missing.Add("setting");
        if (!HasText(root, "background")) missing.Add("background");
        var taskCount = root.TryGetProperty("tasks", out var tasks) && tasks.ValueKind == JsonValueKind.Array
            ? tasks.GetArrayLength()
            : 0;
        if (taskCount == 0
            || tasks.EnumerateArray().Any(t => t.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(t.GetString())))
        {
            missing.Add("tasks");
        }
        if (missing.Count > 0)
            throw new HarnessCheckException($"learner card is missing required section(s): {string.Join(", ", missing)}");

        return new SpeakingCorpusStage(StageProjection, true,
            $"role/profession, setting, background and {taskCount} task(s) present; no emotion/goal/topic keys");
    }

    private static IEnumerable<string> ForbiddenKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (ForbiddenLearnerKeyParts.Any(part => property.Name.Contains(part, StringComparison.OrdinalIgnoreCase)))
                    yield return property.Name;
                foreach (var nested in ForbiddenKeys(property.Value)) yield return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                foreach (var nested in ForbiddenKeys(item)) yield return nested;
        }
    }

    private static bool HasText(JsonElement root, string property)
        => root.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(value.GetString());

    // ── c. live_voice_readiness ──────────────────────────────────────────

    private static SpeakingCorpusStage CheckReadiness(InterlocutorScript? script, LiveVoiceContentReadiness? readiness)
    {
        if (script is null)
            throw new HarnessCheckException(
                "no interlocutor script: live voice would have to generate a projection (not attempted in $0 mode)");
        if (readiness is null)
            throw new HarnessCheckException(
                $"generated projection is stale or not Jev-validated (jevValidationStatus={script.JevValidationStatus ?? "none"})");
        if (readiness.NeedsOwnerInput)
            throw new HarnessCheckException(
                $"interlocutor script needs owner input ({readiness.Provenance}, jev={readiness.JevValidationStatus})");
        return new SpeakingCorpusStage(StageReadiness, true,
            $"{readiness.Provenance} ready (jev={readiness.JevValidationStatus})");
    }

    // ── d. ai_patient_instructions ───────────────────────────────────────

    private static SpeakingCorpusStage CheckInstructions(RolePlayCard card, LiveVoiceContentReadiness? readiness)
    {
        if (readiness is null)
            throw new HarnessCheckException("blocked: no resolved interlocutor script to build the AI patient from");

        var instructions = LiveVoiceService.BuildInstructions(card, readiness.Script, readiness);
        if (!instructions.Contains(CandidateFirstRule, StringComparison.Ordinal))
            throw new HarnessCheckException("AI patient instructions lack the candidate-first rule");
        var contextLabel = instructions.IndexOf(ContextOnlyLabel, StringComparison.Ordinal);
        if (contextLabel < 0)
            throw new HarnessCheckException($"AI patient instructions lack the '{ContextOnlyLabel}' candidate-card label");

        // Candidate task text may only appear inside the labelled card-data block.
        var leakedTask = card.Tasks.FirstOrDefault(task =>
        {
            var at = string.IsNullOrWhiteSpace(task) ? -1 : instructions.IndexOf(task.Trim(), StringComparison.Ordinal);
            return at >= 0 && at < contextLabel;
        });
        if (leakedTask is not null)
            throw new HarnessCheckException($"candidate task text appears outside the '{ContextOnlyLabel}' block: '{leakedTask}'");

        return new SpeakingCorpusStage(StageInstructions, true,
            $"built ({instructions.Length} chars): candidate-first rule present, candidate card data only under '{ContextOnlyLabel}'");
    }

    // ── e + f. session_state_machine + grading_contract (rolled back) ──

    private async Task<SpeakingCorpusStage[]> RunSessionAndGradingAsync(
        IServiceProvider sp,
        LearnerDbContext db,
        RolePlayCard card,
        CancellationToken ct)
    {
        // Every SaveChanges of this scope's DbContext enlists in this
        // transaction and it is always rolled back. The InMemory provider (test
        // host only) has no transactions: its rows stay in the throwaway store.
        var transactional = db.Database.IsRelational();
        await using var tx = transactional ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            var (sessionStage, userId, sessionId) = await RunStateMachineAsync(sp, db, card, transactional, ct);
            var gradingStage = sessionId is null
                ? new SpeakingCorpusStage(StageGrading, false, $"blocked: {StageSession} did not reach submit")
                : await RunGradingAsync(sp, db, userId!, sessionId, ct);
            return [sessionStage, gradingStage];
        }
        finally
        {
            if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
        }
    }

    private static async Task<(SpeakingCorpusStage Stage, string? UserId, string? SessionId)> RunStateMachineAsync(
        IServiceProvider sp,
        LearnerDbContext db,
        RolePlayCard card,
        bool transactional,
        CancellationToken ct)
    {
        var step = "create_learner";
        try
        {
            var now = DateTimeOffset.UtcNow;
            var userId = $"corpus-harness-{Guid.NewGuid():N}";
            db.Users.Add(new LearnerUser
            {
                Id = userId,
                DisplayName = "Speaking corpus harness (rolled back)",
                Email = $"{userId}@corpus-harness.invalid",
                ActiveProfessionId = card.ProfessionId,
                AccountStatus = "active",
                OnboardingCompleted = true,
                CreatedAt = now,
                LastActiveAt = now,
            });
            await db.SaveChangesAsync(ct);

            // Enough for the 2-credit practice hold, through the real admin
            // ledger path. The designated free card needs none (server decides).
            step = "grant_credits";
            await sp.GetRequiredService<IAiPackageCreditService>().AdjustAsync(
                userId,
                new AiPackageCreditAdjustmentRequest(
                    FlexibleCreditsDelta: 0,
                    WritingOnlyCreditsDelta: 0,
                    SpeakingOnlyCreditsDelta: 4,
                    ListeningTestsDelta: 0,
                    ReadingTestsDelta: 0,
                    MockExamsDelta: 0,
                    ExpiresAt: null,
                    Reason: "Speaking corpus harness (rolled back)"),
                "corpus-harness",
                ct);

            var sessions = sp.GetRequiredService<SpeakingSessionService>();

            step = "create_session";
            var created = await sessions.CreateSessionAsync(
                userId, new CreateSpeakingSessionRequest(card.Id, "ai_self_practice"), ct);
            var sessionId = created.SessionId;
            CheckProjection(created.Card);

            step = "consent";
            await sessions.MarkConsentAsync(userId, sessionId, "recording.v1", ct);

            step = "start_warmup";
            await sessions.StartWarmupAsync(userId, sessionId, ct);

            step = "finish_warmup";
            var warmupDone = await sessions.FinishWarmupAsync(userId, sessionId, ct);
            if (warmupDone.Admission is not null)
            {
                // The live-session cap is full (admission queue): the harness cannot judge the prep timer.
                throw new HarnessCheckException(
                    $"live Speaking capacity is full (queue position {warmupDone.Admission.Position}); retry when it frees");
            }
            var prepClock = await sessions.GetClockAsync(userId, sessionId, ct);
            if (prepClock.Stage != "prep" || prepClock.StageEndsAt is null)
                throw new HarnessCheckException($"prep timer not running (stage={prepClock.Stage})");

            step = "start_roleplay";
            await sessions.StartRolePlayAsync(userId, sessionId, ct);
            var activeClock = await sessions.GetClockAsync(userId, sessionId, ct);
            if (activeClock.Stage != "active" || activeClock.StageEndsAt is null)
                throw new HarnessCheckException($"role-play timer not running (stage={activeClock.Stage})");

            step = "scripted_transcript";
            var turns = await WriteScriptedTranscriptAsync(sp, db, card, userId, sessionId, ct);

            step = "end";
            await sessions.EndSessionAsync(userId, sessionId, ct);

            step = "submit";
            var submitted = await sessions.SubmitForMarkingAsync(userId, sessionId, ct);
            if (submitted.SubmittedAt is null)
                throw new HarnessCheckException("submit did not stamp SubmittedAt");

            var storeNote = transactional ? "rolled back" : "in-memory provider: no transaction";
            return (new SpeakingCorpusStage(StageSession, true,
                $"consent → warm-up → prep (timer) → role-play (timer) → {turns} scripted candidate-first turns → end → submit; " +
                $"freeSample={created.IsFreeSample}; {storeNote}"), userId, sessionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (new SpeakingCorpusStage(StageSession, false, $"{step}: {Describe(ex)}"), null, null);
        }
    }

    /// <summary>
    /// Recorder-fallback evidence without audio or STT: the same recording row
    /// shape <see cref="SpeakingSessionRecordingService.ReceiveAsync"/> writes
    /// (no blob), the real transcription queue row, then the real
    /// <see cref="SpeakingTranscriptionPipeline.PromoteLatestAsync"/> with a
    /// deterministic scripted result in place of the provider's.
    /// </summary>
    private static async Task<int> WriteScriptedTranscriptAsync(
        IServiceProvider sp,
        LearnerDbContext db,
        RolePlayCard card,
        string userId,
        string sessionId,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var recordingId = SpeakingSessionRecordingService.RecordingIdFor(sessionId);
        var mediaAssetId = $"smed_{Guid.NewGuid():N}";
        db.MediaAssets.Add(new MediaAsset
        {
            Id = mediaAssetId,
            OriginalFilename = $"{recordingId}.webm",
            MimeType = "audio/webm",
            Format = "webm",
            SizeBytes = 1,
            DurationSeconds = 290,
            StoragePath = $"speaking/sessions/{sessionId}/{mediaAssetId}.webm",
            Status = MediaAssetStatus.Ready,
            MediaKind = "audio",
            UploadedBy = userId,
            UploadedAt = now,
            ProcessedAt = now,
        });
        db.SpeakingRecordings.Add(new SpeakingRecording
        {
            Id = recordingId,
            SpeakingSessionId = sessionId,
            MediaAssetId = mediaAssetId,
            Kind = SpeakingRecordingKind.Audio,
            Source = SpeakingRecordingSource.ClientMediaRecorder,
            DurationSeconds = 290,
            SizeBytes = 1,
            Sha256 = string.Empty,
            MimeType = "audio/webm",
            ConsentVersion = "recording.v1",
            IsArchived = false,
            RetentionExpiresAt = now.AddDays(1),
            IsWarmup = false,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);

        await sp.GetRequiredService<SpeakingTranscriptionPipeline>().EnqueueAsync(sessionId, recordingId, ct);
        var queued = await db.SpeakingTranscripts
            .FirstAsync(t => t.SpeakingSessionId == sessionId && t.Provider == SpeakingTranscriptionPipeline.StateQueued, ct);

        var turns = BuildScriptedTurns(card);
        var segments = turns.Select((turn, i) => new
        {
            speaker = turn.Speaker,
            startMs = i * 20_000,
            endMs = i * 20_000 + 15_000,
            text = turn.Text,
            confidence = 1.0,
            words = Array.Empty<object>(),
        });
        await SpeakingTranscriptionPipeline.PromoteLatestAsync(db, queued, new SpeakingTranscriptionProviderResult
        {
            Provider = ScriptedTranscriptProvider,
            Language = "en",
            SegmentsJson = JsonSerializer.Serialize(segments),
            WordCount = turns.Sum(t => t.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length),
            MeanConfidence = 1.0,
        }, ScriptedTranscriptProvider, ct);
        await db.SaveChangesAsync(ct);
        return turns.Count;
    }

    /// <summary>Eight deterministic turns, candidate first, built from the card's tasks.</summary>
    internal static IReadOnlyList<(string Speaker, string Text)> BuildScriptedTurns(RolePlayCard card)
    {
        var tasks = card.Tasks.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToArray();
        string TaskAt(int i) => tasks.Length == 0 ? card.ScenarioTitle : tasks[i % tasks.Length];
        return
        [
            ("candidate", $"Hello, I am the {card.CandidateRole} looking after you today. How can I help you?"),
            ("patient", "Hello. I would like to discuss the situation today."),
            ("candidate", $"Thank you. First, {TaskAt(0)}"),
            ("patient", "I see. Could you explain that in a little more detail?"),
            ("candidate", $"Of course. Next, {TaskAt(1)}"),
            ("patient", "That makes sense. Is there anything else I should know?"),
            ("candidate", $"Yes. Finally, {TaskAt(2)} Do you have any questions?"),
            ("patient", "No, thank you for explaining that."),
        ];
    }

    private static async Task<SpeakingCorpusStage> RunGradingAsync(
        IServiceProvider sp,
        LearnerDbContext db,
        string userId,
        string sessionId,
        CancellationToken ct)
    {
        var step = "build_assessor";
        try
        {
            // Real assessor + canonical operation; only the model reply is canned.
            var gateway = new CannedGraderGateway(sp.GetRequiredService<AiGatewayService>());
            var classic = new SpeakingAiAssessmentService(
                db, gateway, sp.GetRequiredService<ILogger<SpeakingAiAssessmentService>>());
            var canonical = new SpeakingCanonicalAssessmentService(
                db,
                classic,
                sp.GetRequiredService<SpeakingSimulationV11AssessmentService>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<SpeakingCanonicalAssessmentService>>(),
                sp.GetService<OetLearner.Api.Services.Ai.IAiCreditReservationService>());

            step = "route";
            if (await canonical.UsesV11Async(sessionId, ct))
                throw new HarnessCheckException("session routes to the v1.1 assessor; canned classic grading not attempted");

            step = "assess";
            await canonical.AssessNowAsync(sessionId, ct);
            if (gateway.Calls != 1)
                throw new HarnessCheckException($"expected exactly one canned grader call, saw {gateway.Calls}");

            step = "result";
            var latest = await classic.GetLatestAsync(sessionId, ct)
                ?? throw new HarnessCheckException("no assessment persisted");
            if (latest.EstimatedScaledScore is < 0 or > 500)
                throw new HarnessCheckException($"scaled score {latest.EstimatedScaledScore} is outside 0-500");
            var missing = CriterionCodes.Where(code => !latest.CriterionScores.ContainsKey(code)).ToArray();
            if (missing.Length > 0)
                throw new HarnessCheckException($"criteria map incomplete: missing {string.Join(", ", missing)}");

            // What GET /v1/speaking/sessions/{id}/results serves.
            var state = await canonical.GetStateAsync(sessionId, ct);
            if (state.AssessmentState != SpeakingAssessmentState.Completed)
                throw new HarnessCheckException($"results assessmentState is '{state.AssessmentState}', not completed");
            var detail = await sp.GetRequiredService<SpeakingSessionService>().GetSessionForLearnerAsync(userId, sessionId, ct);

            return new SpeakingCorpusStage(StageGrading, true,
                $"CANNED grader response (no provider call) through the real parse/clamp/scale path: " +
                $"{latest.EstimatedScaledScore}/500, {CriterionCodes.Length}/{CriterionCodes.Length} criteria; " +
                $"results assessmentState=completed, card {detail.RolePlayCardId}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SpeakingCorpusStage(StageGrading, false, $"{step}: {Describe(ex)}");
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static SpeakingCorpusStage Run(string name, Func<SpeakingCorpusStage> check)
    {
        try
        {
            return check();
        }
        catch (Exception ex)
        {
            return new SpeakingCorpusStage(name, false, Describe(ex));
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        HarnessCheckException => ex.Message,
        ApiException api => $"{api.ErrorCode}: {api.Message}",
        _ => $"{ex.GetType().Name}: {ex.Message}",
    };

    /// <summary>
    /// The only IAiGatewayService the harness hands the assessor: prompt
    /// grounding is the real rulebook builder (no network), and the completion
    /// is a fixed, valid model reply. It never reaches a provider.
    /// </summary>
    private sealed class CannedGraderGateway(AiGatewayService grounding) : IAiGatewayService
    {
        public int Calls { get; private set; }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => grounding.BuildGroundedPrompt(context);

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new AiGatewayResult { Completion = CannedAssessmentJson });
        }
    }

    internal static readonly string CannedAssessmentJson = JsonSerializer.Serialize(new
    {
        criterionScores = new
        {
            intelligibility = new { score = 4, rationale = "Canned harness score.", evidenceQuotes = Array.Empty<string>() },
            fluency = new { score = 4, rationale = "Canned harness score.", evidenceQuotes = Array.Empty<string>() },
            appropriateness = new { score = 4, rationale = "Canned harness score.", evidenceQuotes = Array.Empty<string>() },
            grammarExpression = new { score = 4, rationale = "Canned harness score.", evidenceQuotes = Array.Empty<string>() },
            relationshipBuilding = new { score = 2, rationale = "Canned harness score.", evidenceQuotes = Array.Empty<string>() },
            patientPerspective = new { score = 2, rationale = "Canned harness score.", evidenceQuotes = Array.Empty<string>() },
            structure = new { score = 2, rationale = "Canned harness score.", evidenceQuotes = Array.Empty<string>() },
            informationGathering = new { score = 2, rationale = "Canned harness score.", evidenceQuotes = Array.Empty<string>() },
            informationGiving = new { score = 2, rationale = "Canned harness score.", evidenceQuotes = Array.Empty<string>() },
        },
        readinessBand = "borderline",
        overallSummary = "Canned corpus-harness response. Not a real assessment.",
        confidenceBand = "medium",
        strengths = Array.Empty<string>(),
        improvements = Array.Empty<string>(),
        recommendedDrillKinds = Array.Empty<string>(),
    });
}
