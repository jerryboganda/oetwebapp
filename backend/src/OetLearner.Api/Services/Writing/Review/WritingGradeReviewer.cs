using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Ai.TypeSafe;
using OetLearner.Api.Services.Ai.Review;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Writing.Review;

/// <summary>
/// The secondary reviewer of a primary Writing grade (owner handoff 6 Oct 2026). The grading pipeline calls it between
/// the primary grade (plus Jev) and everything that makes a result visible.
/// </summary>
public interface IWritingGradeReviewer
{
    /// <summary>Folded into the grade reuse key and the stage fingerprint.</summary>
    string Version { get; }

    /// <summary>Off, Shadow or Enforce. Read uncached on every run; unreadable means Off.</summary>
    Task<WritingReviewMode> GetModeAsync(CancellationToken ct);

    /// <summary>
    /// Reviews one primary grade. Enforce: returns <see cref="WritingReviewStatus.Reviewed"/> or
    /// <see cref="WritingReviewStatus.Skipped"/> (admin kill list), or THROWS the retryable
    /// <c>writing_review_unavailable</c> so the letter is held and re-queued (never published unreviewed).
    /// Shadow: never throws, never changes a result.
    /// </summary>
    Task<WritingReviewOutcome> ReviewAsync(WritingReviewRequest request, CancellationToken ct);
}

/// <summary>
/// Reviewer on the existing <c>writing-codex-sub</c> route (GPT-6.1 Sol), pinned in the request and sent through
/// <see cref="IAiGatewayService"/> with a grounded prompt, so every physical call is one AiOperation and one
/// AiUsageRecord under <see cref="AiFeatureCodes.WritingGradeReview"/>. Never Max (the reviewer is not a grade hop
/// and <see cref="WritingSubscriptionSelector"/> is not consulted) and never a credit debit. Every call goes through the
/// SHARED reviewer pipeline (<see cref="OetLearner.Api.Services.Ai.Review.SharedReviewerRunner"/>), so Writing and Speaking
/// share one bounded Codex capacity gate and one automatic API fallback (the repo's existing <c>anthropic</c> row, the same
/// one the grade chain uses as L2): a reviewer outage can no longer park a letter for hours waiting on a Codex quota reset.
/// If BOTH routes fail, Enforce still holds the letter (bounded, see <see cref="WritingGradeChainOptions"/>).
///
/// Proposals only: <see cref="WritingReviewApplier"/> decides. Each pass is persisted through the request, so a held
/// letter resumes without a second provider call; the passes are stateless against the PRIMARY values, so the final
/// outcome is the last pass applied once.
/// </summary>
public sealed class WritingGradeReviewer(
    LearnerDbContext db,
    IAiGatewayService gateway,
    IOptions<WritingGradeChainOptions> chainOptions,
    IOptions<WritingReviewOptions> reviewOptions,
    TimeProvider clock,
    ILogger<WritingGradeReviewer> logger,
    // AI Pipeline Control Center: owner-saved order and switches of the reviewer stage. Optional LAST parameter.
    OetLearner.Api.Services.AiPipeline.IAiPipelineStore? pipelineStore = null,
    // Subscription-account rotation (owner directive 2026-10-10): permutes the Codex accounts of the
    // reviewer plan per run by remaining quota. Optional LAST parameters, absent = the saved order runs as saved.
    OetLearner.Api.Services.AiPipeline.ISubscriptionAccountPool? accountPool = null,
    OetLearner.Api.Services.AiPipeline.ISubscriptionAccountStateProvider? accountState = null) : IWritingGradeReviewer
{
    /// <summary>FeatureFlags row, Enabled = false turns the reviewer Off (absent row = on while the Codex row is active).</summary>
    public const string ReviewerFlagKey = "writing_ai_reviewer";

    /// <summary>FeatureFlags row, Enabled = true runs the reviewer in Shadow mode (records proposals, changes nothing).</summary>
    public const string ShadowFlagKey = "writing_ai_reviewer_shadow";

    private static int _inactiveWarned;

    private readonly WritingGradeChainOptions _chain = chainOptions.Value;
    private readonly WritingReviewOptions _policy = reviewOptions.Value;

    public string Version => WritingReviewPrompt.Version;

    public async Task<WritingReviewMode> GetModeAsync(CancellationToken ct)
    {
        try
        {
            var flags = await db.FeatureFlags.AsNoTracking()
                .Where(f => f.Key == ShadowFlagKey || f.Key == ReviewerFlagKey)
                .Select(f => new { f.Key, f.Enabled, f.UpdatedAt })
                .ToListAsync(ct);

            // Newest row wins per key, read uncached.
            var shadow = flags.Where(f => f.Key == ShadowFlagKey).OrderByDescending(f => f.UpdatedAt).FirstOrDefault();
            if (shadow is { Enabled: true }) return WritingReviewMode.Shadow;

            var enforce = flags.Where(f => f.Key == ReviewerFlagKey).OrderByDescending(f => f.UpdatedAt).FirstOrDefault();
            if (enforce is { Enabled: false }) return WritingReviewMode.Off;

            if (pipelineStore is not null)
            {
                // The owner-saved stage wins over the legacy provider-row check: Off when the stage is switched off
                // or no step is usable, otherwise Enforce.
                var plan = await pipelineStore.ResolvePlanAsync(OetLearner.Api.Services.AiPipeline.AiPipelineStageKeys.WritingReview, ct);
                return plan.StageEnabled && plan.Hops.Count > 0 ? WritingReviewMode.Enforce : WritingReviewMode.Off;
            }

            var routeActive = await db.AiProviders.AsNoTracking()
                .AnyAsync(p => p.Code == WritingSubscriptionProviders.Codex && p.IsActive, ct);
            if (routeActive) return WritingReviewMode.Enforce;

            // Not enabled (the Codex row is missing or inactive): grading simply runs unreviewed, never held.
            if (Interlocked.Exchange(ref _inactiveWarned, 1) == 0)
            {
                logger.LogWarning(
                    "Writing secondary reviewer is off: the {Provider} provider row is missing or inactive.",
                    WritingSubscriptionProviders.Codex);
            }

            return WritingReviewMode.Off;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Type only: never the message, which can carry connection details.
            logger.LogWarning("Writing reviewer mode could not be read ({ErrorType}); treating it as off.", ex.GetType().Name);
            return WritingReviewMode.Off;
        }
    }

    public async Task<WritingReviewOutcome> ReviewAsync(WritingReviewRequest request, CancellationToken ct)
    {
        var shadow = request.Mode == WritingReviewMode.Shadow;
        try
        {
            return await RunAsync(request, shadow, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiException) when (!shadow)
        {
            throw;
        }
        catch (AiQuotaDeniedException ex) when (!shadow && ex.ErrorCode == "feature_disabled")
        {
            // The admin per-feature kill list is the emergency lever: the primary result stands.
            logger.LogWarning(
                "Writing review skipped for submission {SubmissionId}: {FeatureCode} is disabled by an administrator.",
                request.SubmissionId, AiFeatureCodes.WritingGradeReview);
            return Skipped(request, "feature_disabled");
        }
        catch (AiFeaturePolicyRefusedException ex) when (!shadow && ex.Reason == "policy_disabled")
        {
            logger.LogWarning(
                "Writing review skipped for submission {SubmissionId}: the feature policy is disabled.",
                request.SubmissionId);
            return Skipped(request, "policy_disabled");
        }
        catch (Exception ex) when (shadow)
        {
            // Shadow never changes a result and never holds one.
            logger.LogWarning(
                "Writing shadow review failed for submission {SubmissionId} ({ErrorType}); the result is unaffected.",
                request.SubmissionId, ex.GetType().Name);
            return Skipped(request, "shadow_failed");
        }
        catch (Exception ex)
        {
            // Enforce: an outage never publishes an unreviewed result. The primary result and the credit hold stay
            // on the letter, so every re-queue resumes this review for free.
            logger.LogWarning(
                "Writing review unavailable for submission {SubmissionId} ({ErrorType}); holding the letter.",
                request.SubmissionId, ex.GetType().Name);
            throw WritingReviewHold.Unavailable();
        }
    }

    private async Task<WritingReviewOutcome> RunAsync(WritingReviewRequest request, bool shadow, CancellationToken ct)
    {
        // A primary at or above the guardrail is verified with the enhanced prompt in ONE call; a lower primary that the
        // reviewer lifts to the guardrail gets its enhanced verification as a second call.
        var enhancedFirst = OetScoring.OetReportedScaledScore(request.Primary.ScaledScore) >= _policy.EnhancedThreshold;
        var fingerprint = FingerprintOf(request, enhancedFirst);

        var passes = new List<WritingReviewPassRecord>();
        if (!shadow
            && request.Persisted is { } stored
            && stored.Version == Version
            && stored.InputFingerprint == fingerprint)
        {
            passes.AddRange(stored.Passes);
        }

        // At most MaxEnhancedRounds enhanced passes in total (the first pass counts when it is enhanced).
        // ponytail: a Shadow run is ONE pass (it runs inline, so every extra pass delays the letter); a lifted
        // 400+ score is therefore not enhanced-verified in Shadow. Enforce runs the full rounds.
        var lastPass = shadow ? 0 : Math.Max(0, enhancedFirst ? _policy.MaxEnhancedRounds - 1 : _policy.MaxEnhancedRounds);
        var knownIds = request.Findings.Select(f => f.Id).ToArray();
        var issues = new List<string>();
        WritingReviewOutcome? outcome = null;
        var unresolved = false;

        for (var pass = 0; pass <= lastPass; pass++)
        {
            var enhanced = pass == 0 ? enhancedFirst : true;
            WritingReviewDecision decision;
            if (pass < passes.Count)
            {
                // Resume: re-apply the stored reply, no provider call.
                if (!WritingReviewDecisionParser.TryParse(passes[pass].Json, knownIds, out decision))
                {
                    throw new InvalidOperationException("A stored review pass is unreadable.");
                }
            }
            else
            {
                var called = await CallPassAsync(request, enhanced, issues, pass, knownIds, ct);
                decision = called.Decision;
                passes.Add(called.Record);
                if (!shadow)
                {
                    await request.PersistStageAsync(new WritingReviewStageRecord(Version, fingerprint, passes.ToArray()), ct);
                }
            }

            outcome = WritingReviewApplier.Apply(request, decision, _policy, enhanced);
            if (OetScoring.OetReportedScaledScore(outcome.Scores.ScaledScore) < _policy.EnhancedThreshold) break;
            if (enhanced && outcome.EnhancedFailures.Count == 0) break;
            if (pass >= lastPass)
            {
                unresolved = outcome.EnhancedFailures.Count > 0;
                break;
            }

            // Next pass: enhanced verification of a lifted score, or a corrective round naming what still fails.
            issues = outcome.EnhancedFailures.ToList();
        }

        var final = outcome ?? throw new InvalidOperationException("The review ran no pass.");
        var notes = final.Notes;
        notes.Passes = passes.Count;
        foreach (var record in passes)
        {
            if (!string.IsNullOrEmpty(record.UsageRecordId)) notes.UsageRecordIds.Add(record.UsageRecordId);
        }

        if (passes.Count > 0)
        {
            notes.Provider = passes[^1].Provider;
            notes.Model = passes[^1].Model;
        }

        if (passes.Any(p => string.Equals(p.Provider, WritingSubscriptionProviders.ClaudeApi, StringComparison.OrdinalIgnoreCase)))
        {
            // The shared reviewer gate/fallback moved this review off Codex onto the API route.
            notes.Flags.Add("codex_api_fallback");
        }

        var tutorReasons = final.TutorReasons.ToList();
        if (unresolved)
        {
            // Never clipped: the reviewer's recalibrated values are published and a human is asked to look.
            notes.EnhancedUnresolved = true;
            notes.Flags.Add("enhanced_unresolved");
            if (!tutorReasons.Contains(WritingJevReviewReasons.ReviewerUnresolved))
            {
                tutorReasons.Add(WritingJevReviewReasons.ReviewerUnresolved);
            }
        }

        notes.Status = shadow ? "shadowed" : "reviewed";
        return final with
        {
            Status = shadow ? WritingReviewStatus.Shadowed : WritingReviewStatus.Reviewed,
            TutorReasons = tutorReasons,
        };
    }

    private async Task<(WritingReviewDecision Decision, WritingReviewPassRecord Record)> CallPassAsync(
        WritingReviewRequest request,
        bool enhanced,
        IReadOnlyList<string> issues,
        int pass,
        IReadOnlyCollection<string> knownIds,
        CancellationToken ct)
    {
        // The stage budget is what is left of the claim's lease minus a margin, so the cron never reclaims a live review.
        var remaining = StageBudget(request);
        if (remaining < WritingGradeChain.MinimumAttemptBudget)
        {
            logger.LogWarning(
                "Writing review for submission {SubmissionId} has no time left in its lease; holding the letter.",
                request.SubmissionId);
            throw WritingReviewHold.Unavailable();
        }

        if (!RulebookProfessionParser.TryParse(request.Profession, out var profession))
        {
            logger.LogWarning(
                "Writing review for submission {SubmissionId}: profession {Profession} is not supported.",
                request.SubmissionId, request.Profession);
            throw WritingReviewHold.Unavailable();
        }

        AiGroundedPrompt prompt;
        try
        {
            prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Writing,
                LetterType = WritingLetterTypeTaxonomy.ToLegacyLetterType(request.LetterType),
                Profession = profession,
                Task = AiTaskMode.ReviewWriting,
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Writing review prompt could not be built for submission {SubmissionId} ({ErrorType}).",
                request.SubmissionId, ex.GetType().Name);
            throw WritingReviewHold.Unavailable();
        }

        var template = new AiGatewayRequest
        {
            Prompt = prompt,
            UserInput = WritingReviewPrompt.Build(request, _policy, enhanced, issues),
            // The route is pinned here; WritingGradeChain.RunReviewAsync restates it per attempt.
            Provider = WritingSubscriptionProviders.Codex,
            Model = WritingSubscriptionProviders.CodexModel,
            Temperature = 0.1,
            MaxTokens = 12000,
            FeatureCode = AiFeatureCodes.WritingGradeReview,
            PromptTemplateId = enhanced ? WritingReviewPrompt.EnhancedTemplateId : WritingReviewPrompt.TemplateId,
            UserId = request.UserId,
            AssessmentContext = string.Equals(request.SubmissionMode, "mock", StringComparison.OrdinalIgnoreCase)
                ? AiAssessmentContext.Mock
                : AiAssessmentContext.Practice,
            // Server-derived: this call exists only inside a grade the learner already holds. It keeps the
            // reviewer out of the plan feature list and the learner's token counters. No credit reservation and
            // no debit (the feature code is not in ShouldDebitAiCredit).
            FreeSampleGrant = true,
            ResourceId = request.SubmissionId.ToString("N"),
            ResourceType = "writing_submission_review",
        };

        var kind = pass == 0
            ? (enhanced ? "enhanced" : "review")
            : (issues.Count > 0 ? "corrective" : "enhanced");

        (WritingReviewDecision Decision, WritingReviewPassRecord Record) ParseResult(AiGatewayResult result)
        {
            // Parsing happens inside the attempt: an unreadable reply fails over like any provider failure.
            if (!WritingReviewDecisionParser.TryParse(result.Completion, knownIds, out var parsed))
            {
                throw new WritingRubricUnreadableException("Writing review returned an unreadable response.");
            }

            return (parsed, new WritingReviewPassRecord(
                kind,
                parsed.RawJson,
                result.ResolvedProvider,
                result.ResolvedModel,
                result.UsageRecordId,
                clock.GetUtcNow()));
        }

        var shared = SharedReviewerOptions.Current;
        // Owner-saved reviewer steps: which of the Codex accounts / the API step run, their models and
        // their order. Subscription-account rotation (owner directive 2026-10-10) permutes the Codex
        // accounts of this plan in memory, best-remaining-quota first, and writes nothing back.
        var reviewPlan = pipelineStore is null ? null : await pipelineStore.ResolvePlanAsync(OetLearner.Api.Services.AiPipeline.AiPipelineStageKeys.WritingReview, ct);
        if (reviewPlan is not null && accountPool is not null && accountState is not null)
        {
            reviewPlan = await accountPool.OrderAsync(accountState, reviewPlan, ct);
        }
        var codexHops = SharedReviewerRunner.SubscriptionCodexHops(reviewPlan);
        var apiHop = reviewPlan?.Hops.FirstOrDefault(h => OetLearner.Api.Services.AiPipeline.SubscriptionAccountGroups.GroupOf(h.Provider) is null);
        var codexOn = reviewPlan is null || codexHops.Count > 0;
        var apiOn = reviewPlan is null || apiHop is not null;
        var apiFirst = codexHops.Count > 0 && apiHop is not null && apiHop.Index < codexHops[0].Index;
        var codexModel = codexHops.Count > 0 && !string.IsNullOrWhiteSpace(codexHops[0].Model)
            ? codexHops[0].Model!
            : WritingSubscriptionProviders.CodexModel;
        var apiProvider = apiHop?.Provider ?? WritingSubscriptionProviders.ClaudeApi;
        var apiModel = string.IsNullOrWhiteSpace(apiHop?.Model) ? WritingSubscriptionProviders.ClaudeModel : apiHop!.Model;
        var (runResult, _) = await SharedReviewerRunner.RunAsync(
            shared,
            CodexReviewerGate.Default,
            "writing",
            request.SubmissionId.ToString("N"),
            async token =>
            {
                if (codexHops.Count == 0) throw new InvalidOperationException("The reviewer stage has no Codex account step.");
                Exception? lastHop = null;
                foreach (var hop in codexHops)
                {
                    try
                    {
                        return await WritingGradeChain.RunReviewAsync(
                            gateway,
                            template with { Model = string.IsNullOrWhiteSpace(hop.Model) ? codexModel : hop.Model },
                            request.GradeEpoch,
                            pass,
                            ParseResult,
                            _chain,
                            remaining,
                            clock,
                            logger,
                            token,
                            hop.Provider,
                            hop.Attempts,
                            hop.BudgetSeconds);
                    }
                    catch (Exception ex) when (WritingGradeChain.IsFailoverable(ex, token))
                    {
                        // One account out of quota (or down): the next Codex account runs inside this same
                        // phase, then the runner's API fallback takes over if every account failed.
                        lastHop = ex;
                        logger.LogWarning(
                            "Writing reviewer hop {Provider} failed ({ErrorType}); trying the next Codex account.",
                            hop.Provider, ex.GetType().Name);
                    }
                }

                throw lastHop ?? new InvalidOperationException("Every Codex account failed for this review pass.");
            },
            async token =>
            {
                // The review cap from the same provider row: never Max, never a new integration. The API fallback
                // uses the same prompt/parse/merge semantics, so the fallback is a real review, not a skip.
                var apiTemplate = template with
                {
                    Provider = apiProvider,
                    Model = apiModel,
                    ResourceVersion = WritingGradeChain.ReviewApiResourceVersion(request.GradeEpoch, pass),
                };
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Min(shared.ApiAttemptSeconds, (int)Math.Max(remaining.TotalSeconds, 1))));
                var apiResult = await gateway.CompleteAsync(apiTemplate, timeout.Token);
                return ParseResult(apiResult);
            },
            logger,
            ct,
            codexEnabled: codexOn,
            apiEnabled: apiOn,
            apiFirst: apiFirst);

        return runResult;
    }

    private TimeSpan StageBudget(WritingReviewRequest request)
    {
        var now = clock.GetUtcNow();
        var claimed = request.ClaimedAt ?? now;
        var lease = WritingGradeTimings.StaleClaimLease - TimeSpan.FromSeconds(_chain.ReviewLeaseMarginSeconds);
        return claimed + lease - now;
    }

    /// <summary>
    /// The bounded fallback of an Enforce reviewer outage (owner handoff 6 Oct 2026: a reviewer failure must never leave a letter
    /// Queued). The primary result stands, the letter is flagged for a tutor (rv_unresolved) and the admin notes say why. The
    /// pipeline stages the audit event; no provider call, no credit movement.
    /// </summary>
    internal static WritingReviewOutcome HoldExhausted(WritingReviewRequest request)
    {
        var notes = new WritingReviewAdminNotes
        {
            Version = WritingReviewPrompt.Version,
            Mode = request.Mode.ToString().ToLowerInvariant(),
            Status = "skipped",
            Reason = "review_unavailable",
            PrimaryModel = request.PrimaryModel,
            Primary = request.Primary,
            Final = request.Primary,
        };
        notes.Flags.Add("review_unavailable");
        return new WritingReviewOutcome(
            WritingReviewStatus.Skipped,
            request.Primary,
            request.Findings,
            [],
            [WritingJevReviewReasons.ReviewerUnresolved],
            notes,
            []);
    }

    private WritingReviewOutcome Skipped(WritingReviewRequest request, string reason)
    {
        var notes = new WritingReviewAdminNotes
        {
            Version = Version,
            Mode = request.Mode.ToString().ToLowerInvariant(),
            Status = "skipped",
            Reason = reason,
            PrimaryModel = request.PrimaryModel,
            Primary = request.Primary,
            Final = request.Primary,
        };
        return new WritingReviewOutcome(
            WritingReviewStatus.Skipped,
            request.Primary,
            request.Findings,
            [],
            [],
            notes,
            []);
    }

    /// <summary>
    /// What a stored stage must match to be resumed: the reviewer version, the letter, the primary scores, the finding
    /// set (by fingerprint) and whether the first pass is enhanced.
    /// </summary>
    private string FingerprintOf(WritingReviewRequest request, bool enhancedFirst)
    {
        var p = request.Primary;
        var findings = string.Join(',', request.Findings.Select(f => f.Fingerprint).OrderBy(x => x, StringComparer.Ordinal));
        var material = string.Join(
            '|',
            Version,
            Sha(request.Letter),
            $"{p.C1}.{p.C2}.{p.C3}.{p.C4}.{p.C5}.{p.C6}.{p.ScaledScore}",
            findings,
            enhancedFirst ? "enh" : "std");
        return Sha(material);
    }

    private static string Sha(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty)))[..40];
}
