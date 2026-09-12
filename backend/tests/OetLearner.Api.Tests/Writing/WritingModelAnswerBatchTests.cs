using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Preparation-time Model Answer backfill
/// (<see cref="WritingTaskModelAnswerService.GenerateMissingAsync"/>): missing
/// answers are generated once, Ready answers are never regenerated blindly,
/// and repeat runs are idempotent with zero additional provider calls.
/// </summary>
public sealed class WritingModelAnswerBatchTests
{
    private static LearnerDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"writing-model-answer-batch-{Guid.NewGuid()}")
            .Options;
        return new LearnerDbContext(options);
    }

    internal static async Task<Guid> SeedPublishedTaskAsync(LearnerDbContext db, string taskPrompt, string? exemplar = null)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.WritingScenarios.Add(new WritingScenario
        {
            Id = id,
            Title = "Batch task",
            Profession = "medicine",
            LetterType = "LT-RR",
            TaskPromptMarkdown = taskPrompt,
            Status = "published",
            AuthorId = "admin-1",
            CreatedAt = now,
            UpdatedAt = now,
        });
        // Case notes = the exemplar's own body sentences, so the grounding
        // gate (every body sentence traceable to a case-note fact) passes.
        var ordinal = 1;
        foreach (var sentence in CaseNoteSentencesFor(exemplar ?? ExemplarText()))
        {
            db.WritingScenarioStructuredSentences.Add(new WritingScenarioStructuredSentence
            {
                Id = Guid.NewGuid(),
                ScenarioId = id,
                Ordinal = ordinal++,
                SentenceText = sentence,
                RelevanceLabel = "relevant",
                CreatedAt = now,
            });
        }
        await db.SaveChangesAsync();
        return id;
    }

    internal static IEnumerable<string> CaseNoteSentencesFor(string letter)
        => System.Text.RegularExpressions.Regex.Split(WritingModelAnswerWordCounter.ExtractBody(letter), @"(?<=[.!?])\s+")
            .Select(s => s.Trim())
            .Where(s => s.Length > 0);

    /// <summary>
    /// Owner-compliant exemplar (Addendum Rev8 house style): address, date,
    /// consecutive salutation + Re:, one blank line after Re:, "I am writing
    /// to ..." opening, name-first body paragraphs, clinical values with
    /// spaced units, background before the closure, contact-offer final
    /// sentence, designation-only sign-off; 180-200 BODY words; zero findings
    /// of any severity in Model Answer mode (the same letter is the
    /// WritingRev8LiveFixtureTests Weir correction).
    /// </summary>
    internal static string ExemplarText() => """
Dr M McLaren
Neurologist
Suite 3
67 The Crescent
Newtown

11 August 2014

Dear Dr McLaren,
Re: Mr Michael Weir

I am writing to refer Mr Weir, who is presenting with features suggestive of multiple sclerosis, for a full neurological assessment.

On 9 August 2014, Mr Weir reported dizziness, two blackouts lasting a few minutes each, tingling in his hands, ongoing left leg weakness, breathlessness and occasional constipation. Examination revealed bilateral sensory loss in his hands and a diminished left patellar reflex. A CT scan of the head and spine has been arranged to exclude central causes.

Mr Weir first presented on 29 June 2014 with fatigue and stress, and blood tests were arranged. On review on 7 July 2014, he reported persistent fatigue and low mood and had developed left leg weakness. His cholesterol was 6.37 mmol/L, and his blood count showed a low white cell count, red cell count, haemoglobin and haematocrit. He was assessed for hypercholesterolaemia and advised on lifestyle changes.

Mr Weir has depression, treated with sertraline hydrochloride, known as Zoloft, since September 2012. He smokes and has been overweight long term.

I would be grateful if you could assess Mr Weir, including MRI if indicated. Should there be any queries, kindly do not hesitate to contact me.

Yours sincerely,

Doctor
""";

    // ── Option C background worker: enqueue + idempotent work items ──

    [Fact]
    public async Task EnqueueMissing_EnqueuesOnce_DuplicateEnqueueCollapses()
    {
        await using var db = NewDb();
        await SeedPublishedTaskAsync(db, "Write a routine referral for John Jones to City Clinic.");
        var svc = new WritingTaskModelAnswerService(
            db, new ExemplarGateway(), new WritingRuleEngine(new RulebookLoader()),
            TimeProvider.System, NullLogger<WritingTaskModelAnswerService>.Instance);

        var first = await svc.EnqueueMissingAsync("admin-1", 10, CancellationToken.None);
        var second = await svc.EnqueueMissingAsync("admin-1", 10, CancellationToken.None);

        Assert.Equal(1, first.Enqueued);
        Assert.Equal(0, second.Enqueued);
        Assert.Equal(1, await db.BackgroundJobs.CountAsync(j =>
            j.Type == JobType.WritingModelAnswerGeneration));
    }

    // Root-cause fix (12 Sep 2026): the enqueue job id is deterministic per
    // scenario ("jb-wr-model-answer-{scenarioId}"), so a PRIOR job that ran
    // its retries out to a terminal state (Failed/Completed) left a row
    // occupying that exact id forever. EnqueueMissingAsync used to blindly
    // INSERT a fresh row, hit a primary-key conflict, and silently count the
    // scenario as "skipped" -- indistinguishable from a legitimately busy
    // job, and with no way to ever try that scenario again. This asserts the
    // fix: a terminal row is reused and reset to Queued, not permanently
    // locked out.
    [Fact]
    public async Task EnqueueMissing_Resurrects_A_Scenario_Whose_Prior_Job_Went_Terminal()
    {
        await using var db = NewDb();
        var scenarioId = await SeedPublishedTaskAsync(db, "Write a routine referral for John Jones to City Clinic.");
        var jobId = $"jb-wr-model-answer-{scenarioId:N}";
        db.BackgroundJobs.Add(new BackgroundJobItem
        {
            Id = jobId,
            Type = JobType.WritingModelAnswerGeneration,
            State = AsyncState.Failed,
            ResourceId = scenarioId.ToString("D"),
            StatusReasonCode = "processing_failed",
            StatusMessage = "Failed after 3 attempts: simulated stale terminal job.",
            RetryCount = 3,
            RetryAfterMs = 0,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
            AvailableAt = DateTimeOffset.UtcNow.AddHours(-2),
            LastTransitionAt = DateTimeOffset.UtcNow.AddHours(-2),
        });
        await db.SaveChangesAsync();
        var svc = new WritingTaskModelAnswerService(
            db, new ExemplarGateway(), new WritingRuleEngine(new RulebookLoader()),
            TimeProvider.System, NullLogger<WritingTaskModelAnswerService>.Instance);

        var result = await svc.EnqueueMissingAsync("admin-1", 10, CancellationToken.None);

        Assert.Equal(1, result.Enqueued);
        Assert.Equal(0, result.Skipped);
        var job = await db.BackgroundJobs.SingleAsync(j => j.Id == jobId);
        Assert.Equal(AsyncState.Queued, job.State);
        Assert.Equal(0, job.RetryCount);
    }

    [Fact]
    public async Task GenerateIfNeeded_SkipsFreshReady_WithZeroProviderCalls()
    {
        await using var db = NewDb();
        var scenarioId = await SeedPublishedTaskAsync(db, "Write a routine referral for John Jones to City Clinic.");
        var gateway = new ExemplarGateway();
        var svc = new WritingTaskModelAnswerService(
            db, gateway, new WritingRuleEngine(new RulebookLoader()),
            TimeProvider.System, NullLogger<WritingTaskModelAnswerService>.Instance);

        var first = await svc.GenerateIfNeededAsync(scenarioId, "admin-1", CancellationToken.None);
        Assert.Equal("generated", first.Outcome);
        Assert.Equal(1, gateway.Calls);

        // Redelivery (queue retry / restart recovery) must not spend again.
        var second = await svc.GenerateIfNeededAsync(scenarioId, "admin-1", CancellationToken.None);
        Assert.Equal("ready-skipped", second.Outcome);
        Assert.Equal(1, gateway.Calls);
    }

    [Fact]
    public void Transient_hold_classification_only_flags_generation_failures()
    {
        Assert.True(WritingTaskModelAnswerService.IsTransientHold("model_answer_generation_failed"));
        Assert.False(WritingTaskModelAnswerService.IsTransientHold("model_answer_word_count_out_of_range"));
        Assert.False(WritingTaskModelAnswerService.IsTransientHold("model_answer_unmapped_sentence"));
        Assert.False(WritingTaskModelAnswerService.IsTransientHold(null));
    }

    private sealed class ExemplarGateway : IAiGatewayService
    {
        public int Calls { get; private set; }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**",
                TaskInstruction = "generate",
            };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Calls++;
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                modelAnswerText = ExemplarText(),
                whyThisWorks = new[] { "Grounded exemplar." },
                groundedFactReferences = new[] { "case-note-line:1" },
            });
            return Task.FromResult(new AiGatewayResult
            {
                Completion = json,
                ResolvedModel = "claude-sonnet-5",
            });
        }
    }

    [Fact]
    public async Task GenerateMissing_GeneratesOnceThenSkipsWithoutNewProviderCalls()
    {
        await using var db = NewDb();
        var gateway = new ExemplarGateway();
        var svc = new WritingTaskModelAnswerService(
            db, gateway, new WritingRuleEngine(new RulebookLoader()), TimeProvider.System,
            NullLogger<WritingTaskModelAnswerService>.Instance);

        var scenarioId = await SeedPublishedTaskAsync(db, "Write a routine referral for John Jones to City Clinic.");

        var first = await svc.GenerateMissingAsync("admin-1", 5, false, CancellationToken.None);
        Assert.Equal(1, first.Generated);
        Assert.Equal(0, first.Skipped);
        Assert.Equal("generated", first.Items.Single().Outcome);
        Assert.Equal(1, gateway.Calls);

        var row = await db.WritingTaskModelAnswers.AsNoTracking().SingleAsync(a => a.ScenarioId == scenarioId);
        Assert.Equal(WritingAssessmentModelAnswerStatus.Ready, row.Status);
        Assert.False(row.IsCandidateVisible);

        // Awaiting approval: must NOT regenerate (would discard the pending
        // review and burn another provider call).
        var second = await svc.GenerateMissingAsync("admin-1", 5, false, CancellationToken.None);
        Assert.Equal(0, second.Generated);
        Assert.Equal(1, second.Skipped);
        Assert.Equal("awaiting_approval", second.Items.Single().HoldReason);
        Assert.Equal(1, gateway.Calls);

        // Approved + fresh: reused forever.
        await svc.ApproveAsync(scenarioId, "admin-1", CancellationToken.None);
        var third = await svc.GenerateMissingAsync("admin-1", 5, false, CancellationToken.None);
        Assert.Equal(0, third.Generated);
        Assert.Equal("already_ready", third.Items.Single().HoldReason);
        Assert.Equal(1, gateway.Calls);
    }

    [Fact]
    public async Task GenerateMissing_RegeneratesStaleOnlyOnExplicitRequest()
    {
        await using var db = NewDb();
        var gateway = new ExemplarGateway();
        var svc = new WritingTaskModelAnswerService(
            db, gateway, new WritingRuleEngine(new RulebookLoader()), TimeProvider.System,
            NullLogger<WritingTaskModelAnswerService>.Instance);

        var scenarioId = await SeedPublishedTaskAsync(db, "Write a routine referral for John Jones to City Clinic.");
        await svc.GenerateMissingAsync("admin-1", 5, false, CancellationToken.None);
        await svc.ApproveAsync(scenarioId, "admin-1", CancellationToken.None);
        Assert.Equal(1, gateway.Calls);

        // Drift the source content: the approved answer is now stale.
        var scenario = await db.WritingScenarios.FirstAsync(s => s.Id == scenarioId);
        scenario.TaskPromptMarkdown = "Write a routine referral for John Jones to City Clinic urgently.";
        await db.SaveChangesAsync();

        var withoutStale = await svc.GenerateMissingAsync("admin-1", 5, false, CancellationToken.None);
        Assert.Equal(0, withoutStale.Generated);
        Assert.Equal("stale_refresh_not_requested", withoutStale.Items.Single().HoldReason);
        Assert.Equal(1, gateway.Calls);

        var withStale = await svc.GenerateMissingAsync("admin-1", 5, true, CancellationToken.None);
        Assert.Equal(1, withStale.Generated);
        Assert.Equal(2, gateway.Calls);
    }
}
