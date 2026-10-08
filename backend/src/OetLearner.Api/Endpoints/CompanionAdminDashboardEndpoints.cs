using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// The three companion operator dashboards SAMI handover §13.2 requires and that
/// nothing served until now:
///
/// <list type="bullet">
///   <item><b>F-127 · AI quality</b> — <c>/v1/admin/companion/quality</c>:
///     per-feature completion/failure counts and error-code classes read straight
///     from <see cref="AiUsageRecord"/>, citation coverage on companion answers
///     read from <see cref="AiAssistantMessage.CitationsJson"/>, and the
///     unresolved learner handoff backlog.</item>
///   <item><b>F-128 · Content gap</b> — <c>/v1/admin/companion/content-gaps</c>:
///     which (profession, subtest) scopes have no retrievable approved source and
///     therefore cannot ground an answer, plus the topics with the highest count
///     of companion answers that cited nothing, plus the raw learner-raised
///     issues the companion could not resolve.</item>
///   <item><b>F-129 · Teaching gap</b> — <c>/v1/admin/companion/teaching-gaps</c>:
///     aggregate <see cref="ErrorDnaEntry"/> rows — what learners repeatedly get
///     wrong, by category, subtest and pattern.</item>
/// </list>
///
/// <para>
/// <b>The governing rule of this file is that no number is invented.</b> Every
/// count below is a <c>COUNT</c>/<c>GROUP BY</c> over a column that the runtime
/// already writes. Where the handover asks for a signal that has no data source
/// at all, the endpoint returns an explicit <c>notInstrumented</c> entry saying
/// so — never a zero, because a zero reads as "we measured this and it is fine",
/// which is the opposite of the truth. Each such signal is named with the reason
/// it cannot be measured yet, so the gap is actionable rather than merely absent.
/// </para>
///
/// <para>
/// Two vocabularies are derived at read time rather than stored, and both are
/// stated in the payload so the dashboard never has to guess:
/// <list type="number">
///   <item>
///     A companion answer is an <see cref="AiAssistantMessage"/> whose thread
///     <see cref="AiAssistantThread.Role"/> is not admin/expert. Companion turns
///     currently run on <see cref="AiFeatureCodes.AiAssistantLearner"/>, because
///     that is the feature code <c>AiAssistantOrchestrator.GetFeatureCode</c>
///     resolves for the learner role, and the orchestrator records one usage row
///     per physical provider call under it. <see cref="AiFeatureCodes.CompanionChat"/>
///     is declared, quota-planned and entitlement-checked, but no call site writes
///     a usage row under it yet — so a zero for that code is a fact about the
///     build, not a measurement failure, and the dashboard says so on screen.
///   </item>
///   <item>
///     Failure classes are prefix/exact matches over the error-code vocabulary the
///     gateway actually writes. An unrecognised code lands in <c>other</c> rather
///     than being quietly reclassified, so a new code is visible immediately.
///   </item>
/// </list>
/// </para>
///
/// <para>
/// Read-only and additive: every route is a <c>GET</c>, nothing is written, no
/// audit row is produced, and no existing endpoint, migration or credit/quota
/// path is touched. Authorisation matches
/// <see cref="CompanionAccessAdminEndpoints"/> and
/// <see cref="CompanionKnowledgeAdminEndpoints"/> (<c>AdminAiConfig</c>);
/// the feature-flag reads additionally require <c>AdminFeatureFlags</c>, the
/// same permission the admin flags surface itself uses.
/// </para>
/// </summary>
public static class CompanionAdminDashboardEndpoints
{
    /// <summary>
    /// Feature codes whose <see cref="AiUsageRecord"/> rows belong to the
    /// companion surface. Kept as one list so a new companion call site is added
    /// in exactly one place.
    /// </summary>
    private static readonly string[] CompanionFeatureCodes =
    [
        AiFeatureCodes.AiAssistantLearner,
        AiFeatureCodes.CompanionChat,
        AiFeatureCodes.CompanionRetrieval,
        AiFeatureCodes.CompanionAction,
    ];

    /// <summary>
    /// Error codes that mean "we refused to answer", as opposed to "the provider
    /// broke". Separated because the remedy is completely different: a refusal is
    /// a policy/entitlement/config condition an operator can change, a provider
    /// failure is not.
    /// </summary>
    private static readonly string[] RefusalErrorCodes =
    [
        "ungrounded",
        "feature_disabled",
        "kill_switch",
        "quota_exhausted",
        "quota_denied",
        "ai_credits_insufficient",
        "ai_credit_accounting_unavailable",
        "ai_feature_policy_refused",
        "provider_not_configured",
        "no_provider",
        "mock_provider_forbidden",
        "mock_provider_fallback_forbidden",
        "byok_key_required",
        "mock_assessment_forbidden",
        "mock_full_grade_retired",
        "provider_route_resolution_failed",
        "provider_registry_resolution_failed",
        "tool_loop_truncated",
        // Non-companion assistant surfaces only, but they are written under the
        // same three feature codes and are refusals by the same test.
        "opencode_role_not_allowed",
    ];

    public static IEndpointRouteBuilder MapCompanionAdminDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        MapQuality(app);
        MapContentGaps(app);
        MapTeachingGaps(app);
        return app;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // F-127 · AI quality dashboard
    // ═══════════════════════════════════════════════════════════════════════

    private static void MapQuality(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/companion/quality")
            .RequireAuthorization("AdminAiConfig")
            .RequireRateLimiting("PerUser");

        // ── Refusals, failures and citation coverage in one operator read ───
        group.MapGet("/summary", async (
            LearnerDbContext db,
            ICompanionFeatureFlags flags,
            TimeProvider clock,
            CancellationToken ct,
            int days = 30,
            int topErrors = 15) =>
        {
            var window = ClampDays(days);
            var since = clock.GetUtcNow().AddDays(-window);

            var calls = await db.AiUsageRecords
                .AsNoTracking()
                .Where(r => r.CreatedAt >= since && CompanionFeatureCodes.Contains(r.FeatureCode))
                .GroupBy(r => r.FeatureCode)
                .Select(g => new
                {
                    FeatureCode = g.Key,
                    Total = g.Count(),
                    Failures = g.Count(x => x.Outcome != AiCallOutcome.Success),
                    FailuresWithCode = g.Count(x => x.Outcome != AiCallOutcome.Success && x.ErrorCode != null),
                })
                .ToListAsync(ct);

            var errors = await db.AiUsageRecords
                .AsNoTracking()
                .Where(r => r.CreatedAt >= since
                    && CompanionFeatureCodes.Contains(r.FeatureCode)
                    && r.Outcome != AiCallOutcome.Success
                    && r.ErrorCode != null)
                .GroupBy(r => new { r.ErrorCode, r.Outcome })
                .Select(g => new
                {
                    ErrorCode = g.Key.ErrorCode!,
                    Outcome = g.Key.Outcome,
                    Count = g.Count(),
                    LastSeenAt = g.Max(x => x.CreatedAt),
                })
                .ToListAsync(ct);

            // ── Citation coverage ─────────────────────────────────────────
            // One companion answer = one assistant-role message in a non-staff
            // thread. "Cited nothing" is CitationsJson null OR the literal "[]":
            // the orchestrator writes null when there were no citations and the
            // citations array is serialised for the intermediate tool-call
            // messages too, so both spellings occur in real rows.
            var answers = await db.AiAssistantMessages
                .AsNoTracking()
                .Where(m => m.Role == "assistant"
                    && m.CreatedAt >= since
                    && db.AiAssistantThreads.Any(t => t.Id == m.ThreadId
                        && t.Role != ApplicationUserRoles.Admin
                        && t.Role != ApplicationUserRoles.Expert))
                .GroupBy(m => m.CitationsJson == null || m.CitationsJson == "[]")
                .Select(g => new { Uncited = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            // Message volume per thread role, so the companion denominator above
            // is visibly a subset of total assistant traffic rather than a
            // differently-scoped number that looks comparable but is not.
            var messagesByThreadRole = await (
                from m in db.AiAssistantMessages.AsNoTracking()
                join t in db.AiAssistantThreads.AsNoTracking() on m.ThreadId equals t.Id
                where m.Role == "assistant" && m.CreatedAt >= since
                group m by t.Role into g
                select new { Role = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            // Unresolved questions, by queue and by distinct learner. The learner
            // statement itself is returned verbatim by /content-gaps/handoffs —
            // it is text the learner deliberately filed for a human to read,
            // unlike chat content, which stays where it is.
            var handoffs = await db.CompanionHandoffs
                .AsNoTracking()
                .Where(h => h.CreatedAt >= since)
                .GroupBy(h => new { h.Status, h.Route })
                .Select(g => new
                {
                    Status = g.Key.Status,
                    Route = g.Key.Route,
                    Count = g.Count(),
                    DistinctLearners = g.Select(h => h.UserId).Distinct().Count(),
                    OldestAt = g.Min(h => h.CreatedAt),
                })
                .ToListAsync(ct);

            var unresolved = await db.CompanionHandoffs
                .AsNoTracking()
                .Where(h => h.Status != "resolved")
                .GroupBy(h => h.Route)
                .Select(g => new
                {
                    Route = g.Key,
                    Count = g.Count(),
                    OldestCreatedAt = g.Min(h => h.CreatedAt),
                })
                .ToListAsync(ct);

            var retrievalEnabled = await flags.IsRetrievalEnabledAsync(ct);
            var companionEnabled = await flags.IsEnabledAsync(ct);
            var actionsEnabled = await flags.AreActionsEnabledAsync(ct);

            var totalCalls = calls.Sum(c => c.Total);
            var totalFailures = calls.Sum(c => c.Failures);
            var citedAnswers = answers.FirstOrDefault(a => !a.Uncited)?.Count ?? 0;
            var uncitedAnswers = answers.FirstOrDefault(a => a.Uncited)?.Count ?? 0;
            var totalAnswers = citedAnswers + uncitedAnswers;

            var classified = errors
                .Select(e => new
                {
                    e.ErrorCode,
                    e.Outcome,
                    e.Count,
                    e.LastSeenAt,
                    Class = ClassifyError(e.ErrorCode, e.Outcome),
                })
                .OrderByDescending(e => e.Count)
                .ToList();

            return Results.Ok(new
            {
                generatedAt = clock.GetUtcNow(),
                windowDays = window,
                flags = new { companionEnabled, retrievalEnabled, actionsEnabled },
                calls = calls
                    .Select(c => new
                    {
                        featureCode = c.FeatureCode,
                        totalCalls = c.Total,
                        failures = c.Failures,
                        failureRatePct = c.Total == 0 ? 0m : Math.Round(c.Failures * 100m / c.Total, 2),
                    })
                    .OrderByDescending(c => c.failures)
                    .ThenByDescending(c => c.totalCalls)
                    .ToList(),
                totals = new
                {
                    calls = totalCalls,
                    failures = totalFailures,
                    failureRatePct = totalCalls == 0 ? 0m : Math.Round(totalFailures * 100m / totalCalls, 2),
                    failuresWithErrorCode = calls.Sum(c => c.FailuresWithCode),
                },
                errorClasses = classified
                    .GroupBy(e => e.Class)
                    .Select(g => new
                    {
                        @class = g.Key,
                        count = g.Sum(x => x.Count),
                        codes = g.OrderByDescending(x => x.Count)
                            .Select(x => new { errorCode = x.ErrorCode, outcome = x.Outcome.ToString(), count = x.Count, lastSeenAt = x.LastSeenAt })
                            .ToList(),
                    })
                    .OrderByDescending(g => g.count)
                    .ToList(),
                topErrors = classified.Take(Math.Clamp(topErrors, 1, 50))
                    .Select(e => new { errorCode = e.ErrorCode, outcome = e.Outcome.ToString(), count = e.Count, lastSeenAt = e.LastSeenAt, @class = e.Class })
                    .ToList(),
                citationCoverage = new
                {
                    companionAnswers = totalAnswers,
                    citedAnswers,
                    uncitedAnswers,
                    uncitedPct = totalAnswers == 0 ? 0m : Math.Round(uncitedAnswers * 100m / totalAnswers, 2),
                    retrievalEnabled,
                    messagesByThreadRole = messagesByThreadRole
                        .OrderByDescending(r => r.Count)
                        .Select(r => new { role = r.Role, count = r.Count })
                        .ToList(),
                },
                handoffs = new
                {
                    unresolvedTotal = unresolved.Sum(u => u.Count),
                    unresolved = unresolved
                        .Select(u => new { route = u.Route, count = u.Count, oldestCreatedAt = u.OldestCreatedAt })
                        .OrderByDescending(u => u.count)
                        .ToList(),
                    inWindow = handoffs
                        .Select(h => new { status = h.Status, route = h.Route, count = h.Count, distinctLearners = h.DistinctLearners, oldestAt = h.OldestAt })
                        .OrderByDescending(h => h.count)
                        .ToList(),
                },
                notInstrumented = new object[]
                {
                    new
                    {
                        signal = "answer_correctness",
                        detail = "No table stores a per-answer correctness or 'this was wrong' verdict for a companion turn, "
                            + "and no learner feedback (thumbs up/down, report-answer) is captured anywhere. A zero here would "
                            + "claim we reviewed every answer and found none wrong.",
                    },
                    new
                    {
                        signal = "hallucination_flag",
                        detail = "Grounding is enforced structurally (AiGatewayService refuses an ungrounded prompt, and the "
                            + "companion output screen blocks verbatim source reuse), but neither event is a persisted per-answer "
                            + "hallucination flag. The 'ungrounded' error class below counts pre-call refusals, which is a different "
                            + "thing and is labelled as such.",
                    },
                    new
                    {
                        signal = "withheld_output_count",
                        detail = "OUTPUT_WITHHELD is yielded as an in-process AssistantTurnError and never written to a column. The "
                            + "only durable trace is that the stored assistant message body was replaced by the "
                            + "'[Withheld by the content-protection check...' note, which is indistinguishable from that literal "
                            + "string being typed by a model. Counting it would be a guess, so it is not counted.",
                    },
                    new
                    {
                        signal = "answer_confidence",
                        detail = "No numeric confidence score is persisted for a companion answer. The only confidence value in the "
                            + "system belongs to Jev judgment calls (jev.*), which are not companion answers.",
                    },
                    new
                    {
                        signal = "source_conflict",
                        detail = "CompanionRetriever computes an authority-conflict flag per turn for the live prompt, but stores it "
                            + "nowhere, so conflicts currently cannot be counted or listed after the fact. Persisting it is a schema "
                            + "change and is deliberately out of scope for this read-only dashboard.",
                    },
                },
            });
        }).WithAdminRead("AdminFeatureFlags");

        // ── Which source keys were actually cited, and which never were ─────
        //
        // Indexed sources that answer nothing are the most actionable content-gap
        // signal the corpus can produce: the source is approved and retrievable,
        // yet no learner answer has ever drawn on it. "Never cited" is bounded by
        // the same window as every other number here, so a source that is merely
        // new is not libelled as unusable.
        group.MapGet("/citations", async (
            LearnerDbContext db,
            TimeProvider clock,
            CancellationToken ct,
            int days = 30,
            int take = 50) =>
        {
            var window = ClampDays(days);
            var since = clock.GetUtcNow().AddDays(-window);
            var limit = Math.Clamp(take, 1, 200);

            var raw = await db.AiAssistantMessages
                .AsNoTracking()
                .Where(m => m.Role == "assistant"
                    && m.CitationsJson != null
                    && m.CitationsJson != "[]"
                    && m.CreatedAt >= since)
                .Select(m => m.CitationsJson!)
                .ToListAsync(ct);

            var cited = new Dictionary<string, (int Count, string? Title)>(StringComparer.Ordinal);
            var unparsableRows = 0;
            foreach (var json in raw)
            {
                List<CompanionCitationSourceRef>? parsed;
                try
                {
                    parsed = System.Text.Json.JsonSerializer.Deserialize<List<CompanionCitationSourceRef>>(json, CitationJson);
                }
                catch (System.Text.Json.JsonException)
                {
                    unparsableRows += 1;
                    continue;
                }

                if (parsed is null) continue;
                foreach (var citation in parsed)
                {
                    if (string.IsNullOrWhiteSpace(citation.SourceKey)) continue;
                    var existing = cited.TryGetValue(citation.SourceKey, out var prior) ? prior : default;
                    cited[citation.SourceKey] = (existing.Count + 1, citation.SourceTitle ?? existing.Title);
                }
            }

            // Retrievable, non-superseded sources — the same filter the status
            // endpoint uses, so "never cited" is measured against sources a
            // learner could actually have received.
            var now = clock.GetUtcNow();
            var retrievable = await db.CompanionSources
                .AsNoTracking()
                .Where(s => s.State == CompanionSourceState.Approved)
                .Where(s => s.SupersededBySourceId == null)
                .Where(s => s.EffectiveFrom == null || s.EffectiveFrom <= now)
                .Where(s => s.EffectiveTo == null || s.EffectiveTo >= now)
                .Select(s => new { s.SourceKey, s.Title, s.ProfessionId, s.SubtestCode, s.IsProprietary })
                .ToListAsync(ct);

            var neverCited = retrievable
                .Where(s => !cited.ContainsKey(s.SourceKey))
                .OrderBy(s => s.ProfessionId)
                .ThenBy(s => s.SubtestCode)
                .ThenBy(s => s.SourceKey)
                .ToList();

            return Results.Ok(new
            {
                generatedAt = now,
                windowDays = window,
                citedSourceKeys = cited.Count,
                unparsableCitationRows = unparsableRows,
                topCited = cited
                    .OrderByDescending(kv => kv.Value.Count)
                    .Take(limit)
                    .Select(kv => new { sourceKey = kv.Key, sourceTitle = kv.Value.Title, citations = kv.Value.Count })
                    .ToList(),
                neverCitedCount = neverCited.Count,
                neverCited = neverCited.Take(limit).ToList(),
                note = "Counts are the citation entries stored on companion answers in this window. "
                    + "'Never cited' means no stored answer cited the source inside the window; a source outside the window "
                    + "or newly approved is not evidence that it cannot answer.",
            });
        }).WithAdminRead("AdminFeatureFlags");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // F-128 · Content-gap dashboard
    // ═══════════════════════════════════════════════════════════════════════

    private static void MapContentGaps(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/companion/content-gaps")
            .RequireAuthorization("AdminAiConfig")
            .RequireRateLimiting("PerUser");

        // ── Topics with no supporting source ────────────────────────────────
        //
        // Two halves, and they are deliberately kept separate because they are
        // different kinds of fact:
        //   (a) CORPUS COVERAGE  — which (profession, subtest) scopes hold no
        //       retrievable approved source at all. This is a measurement, not an
        //       inference: with zero approved sources in a scope, retrieval there
        //       provably cannot return evidence.
        //   (b) UNANSWERED TOPICS — which scopes actually produced companion
        //       answers that cited nothing. This is the observed symptom. It is
        //       named "cited nothing", never "no content exists", because an
        //       uncited answer can also come from the retrieval flag being off,
        //       from a query that matched nothing, or from prompt composition
        //       failing before retrieval ran — none of which are content gaps.
        //       The two halves are shown side by side so an operator can tell
        //       "no content" (a) from "content exists but was not reached" (b).
        group.MapGet("/coverage", async (
            LearnerDbContext db,
            ICompanionFeatureFlags flags,
            TimeProvider clock,
            CancellationToken ct,
            int days = 30,
            int take = 25) =>
        {
            var window = ClampDays(days);
            var since = clock.GetUtcNow().AddDays(-window);
            var limit = Math.Clamp(take, 1, 100);

            var now = clock.GetUtcNow();
            var sources = await db.CompanionSources
                .AsNoTracking()
                .GroupBy(s => new { s.ProfessionId, s.SubtestCode })
                .Select(g => new
                {
                    ProfessionId = g.Key.ProfessionId,
                    SubtestCode = g.Key.SubtestCode,
                    Total = g.Count(),
                    Approved = g.Count(x => x.State == CompanionSourceState.Approved),
                    PendingApproval = g.Count(x => x.State == CompanionSourceState.PendingApproval),
                    Draft = g.Count(x => x.State == CompanionSourceState.Draft),
                    Superseded = g.Count(x => x.State == CompanionSourceState.Superseded),
                    Retrievable = g.Count(x => x.State == CompanionSourceState.Approved
                        && x.SupersededBySourceId == null
                        && (x.EffectiveFrom == null || x.EffectiveFrom <= now)
                        && (x.EffectiveTo == null || x.EffectiveTo >= now)),
                })
                .ToListAsync(ct);

            // Answers that cited nothing, attributed to the learner's ACTIVE
            // profession. Active-profession is a current value, not the value at
            // answer time (that is not recorded), so this is described as an
            // attribution rather than a historical fact. A learner with no
            // profession on file lands in the "unknown" bucket instead of being
            // dropped, which keeps the denominator honest.
            var uncitedRaw = await (
                from m in db.AiAssistantMessages.AsNoTracking()
                join t in db.AiAssistantThreads.AsNoTracking() on m.ThreadId equals t.Id
                join u in db.Users.AsNoTracking() on t.UserId equals u.Id into users
                from u in users.DefaultIfEmpty()
                where m.Role == "assistant"
                    && m.CreatedAt >= since
                    && (m.CitationsJson == null || m.CitationsJson == "[]")
                    && t.Role != ApplicationUserRoles.Admin
                    && t.Role != ApplicationUserRoles.Expert
                select new { ProfessionId = u != null ? u.ActiveProfessionId : null, UserId = t.UserId })
                .ToListAsync(ct);

            var uncitedByProfession = uncitedRaw
                .GroupBy(r => string.IsNullOrWhiteSpace(r.ProfessionId) ? "unknown" : r.ProfessionId!)
                .Select(g => new
                {
                    professionId = g.Key,
                    uncitedAnswers = g.Count(),
                    distinctLearners = g.Select(r => r.UserId).Distinct().Count(),
                })
                .OrderByDescending(r => r.uncitedAnswers)
                .ToList();

            // Denominator per profession: every companion answer, cited or not,
            // so "12 uncited" can be read against the traffic it came from.
            var answersByProfession = await (
                from m in db.AiAssistantMessages.AsNoTracking()
                join t in db.AiAssistantThreads.AsNoTracking() on m.ThreadId equals t.Id
                join u in db.Users.AsNoTracking() on t.UserId equals u.Id into users
                from u in users.DefaultIfEmpty()
                where m.Role == "assistant"
                    && m.CreatedAt >= since
                    && t.Role != ApplicationUserRoles.Admin
                    && t.Role != ApplicationUserRoles.Expert
                select new { ProfessionId = u != null ? u.ActiveProfessionId : null })
                .ToListAsync(ct);

            var answersPerProfession = answersByProfession
                .GroupBy(r => string.IsNullOrWhiteSpace(r.ProfessionId) ? "unknown" : r.ProfessionId!)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

            var retrievalEnabled = await flags.IsRetrievalEnabledAsync(ct);

            return Results.Ok(new
            {
                generatedAt = now,
                windowDays = window,
                retrievalEnabled,
                corpusCoverage = sources
                    .OrderBy(s => s.ProfessionId)
                    .ThenBy(s => s.SubtestCode)
                    .Select(s => new
                    {
                        professionId = s.ProfessionId,
                        subtestCode = s.SubtestCode,
                        totalSources = s.Total,
                        approved = s.Approved,
                        retrievable = s.Retrievable,
                        pendingApproval = s.PendingApproval,
                        draft = s.Draft,
                        superseded = s.Superseded,
                        // The measurement that makes this a gap rather than a
                        // preference: nothing can be retrieved in this scope.
                        noRetrievableSource = s.Retrievable == 0,
                    })
                    .ToList(),
                scopesWithoutRetrievableSource = sources.Count(s => s.Retrievable == 0),
                uncitedTopics = uncitedByProfession
                    .Take(limit)
                    .Select(t => new
                    {
                        t.professionId,
                        t.uncitedAnswers,
                        t.distinctLearners,
                        totalAnswers = answersPerProfession.TryGetValue(t.professionId, out var total) ? total : t.uncitedAnswers,
                    })
                    .ToList(),
                notInstrumented = new object[]
                {
                    new
                    {
                        signal = "retrieval_miss_by_subtest",
                        detail = "Retrieval runs inside the turn and records only its embedding call "
                            + "(companion.retrieval.v1). The retrieved chunk ids, the miss reason and the subtest are not "
                            + "persisted, so an uncited answer cannot be attributed to a subtest — only to the learner's "
                            + "profession. Subtest-level clustering needs a per-turn retrieval trace column.",
                    },
                    new
                    {
                        signal = "corpus_scope_vs_demand",
                        detail = "Companion sources carry (profession, subtest) scope, but a companion question carries no topic "
                            + "classification at all, so supply cannot be matched to demand by topic. The two halves of this "
                            + "payload are therefore reported side by side rather than joined.",
                    },
                    new
                    {
                        signal = "handoff_topic_clustering",
                        detail = "Learner handoff issues are free text with no topic taxonomy or classifier. They are listed "
                            + "verbatim with counts instead of being auto-bucketed into invented topic names.",
                    },
                },
            });
        }).WithAdminRead("AdminFeatureFlags");

        // ── What learners asked that Sami could not answer ──────────────────
        //
        // The closest thing to a real topic list that exists: text the LEARNER
        // wrote for a human to read. Chat message content is deliberately NOT
        // mined here (and no message body is returned at all) — a learner does not
        // expect their practice conversation to become an operator report. This is
        // also the only companion surface here that carries anything per-learner:
        // the issue text, which was filed expressly for a tutor to read, and the
        // handoff id it is triaged by.
        group.MapGet("/handoffs", async (
            LearnerDbContext db,
            TimeProvider clock,
            CancellationToken ct,
            int days = 90,
            int take = 100) =>
        {
            var window = ClampDays(days);
            var limit = Math.Clamp(take, 1, 500);
            var since = clock.GetUtcNow().AddDays(-window);

            var rows = await db.CompanionHandoffs
                .AsNoTracking()
                .Where(h => h.CreatedAt >= since && h.Status != "resolved")
                .OrderBy(h => h.CreatedAt)
                .Take(limit)
                .Select(h => new
                {
                    h.Id,
                    h.CreatedAt,
                    h.Route,
                    h.Status,
                    h.Issue,
                })
                .ToListAsync(ct);

            // Repeated issue text is the one honest clustering available: the same
            // sentence filed more than once is the same unresolved topic, and the
            // text is the learner's own. It is grouped case-insensitively and
            // reported verbatim — no topic label is invented for it.
            var duplicateIssueTexts = rows
                .GroupBy(r => r.Issue.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => new { issue = g.First().Issue.Trim(), count = g.Count() })
                .OrderByDescending(g => g.count)
                .ThenBy(g => g.issue, StringComparer.OrdinalIgnoreCase)
                .Take(25)
                .ToList();

            var byRoute = rows
                .GroupBy(r => r.Route)
                .Select(g => new { route = g.Key, count = g.Count(), oldestCreatedAt = g.Min(r => r.CreatedAt) })
                .OrderByDescending(g => g.count)
                .ToList();

            return Results.Ok(new
            {
                generatedAt = clock.GetUtcNow(),
                windowDays = window,
                unresolvedCount = rows.Count,
                byRoute,
                rows = rows.Select(r => new
                {
                    id = r.Id,
                    createdAt = r.CreatedAt,
                    ageDays = Math.Round((clock.GetUtcNow() - r.CreatedAt).TotalDays, 1),
                    route = r.Route,
                    status = r.Status,
                    issue = r.Issue,
                }),
                repeatedIssues = duplicateIssueTexts,
            });
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // F-129 · Teaching-gap dashboard
    // ═══════════════════════════════════════════════════════════════════════

    private static void MapTeachingGaps(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/companion/teaching-gaps")
            .RequireAuthorization("AdminAiConfig")
            .RequireRateLimiting("PerUser");

        // ── What learners repeatedly get wrong ─────────────────────────────
        //
        // ErrorDnaEntry is the only real pedagogical signal in the system: every
        // row was created from observed evidence (a published writing finding, a
        // missed listening word, a wrong reading answer) and never from a guess.
        //
        // UserId is read to COUNT distinct learners and is then dropped: no user
        // id, and no per-learner row, appears anywhere in the response. That is
        // what lets a tutor read this without reading any individual learner's
        // record. The pattern text is the runtime's own stored weakness pattern,
        // not authored here.
        //
        // Rows are grouped by (PatternKey, Category, Subtest) rather than by
        // Pattern alone: PatternKey is the runtime's own SHA-256 of
        // (category + lowercased pattern), so it is the identity the recording
        // path itself upserts on, and grouping on it cannot merge two different
        // categories that happen to share a pattern string.
        group.MapGet("/recurring", async (
            LearnerDbContext db,
            TimeProvider clock,
            CancellationToken ct,
            int take = 50,
            int staleDays = 30,
            string? subtest = null,
            string? sourceKind = null) =>
        {
            var limit = Math.Clamp(take, 1, 200);
            var quietDays = Math.Clamp(staleDays, 1, 365);
            var quietSince = clock.GetUtcNow().AddDays(-quietDays);

            var query = db.ErrorDnaEntries.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(subtest))
            {
                var value = subtest.Trim().ToLowerInvariant();
                query = query.Where(e => e.Subtest == value);
            }
            if (!string.IsNullOrWhiteSpace(sourceKind))
            {
                var value = sourceKind.Trim();
                query = query.Where(e => e.SourceKind == value);
            }

            var rows = await query
                .Select(e => new
                {
                    e.PatternKey,
                    e.Category,
                    e.Subtest,
                    e.Pattern,
                    e.EvidenceCount,
                    e.MasteryScore,
                    e.ReviewCount,
                    e.NextReviewAt,
                    e.LastSeenAt,
                    e.SourceKind,
                    e.UserId,
                })
                .ToListAsync(ct);

            var patterns = new List<CompanionTeachingGapPattern>();
            foreach (var patternGroup in rows.GroupBy(r => new { r.PatternKey, r.Category, r.Subtest }))
            {
                var members = patternGroup.ToList();
                var learners = members.Select(m => m.UserId).Distinct().Count();
                var bySource = members
                    .GroupBy(m => m.SourceKind ?? "unknown")
                    .Select(g => new CompanionTeachingGapSource(g.Key, g.Count()))
                    .OrderByDescending(g => g.Count)
                    .ToList();

                patterns.Add(new CompanionTeachingGapPattern(
                    patternGroup.Key.PatternKey,
                    patternGroup.Key.Category,
                    patternGroup.Key.Subtest,
                    members.OrderByDescending(m => m.EvidenceCount).First().Pattern,
                    learners,
                    members.Sum(m => m.EvidenceCount),
                    (int)Math.Round(members.Average(m => (double)m.MasteryScore)),
                    members.Min(m => m.MasteryScore),
                    members.Max(m => m.MasteryScore),
                    members.Count(m => m.MasteryScore < 30),
                    members.Sum(m => m.ReviewCount),
                    members.Min(m => m.FirstSeenAt),
                    members.Max(m => m.LastSeenAt),
                    members.Count(m => m.LastSeenAt >= quietSince),
                    bySource));
            }

            var ordered = patterns
                .OrderByDescending(p => p.DistinctLearners)
                .ThenByDescending(p => p.TotalEvidence)
                .ThenByDescending(p => p.LastSeenAt)
                .ToList();

            var byCategory = patterns
                .GroupBy(p => p.Category)
                .Select(g => new CompanionTeachingGapCategory(
                    g.Key,
                    g.Count(),
                    g.Sum(p => p.DistinctLearners),
                    g.Sum(p => p.TotalEvidence),
                    (int)Math.Round(g.Average(p => (double)p.AverageMasteryScore)),
                    g.Count(p => p.UnaddressedLearnerRows > 0),
                    g.Max(p => p.LastSeenAt)))
                .OrderByDescending(g => g.TotalEvidence)
                .ToList();

            var bySubtest = patterns
                .GroupBy(p => p.Subtest)
                .Select(g => new CompanionTeachingGapSubtest(
                    g.Key,
                    g.Count(),
                    g.Sum(p => p.DistinctLearners),
                    g.Sum(p => p.TotalEvidence),
                    (int)Math.Round(g.Average(p => (double)p.AverageMasteryScore))))
                .OrderByDescending(g => g.TotalEvidence)
                .ToList();

            // Mastery buckets over every stored row, not the top-N slice, so the
            // distribution is the corpus rather than the leaderboard.
            var buckets = new[] { "0-29", "30-59", "60-79", "80-100" };
            var masteryDistribution = buckets
                .Select(b => new
                {
                    bucket = b,
                    count = b switch
                    {
                        "0-29" => rows.Count(r => r.MasteryScore < 30),
                        "30-59" => rows.Count(r => r.MasteryScore is >= 30 and < 60),
                        "60-79" => rows.Count(r => r.MasteryScore is >= 60 and < 80),
                        _ => rows.Count(r => r.MasteryScore >= 80),
                    },
                })
                .ToList();

            var sourceKinds = rows
                .GroupBy(r => r.SourceKind ?? "unknown")
                .Select(g => new { sourceKind = g.Key, rows = g.Count() })
                .OrderByDescending(g => g.rows)
                .ToList();

            // A quiet filter that matches nothing is a real answer ("no such
            // evidence"), but an unknown sourceKind is usually a typo, so it is
            // reported rather than silently returning an empty table.
            var appliesFilters = !string.IsNullOrWhiteSpace(subtest) || !string.IsNullOrWhiteSpace(sourceKind);
            var knownSourceKinds = new[] { "writing_grade", "listening_answer", "reading_answer", "speaking_assess", "tutor_note" };

            return Results.Ok(new
            {
                generatedAt = clock.GetUtcNow(),
                staleDays = quietDays,
                filters = new { subtest, sourceKind },
                totalRows = rows.Count,
                distinctLearners = rows.Select(r => r.UserId).Distinct().Count(),
                distinctPatterns = patterns.Count,
                patterns = ordered.Take(limit).ToList(),
                byCategory,
                bySubtest,
                masteryDistribution,
                evidenceSources = sourceKinds,
                diagnostics = new
                {
                    noRowsMatchFilters = appliesFilters && rows.Count == 0,
                    unknownSourceKindFilter = !string.IsNullOrWhiteSpace(sourceKind)
                        && !knownSourceKinds.Contains(sourceKind.Trim(), StringComparer.OrdinalIgnoreCase)
                            ? sourceKind.Trim()
                            : null,
                    knownSourceKinds,
                },
                notInstrumented = new object[]
                {
                    new
                    {
                        signal = "non_writing_error_dna",
                        detail = "The only feeder writing ErrorDnaEntry rows today is WritingErrorDnaFeeder (SourceKind "
                            + "'writing_grade', Subtest 'writing'). No equivalent feeder exists for listening, reading or "
                            + "speaking, so their absence below is an absence of instrumentation, not evidence that learners "
                            + "make no mistakes in those subtests. The bySubtest table states which subtests actually have rows.",
                    },
                    new
                    {
                        signal = "learner_cohort_attribution",
                        detail = "ErrorDnaEntry carries no cohort, plan or exam-date column, and joining it to the learner for "
                            + "those attributes would turn an aggregate pedagogical view into per-learner reporting. Cohort "
                            + "slicing is therefore not offered here.",
                    },
                },
            });
        });

        // ── The taxonomy this report is built from ──────────────────────────
        // The form controls offer exactly the values that occur in the data,
        // instead of a hard-coded list that drifts away from what is stored.
        group.MapGet("/dimensions", async (LearnerDbContext db, CancellationToken ct) =>
        {
            var subtests = await db.ErrorDnaEntries.AsNoTracking()
                .GroupBy(e => e.Subtest)
                .Select(g => new { subtest = g.Key, rows = g.Count(), distinctLearners = g.Select(e => e.UserId).Distinct().Count() })
                .OrderByDescending(g => g.rows)
                .ToListAsync(ct);

            var categories = await db.ErrorDnaEntries.AsNoTracking()
                .GroupBy(e => e.Category)
                .Select(g => new { category = g.Key, rows = g.Count(), distinctLearners = g.Select(e => e.UserId).Distinct().Count() })
                .OrderByDescending(g => g.rows)
                .ToListAsync(ct);

            var sourceKinds = await db.ErrorDnaEntries.AsNoTracking()
                .GroupBy(e => e.SourceKind)
                .Select(g => new { sourceKind = g.Key, rows = g.Count() })
                .OrderByDescending(g => g.rows)
                .ToListAsync(ct);

            return Results.Ok(new { subtests, categories, sourceKinds });
        }).WithAdminRead("AdminFeatureFlags");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Shared helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 1..365 days. A zero or negative window would silently return an empty
    /// report that reads like "nothing happened", so it is clamped rather than
    /// accepted.
    /// </summary>
    private static int ClampDays(int days) => Math.Clamp(days, 1, 365);

    /// <summary>
    /// Refusal / provider-failure / other. Prefixes are used where the gateway
    /// derives a family (every <c>provider_*</c> code, the <c>global_budget*</c>
    /// deny reasons), exact codes where the vocabulary is closed. Anything
    /// unmatched is returned as <c>other</c> on purpose: a new error code must
    /// show up as unclassified rather than be absorbed into a class nobody chose.
    /// </summary>
    private static string ClassifyError(string? errorCode, AiCallOutcome outcome)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            return outcome == AiCallOutcome.Success ? "success" : "unclassified_no_code";
        }

        if (RefusalErrorCodes.Contains(errorCode, StringComparer.Ordinal)) return "refusal";
        if (errorCode.StartsWith("global_budget", StringComparison.Ordinal)) return "refusal";
        if (errorCode.StartsWith("provider_", StringComparison.Ordinal))
        {
            // "provider_not_configured" / "provider_route_resolution_failed" are
            // platform configuration refusals, matched above; everything else
            // with this prefix came back from a provider.
            return "provider_failure";
        }

        return outcome switch
        {
            AiCallOutcome.ProviderError => "provider_failure",
            AiCallOutcome.GatewayRefused => "refusal",
            AiCallOutcome.Timeout => "timeout_or_cancel",
            AiCallOutcome.Cancelled => "timeout_or_cancel",
            AiCallOutcome.PlatformError => "platform_error",
            _ => "other",
        };
    }

    /// <summary>
    /// The stored <c>CitationsJson</c> is written by
    /// <c>AiAssistantOrchestrator</c> with a bare <c>JsonSerializer.Serialize</c>
    /// call while every HTTP response goes out through <see cref="Services.JsonSupport"/>
    /// (web defaults, camelCase). Rather than depend on which of the two wrote a
    /// given row, reads are case-insensitive: a casing change must never silently
    /// turn every citation count into zero.
    /// </summary>
    private static readonly System.Text.Json.JsonSerializerOptions CitationJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>One stored citation entry. Only the fields this report counts.</summary>
    private sealed record CompanionCitationSourceRef(
        string? SourceKey,
        string? SourceTitle);

    private sealed record CompanionTeachingGapSource(string SourceKind, int Rows);

    private sealed record CompanionTeachingGapPattern(
        string PatternKey,
        string Category,
        string Subtest,
        string Pattern,
        int DistinctLearners,
        int TotalEvidence,
        int AverageMasteryScore,
        int MinMasteryScore,
        int MaxMasteryScore,
        int UnaddressedLearnerRows,
        int TotalReviews,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset LastSeenAt,
        int LearnersSeenRecently,
        IReadOnlyList<CompanionTeachingGapSource> Sources);

    private sealed record CompanionTeachingGapCategory(
        string Category,
        int DistinctPatterns,
        int DistinctLearners,
        int TotalEvidence,
        int AverageMasteryScore,
        int PatternsWithUnaddressedRows,
        DateTimeOffset LastSeenAt);

    private sealed record CompanionTeachingGapSubtest(
        string Subtest,
        int DistinctPatterns,
        int DistinctLearners,
        int TotalEvidence,
        int AverageMasteryScore);
}
