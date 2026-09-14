using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Addendum Rev8 (11 Sep 2026) §7/§9/§14 — the Model Answer publish gate:
/// VERIFIED/CLEAN means zero unresolved violations of ANY severity under the
/// CURRENT validator; generation repairs only the failed rules and re-runs all
/// validators; the semantic validator's verdict is enforced; a stored verified
/// flag becomes invalid when the validator version changes; approval and the
/// candidate read path require verification under the running validator.
/// </summary>
public sealed class WritingRev8ModelAnswerGateTests
{
    private static LearnerDbContext NewDb()
        => new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"writing-rev8-gate-{Guid.NewGuid()}")
            .Options);

    private static WritingTaskModelAnswerService Service(
        LearnerDbContext db, IAiGatewayService gateway, IWritingModelAnswerSemanticValidator? semantic = null)
        => new(db, gateway, new WritingRuleEngine(new RulebookLoader()), TimeProvider.System,
            NullLogger<WritingTaskModelAnswerService>.Instance, semantic);

    // The live Taylor defect's violations are mostly MAJOR (duplicated age,
    // repeated "urgent", "also", paragraph-start pronouns ...) — the pre-Rev8
    // gate held on Critical findings only, which is how such letters became
    // "Ready". Here the compliant exemplar's text is degraded with one
    // Major-only defect (a mid-sentence "also").
    private static string MajorOnlyDefect()
        => WritingModelAnswerBatchTests.ExemplarText()
            .Replace("continues to smoke and has long been overweight.", "continues to smoke and has also been overweight.");

    [Fact]
    public async Task Import_Holds_A_Letter_With_Only_Major_Violations_And_Records_The_Report()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.", MajorOnlyDefect());
        var svc = Service(db, new ScriptedGateway());

        var dto = await svc.ImportAsync(scenarioId, MajorOnlyDefect(), "admin-1");

        Assert.Equal("HeldForReview", dto.Status);
        Assert.Equal("model_answer_rule_violations", dto.HoldReason);
        Assert.False(dto.IsCandidateVisible);
        var row = await db.WritingTaskModelAnswers.AsNoTracking().SingleAsync(a => a.ScenarioId == scenarioId);
        Assert.Null(row.ValidatorVersion);
        Assert.Contains("linker_avoid_words", row.ValidationReportJson);
    }

    [Fact]
    public async Task Import_Of_Compliant_Letter_Is_Verified_Under_Current_Validator_Then_Approvable()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        var svc = Service(db, new ScriptedGateway());

        var dto = await svc.ImportAsync(scenarioId, WritingModelAnswerBatchTests.ExemplarText(), "admin-1");

        Assert.Equal("Ready", dto.Status);
        Assert.False(dto.IsCandidateVisible);
        Assert.Equal(WritingRuleEngine.ValidatorVersion, dto.ValidatorVersion);
        Assert.StartsWith("rp-", dto.RulePackHash);
        Assert.InRange(dto.BodyWordCount ?? 0, 180, 200);
        Assert.Equal("verified_awaiting_approval", dto.VerificationStatus);

        var approved = await svc.ApproveAsync(scenarioId, "admin-1");
        Assert.True(approved!.IsCandidateVisible);
        var row = await db.WritingTaskModelAnswers.AsNoTracking().SingleAsync(a => a.ScenarioId == scenarioId);
        Assert.True(WritingTaskModelAnswerService.IsVerifiedForCandidates(row));
        Assert.Equal(1, await db.WritingTaskModelAnswers.CountAsync(WritingTaskModelAnswerService.CandidateVisibleVerified));
    }

    [Fact]
    public async Task Stored_Clean_Flag_From_An_Older_Validator_Is_Not_Candidate_Visible_And_Cannot_Be_Approved()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        db.WritingTaskModelAnswers.Add(new WritingTaskModelAnswer
        {
            Id = Guid.NewGuid(),
            ScenarioId = scenarioId,
            Status = WritingAssessmentModelAnswerStatus.Ready,
            IsCandidateVisible = true,
            ModelAnswerText = WritingModelAnswerBatchTests.ExemplarText(),
            ValidatorVersion = "writing-rules.rev5.2026-09-10",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var svc = Service(db, new ScriptedGateway());

        var row = await db.WritingTaskModelAnswers.AsNoTracking().SingleAsync(a => a.ScenarioId == scenarioId);
        Assert.False(WritingTaskModelAnswerService.IsVerifiedForCandidates(row));
        Assert.Equal(0, await db.WritingTaskModelAnswers.CountAsync(WritingTaskModelAnswerService.CandidateVisibleVerified));

        var ex = await Assert.ThrowsAsync<ApiException>(() => svc.ApproveAsync(scenarioId, "admin-1"));
        Assert.Equal("model_answer_not_verified", ex.ErrorCode);
    }

    [Fact]
    public async Task Revalidate_Apply_Stamps_Passing_Rows_And_Holds_Failing_Rows()
    {
        await using var db = NewDb();
        var good = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        var bad = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir again.", MajorOnlyDefect());
        foreach (var (id, text) in new[] { (good, WritingModelAnswerBatchTests.ExemplarText()), (bad, MajorOnlyDefect()) })
        {
            db.WritingTaskModelAnswers.Add(new WritingTaskModelAnswer
            {
                Id = Guid.NewGuid(),
                ScenarioId = id,
                Status = WritingAssessmentModelAnswerStatus.Ready,
                IsCandidateVisible = true,
                ModelAnswerText = text,
                ValidatorVersion = null,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        await db.SaveChangesAsync();
        var svc = Service(db, new ScriptedGateway());

        var report = await svc.RevalidateAsync(new WritingModelAnswerRevalidationRequest(
            Apply: false, IncludeSemantic: false, Profession: null, Offset: 0, Limit: 50, OnlyUnverified: false), "admin-1");
        Assert.Equal(2, report.Checked);
        Assert.Equal(1, report.Passed);
        Assert.Equal(1, report.Failed);
        Assert.Null((await db.WritingTaskModelAnswers.AsNoTracking().SingleAsync(a => a.ScenarioId == good)).ValidatorVersion);

        var applied = await svc.RevalidateAsync(new WritingModelAnswerRevalidationRequest(
            Apply: true, IncludeSemantic: false, Profession: null, Offset: 0, Limit: 50, OnlyUnverified: false), "admin-1");
        Assert.True(applied.Applied);
        var goodRow = await db.WritingTaskModelAnswers.AsNoTracking().SingleAsync(a => a.ScenarioId == good);
        var badRow = await db.WritingTaskModelAnswers.AsNoTracking().SingleAsync(a => a.ScenarioId == bad);
        Assert.Equal(WritingRuleEngine.ValidatorVersion, goodRow.ValidatorVersion);
        Assert.True(WritingTaskModelAnswerService.IsVerifiedForCandidates(goodRow));
        Assert.Equal(WritingAssessmentModelAnswerStatus.HeldForReview, badRow.Status);
        Assert.False(badRow.IsCandidateVisible);
        Assert.Equal("model_answer_revalidation_failed", badRow.HoldReason);
    }

    [Fact]
    public async Task Generate_Repairs_Only_Failed_Rules_Then_Stores_The_Passing_Text()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        var gateway = new ScriptedGateway(MajorOnlyDefect(), WritingModelAnswerBatchTests.ExemplarText());
        var svc = Service(db, gateway);

        var dto = await svc.GenerateAsync(scenarioId, "admin-1");

        Assert.Equal("Ready", dto.Status);
        Assert.Equal(2, gateway.Calls);
        Assert.Equal(1, dto.RepairCount);
        Assert.Contains("linker_avoid_words", gateway.LastUserInput);   // the repair prompt names the failed rule
        Assert.Contains("Previous draft", gateway.LastUserInput);
        Assert.Equal(WritingRuleEngine.ValidatorVersion, dto.ValidatorVersion);
    }

    [Fact]
    public async Task Generate_Never_Stores_When_Every_Attempt_Fails()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        var gateway = new ScriptedGateway(MajorOnlyDefect());
        var svc = Service(db, gateway);

        var dto = await svc.GenerateAsync(scenarioId, "admin-1");

        Assert.Equal("HeldForReview", dto.Status);
        Assert.Equal("model_answer_rule_violations", dto.HoldReason);
        Assert.Equal(4, gateway.Calls); // one generation + three targeted repairs, then stop
        Assert.False(dto.IsCandidateVisible);
    }

    [Fact]
    public async Task Semantic_Validator_Violations_Block_Storage_And_Unavailability_Is_Transient()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        var failing = new FixedSemantic(new WritingModelAnswerSemanticResult(false, false,
            [new WritingModelAnswerSemanticViolation("OWN-W-031", "Mr Weir has depression", "Background placed before the presenting complaint.")],
            "claude-sonnet-5", "2.2.0-canonical-addendum-two", null));
        var held = await Service(db, new ScriptedGateway(), failing)
            .ImportAsync(scenarioId, WritingModelAnswerBatchTests.ExemplarText(), "admin-1");
        Assert.Equal("model_answer_semantic_violations", held.HoldReason);
        Assert.Equal(1, failing.Calls);

        var unavailable = new FixedSemantic(new WritingModelAnswerSemanticResult(false, true, [], null, null, "semantic_validator_failed"));
        var transient = await Service(db, new ScriptedGateway(), unavailable)
            .ImportAsync(scenarioId, WritingModelAnswerBatchTests.ExemplarText(), "admin-1");
        Assert.Equal("model_answer_semantic_validator_unavailable", transient.HoldReason);
        Assert.True(WritingTaskModelAnswerService.IsTransientHold(transient.HoldReason));

        var passing = new FixedSemantic(new WritingModelAnswerSemanticResult(true, false, [], "claude-sonnet-5", "2.2.0-canonical-addendum-two", null));
        var ready = await Service(db, new ScriptedGateway(), passing)
            .ImportAsync(scenarioId, WritingModelAnswerBatchTests.ExemplarText(), "admin-1");
        Assert.Equal("Ready", ready.Status);
    }

    [Fact]
    public async Task Validate_Reports_Without_Storing_Anything()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        var svc = Service(db, new ScriptedGateway());

        var report = await svc.ValidateAsync(scenarioId, MajorOnlyDefect(), false, "admin-1");

        Assert.False(report.Passed);
        Assert.Contains(report.DeterministicFindings, f => f.RuleId == "BUILTIN.linker_avoid_words");
        Assert.Equal(WritingRuleEngine.ValidatorVersion, report.ValidatorVersion);
        Assert.Equal(0, await db.WritingTaskModelAnswers.CountAsync());
    }

    // Root cause (12 Sep 2026): a retry of the whole generation call — job
    // retry, or a second manual/worker trigger — sends byte-identical
    // attempt-0 content and so collides with CoordinatedAiGatewayService's
    // 5-minute AI-operation replay-window dedup. That must be held as a
    // distinct, non-transient reason (never generic "generation_failed"), so
    // the job stops hammer-retrying into the same window and the row is
    // instead picked up cleanly by the next enqueue sweep.
    [Fact]
    public async Task Generate_Holds_Non_Transiently_When_The_AI_Dedup_Window_Collides()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        var svc = Service(db, new DuplicateOperationGateway());

        var dto = await svc.GenerateAsync(scenarioId, "admin-1");

        Assert.Equal("HeldForReview", dto.Status);
        Assert.Equal("model_answer_generation_duplicate_window", dto.HoldReason);
        Assert.False(WritingTaskModelAnswerService.IsTransientHold(dto.HoldReason));
        Assert.False(dto.IsCandidateVisible);
    }

    // Root-cause fix (13 Sep 2026): once a scenario has ANY Completed AI
    // operation for its deterministic attempt-0 content, every later
    // regeneration attempt collided with that same row forever — the 12 Sep
    // fix above stopped the retry storm but never let the scenario actually
    // regenerate. This asserts the fix: a Completed-state collision is
    // retried, bounded, with a bumped replay discriminator, and a genuinely
    // new attempt succeeds. Existing global concurrent-duplicate protection
    // is untouched (see the two tests below and CompleteWithDuplicateRetryAsync's
    // doc comment for why bumping here can never double-charge a provider).
    [Fact]
    public async Task Generate_Retries_A_Completed_Collision_As_A_New_Attempt_And_Succeeds()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        var gateway = new CompletedCollisionThenSucceedsGateway(WritingModelAnswerBatchTests.ExemplarText());
        var svc = Service(db, gateway);

        var dto = await svc.GenerateAsync(scenarioId, "admin-1");

        Assert.Equal("Ready", dto.Status);
        Assert.Equal(2, gateway.Calls); // 1 collision + 1 successful retry
        Assert.Null(gateway.ResourceVersionsSeen[0]);   // first attempt: unmodified, matches current behaviour
        // Retry: a wall-clock-derived version, not a small sequential bump
        // (see CompleteWithDuplicateRetryAsync's doc comment for why -- a
        // small sequence collides with the coordinator's own historical
        // internal bumps on a heavily-reused scenario).
        Assert.True(gateway.ResourceVersionsSeen[1] > 1_000_000_000);
        Assert.Equal(WritingRuleEngine.ValidatorVersion, dto.ValidatorVersion);
    }

    // Live evidence (13 Sep 2026): the coordinator's own bounded internal
    // replay walk can exhaust and report a row as "duplicate" purely because
    // it ran out of rounds -- observed for a FailedTerminal predecessor,
    // which per AiOperationReplayPolicy.Decide is ALWAYS individually
    // CreateNewAttempt-eligible on its own. The reported State is therefore
    // not reliable evidence of why the coordinator gave up; only Indeterminate
    // (see the test below) is ever unsafe to retry past.
    [Fact]
    public async Task Generate_Retries_A_FailedTerminal_Reported_Collision_And_Succeeds()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        var gateway = new CompletedCollisionThenSucceedsGateway(
            WritingModelAnswerBatchTests.ExemplarText(), OetLearner.Api.Domain.AiOperationState.FailedTerminal);
        var svc = Service(db, gateway);

        var dto = await svc.GenerateAsync(scenarioId, "admin-1");

        Assert.Equal("Ready", dto.Status);
        Assert.Equal(2, gateway.Calls);
    }

    // Root-cause fix (13 Sep 2026 live incident): AiOperationInFlightException
    // means the coordinator's own ~4s bounded wait for a GENUINELY in-flight
    // predecessor just expired -- the predecessor is still actively running,
    // not orphaned. Rejoining with the SAME ResourceVersion (never bumping)
    // re-enters that same predecessor's wait instead of spawning a competing
    // real paid call. The old behaviour (bump-and-retry here) caused exactly
    // that: a real production pile-up of colliding real generations.
    [Fact]
    public async Task Generate_Retries_Past_An_Unresolved_InFlight_Slot_And_Succeeds()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        var gateway = new InFlightThenSucceedsGateway(WritingModelAnswerBatchTests.ExemplarText());
        var svc = Service(db, gateway);

        var dto = await svc.GenerateAsync(scenarioId, "admin-1");

        Assert.Equal("Ready", dto.Status);
        Assert.Equal(2, gateway.Calls); // 1 unresolved slot + 1 successful retry
        Assert.All(gateway.ResourceVersionsSeen, v => Assert.Null(v)); // never bumped -- rejoined the same slot
        Assert.Equal(WritingRuleEngine.ValidatorVersion, dto.ValidatorVersion);
    }

    // The exact pile-up scenario from the 13 Sep 2026 incident: a real
    // predecessor stays in-flight across MANY of the coordinator's own ~4s
    // bounded polls (its own generation is genuinely still running). Must
    // keep rejoining the SAME slot the whole time -- never bump to a new
    // version, which would each time spawn a brand-new competing real paid
    // call instead of waiting for the one already running.
    [Fact]
    public async Task Generate_Keeps_Rejoining_A_Long_Running_InFlight_Predecessor_Without_Bumping_Version()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        var gateway = new InFlightThenSucceedsGateway(WritingModelAnswerBatchTests.ExemplarText(), unresolvedCalls: 40);
        var svc = Service(db, gateway);

        var dto = await svc.GenerateAsync(scenarioId, "admin-1");

        Assert.Equal("Ready", dto.Status);
        Assert.Equal(41, gateway.Calls);
        Assert.All(gateway.ResourceVersionsSeen, v => Assert.Null(v)); // every single retry rejoined the same unbumped slot
    }

    // If a predecessor NEVER resolves within MaxInFlightWaitRounds, the
    // dedicated wait-and-rejoin catch gives up and falls back to the existing
    // bounded version-bump escape hatch (last resort) rather than holding the
    // row forever or looping without end.
    [Fact]
    public async Task Generate_Falls_Back_To_A_New_Version_After_Exhausting_The_InFlight_Wait()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        // 91 throws: the first 90 exhaust MaxInFlightWaitRounds rejoining the
        // same slot; the 91st throw finds that budget spent and falls to the
        // separate version-bump budget instead; call 92 (bumped) succeeds.
        var gateway = new InFlightThenSucceedsGateway(WritingModelAnswerBatchTests.ExemplarText(), unresolvedCalls: 91);
        var svc = Service(db, gateway);

        var dto = await svc.GenerateAsync(scenarioId, "admin-1");

        Assert.Equal("Ready", dto.Status);
        Assert.Equal(92, gateway.Calls);
        // The first 91 attempts (90 rejoins + the one that trips the fallback)
        // all still carry the original null slot; only the final, bumped
        // attempt carries a non-null version.
        Assert.Equal(91, gateway.ResourceVersionsSeen.Count(v => v is null));
        Assert.NotNull(gateway.ResourceVersionsSeen[^1]);
    }

    // A resource-version slot already claimed by a DIFFERENT payload (e.g. an
    // unrelated earlier auto-bump landed on the same version number) is the
    // other exception CompleteWithDuplicateRetryAsync retries past, bounded,
    // by trying the next version — never a duplicate-charge risk since it's
    // a plain "find an unclaimed slot" walk, not a replay of a real request.
    [Fact]
    public async Task Generate_Retries_Past_An_Occupied_Replay_Version_Slot_And_Succeeds()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        var gateway = new SlotConflictThenSucceedsGateway(WritingModelAnswerBatchTests.ExemplarText());
        var svc = Service(db, gateway);

        var dto = await svc.GenerateAsync(scenarioId, "admin-1");

        Assert.Equal("Ready", dto.Status);
        Assert.Equal(3, gateway.Calls); // 2 occupied slots + 1 successful retry
        Assert.Equal(WritingRuleEngine.ValidatorVersion, dto.ValidatorVersion);
    }

    // A predecessor whose outcome is ambiguous (never proven un-billed) must
    // NEVER be auto-retried, regardless of how long ago it happened —
    // AiOperationReplayPolicy.Decide() returns Duplicate for Indeterminate
    // unconditionally. This is the "true concurrent/ambiguous duplicate stays
    // blocked" case: the fix above must not weaken it. Assert the gateway is
    // called exactly once — no bump-and-retry is even attempted — and the
    // row still holds via the pre-existing safe fallback.
    [Fact]
    public async Task Generate_Never_Retries_A_Non_Completed_Predecessor()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        var gateway = new AlwaysDuplicateGateway(OetLearner.Api.Domain.AiOperationState.Indeterminate);
        var svc = Service(db, gateway);

        var dto = await svc.GenerateAsync(scenarioId, "admin-1");

        Assert.Equal("HeldForReview", dto.Status);
        Assert.Equal("model_answer_generation_duplicate_window", dto.HoldReason);
        Assert.False(WritingTaskModelAnswerService.IsTransientHold(dto.HoldReason));
        Assert.Equal(1, gateway.Calls); // never retried -- the guard requires State == Completed
    }

    [Fact]
    public void Contact_Offer_Courtesy_Sentence_Is_Not_An_Unmapped_Fact()
    {
        var letter = WritingModelAnswerBatchTests.ExemplarText();
        var facts = WritingModelAnswerBatchTests.CaseNoteSentencesFor(letter)
            .Where(s => !s.StartsWith("Should there be", StringComparison.Ordinal))
            .ToList();
        var grounding = WritingModelAnswerGroundingValidator.Validate(letter, facts);
        Assert.True(grounding.IsGrounded, string.Join(" | ", grounding.UnmappedSentences));
    }

    // ── Rev8 §7.1 — the Weir false minor: "3 children aged 13, 10 and 8"
    // made the old first-match age scan classify an adult patient as a
    // minor, so minor_naming_convention and the adult Re: line rule became
    // mutually unsatisfiable and LT-RR could never pass. ──

    [Fact]
    public void PatientAge_Extractor_Never_Attributes_Relative_Or_List_Ages_To_The_Patient()
    {
        var weirNotes = string.Join('\n',
            "Mr Michael Weir is a patient in your general practice, height 183cm",
            "He is married with 3 children aged 13, 10 and 8");
        Assert.Null(WritingPatientAgeExtractor.Extract(weirNotes));

        Assert.Equal(55, WritingPatientAgeExtractor.Extract("Mr David Taylor, aged 55, presented today."));
        Assert.Equal(9, WritingPatientAgeExtractor.Extract("The patient is 9 years old."));
        Assert.Null(WritingPatientAgeExtractor.Extract("His daughter, aged 8, attends with him."));
        Assert.Null(WritingPatientAgeExtractor.Extract("He has two sons aged 4 and 7."));
        Assert.Equal(45, WritingPatientAgeExtractor.Extract("Mr X, aged 45, has two sons aged 4 and 7."));
    }

    [Fact]
    public async Task Import_With_Relatives_Ages_In_Notes_Stays_Adult_And_Ready()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        db.WritingScenarioStructuredSentences.Add(new WritingScenarioStructuredSentence
        {
            Id = Guid.NewGuid(),
            ScenarioId = scenarioId,
            Ordinal = 999,
            SentenceText = "He is married with 3 children aged 13, 10 and 8",
            RelevanceLabel = "maybe",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var svc = Service(db, new ScriptedGateway());

        var dto = await svc.ImportAsync(scenarioId, WritingModelAnswerBatchTests.ExemplarText(), "admin-1");

        Assert.Equal("Ready", dto.Status);
        var row = await db.WritingTaskModelAnswers.AsNoTracking().SingleAsync(a => a.ScenarioId == scenarioId);
        Assert.DoesNotContain("minor_naming_convention", row.ValidationReportJson);
    }

    [Fact]
    public void Minor_Naming_Rule_Still_Fires_For_A_Genuine_Child_With_A_Titled_Re_Line()
    {
        var engine = new WritingRuleEngine(new RulebookLoader());
        var letter = WritingModelAnswerBatchTests.ExemplarText().Replace("Re: Mr Michael Weir", "Re: Master Tommy Atkins");

        var findings = engine.Lint(new WritingLintInput(
            LetterText: letter,
            LetterType: "LT-RR",
            PatientAge: 9,
            PatientIsMinor: true,
            Profession: ExamProfession.Medicine,
            IsModelAnswer: true));

        Assert.Contains(findings, f => f.RuleId.EndsWith("minor_naming_convention", StringComparison.Ordinal));
    }

    // ── Rev8 §7.2 — owner ruling (13 Sep 2026): a Model Answer must render
    // case-note wording in premium clinical English, so register_colloquial
    // fires even when the notes themselves use the word. There is
    // deliberately NO case-notes exemption; "fatigue"/"lethargy" ground
    // fine, as the stored exemplar proves. ──

    [Fact]
    public void Register_Colloquial_Fires_Even_When_The_Case_Notes_Use_The_Word()
    {
        var engine = new WritingRuleEngine(new RulebookLoader());
        var letter = WritingModelAnswerBatchTests.ExemplarText().Replace("with fatigue, stress and lethargy", "feeling tired, stressed and sluggish");
        const string notes = "He reported feeling run down: tired, stressed and sluggish";

        var withNotes = engine.Lint(new WritingLintInput(
            LetterText: letter, LetterType: "LT-RR",
            Profession: ExamProfession.Medicine, IsModelAnswer: true));
        Assert.Contains(withNotes, f => f.RuleId.EndsWith("register_colloquial", StringComparison.Ordinal));

        var withoutNotes = engine.Lint(new WritingLintInput(
            LetterText: letter, LetterType: "LT-RR",
            Profession: ExamProfession.Medicine, IsModelAnswer: true));
        Assert.Contains(withoutNotes, f => f.RuleId.EndsWith("register_colloquial", StringComparison.Ordinal));

        var premium = engine.Lint(new WritingLintInput(
            LetterText: WritingModelAnswerBatchTests.ExemplarText(),
            LetterType: "LT-RR",
            Profession: ExamProfession.Medicine, IsModelAnswer: true));
        Assert.DoesNotContain(premium, f => f.RuleId.EndsWith("register_colloquial", StringComparison.Ordinal));
    }

    // ── Import semantic flag — an import whose content has already been
    // semantically reviewed may be stored on the deterministic gate alone
    // (owner directive: no paid AI call per import). ──

    [Fact]
    public async Task Import_Without_Semantic_Does_Not_Call_The_Semantic_Validator_And_Still_Stores_Ready()
    {
        await using var db = NewDb();
        var scenarioId = await WritingModelAnswerBatchTests.SeedPublishedTaskAsync(db, "Refer Mr Weir.");
        var semantic = new FixedSemantic(new WritingModelAnswerSemanticResult(
            Passed: true, Unavailable: false, Violations: [], Model: "test", RulebookVersion: null, Error: null));
        var svc = Service(db, new ScriptedGateway(), semantic);

        var dto = await svc.ImportAsync(scenarioId, WritingModelAnswerBatchTests.ExemplarText(), "admin-1", includeSemantic: false);

        Assert.Equal("Ready", dto.Status);
        Assert.Equal(0, semantic.Calls);

        var stored = await svc.ImportAsync(scenarioId, WritingModelAnswerBatchTests.ExemplarText(), "admin-1");
        Assert.Equal("Ready", stored.Status);
        Assert.Equal(1, semantic.Calls);
    }


    private sealed class FixedSemantic(WritingModelAnswerSemanticResult result) : IWritingModelAnswerSemanticValidator
    {
        public int Calls { get; private set; }

        public Task<WritingModelAnswerSemanticResult> ValidateAsync(WritingModelAnswerSemanticRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class DuplicateOperationGateway : IAiGatewayService
    {
        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**",
                TaskInstruction = "generate",
            };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
            => throw new OetLearner.Api.Services.Ai.AiOperationDuplicateResultUnavailableException(
                "op-1", OetLearner.Api.Domain.AiOperationState.Completed, null);
    }

    /// <summary>Throws a Completed-state duplicate collision on the first
    /// call, then succeeds on the next -- the "deliberate later admin
    /// regeneration" case.</summary>
    private sealed class CompletedCollisionThenSucceedsGateway(
        string letter, OetLearner.Api.Domain.AiOperationState state = OetLearner.Api.Domain.AiOperationState.Completed)
        : IAiGatewayService
    {
        public int Calls { get; private set; }
        public List<int?> ResourceVersionsSeen { get; } = [];

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**",
                TaskInstruction = "generate",
            };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            ResourceVersionsSeen.Add(request.ResourceVersion);
            Calls++;
            if (Calls == 1)
            {
                throw new OetLearner.Api.Services.Ai.AiOperationDuplicateResultUnavailableException(
                    "op-completed-predecessor", state, null);
            }

            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                modelAnswerText = letter,
                whyThisWorks = new[] { "Grounded exemplar." },
                groundedFactReferences = new[] { "case-note-line:1" },
            });
            return Task.FromResult(new AiGatewayResult { Completion = json, ResolvedModel = "claude-sonnet-5" });
        }
    }

    /// <summary>Throws an unresolved in-flight-slot timeout on the first
    /// call, then succeeds -- the "orphaned slot from an earlier client
    /// timeout" case.</summary>
    private sealed class InFlightThenSucceedsGateway(string letter, int unresolvedCalls = 1) : IAiGatewayService
    {
        public int Calls { get; private set; }
        public List<int?> ResourceVersionsSeen { get; } = [];

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**",
                TaskInstruction = "generate",
            };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Calls++;
            ResourceVersionsSeen.Add(request.ResourceVersion);
            if (Calls <= unresolvedCalls)
            {
                throw new OetLearner.Api.Services.Ai.AiOperationInFlightException("idem-key-stuck");
            }

            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                modelAnswerText = letter,
                whyThisWorks = new[] { "Grounded exemplar." },
                groundedFactReferences = new[] { "case-note-line:1" },
            });
            return Task.FromResult(new AiGatewayResult { Completion = json, ResolvedModel = "claude-sonnet-5" });
        }
    }

    /// <summary>Throws a resource-slot conflict on the first two calls, then
    /// succeeds -- the "occupied replay-version slot from unrelated activity"
    /// case.</summary>
    private sealed class SlotConflictThenSucceedsGateway(string letter) : IAiGatewayService
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
            if (Calls <= 2)
            {
                throw new OetLearner.Api.Services.Ai.AiOperationConflictException("idem-key-occupied");
            }

            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                modelAnswerText = letter,
                whyThisWorks = new[] { "Grounded exemplar." },
                groundedFactReferences = new[] { "case-note-line:1" },
            });
            return Task.FromResult(new AiGatewayResult { Completion = json, ResolvedModel = "claude-sonnet-5" });
        }
    }

    /// <summary>Always throws a duplicate collision in the given (non-Completed
    /// by design, for the "never retry an ambiguous/concurrent predecessor"
    /// test) state.</summary>
    private sealed class AlwaysDuplicateGateway(OetLearner.Api.Domain.AiOperationState state) : IAiGatewayService
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
            throw new OetLearner.Api.Services.Ai.AiOperationDuplicateResultUnavailableException(
                "op-ambiguous", state, null);
        }
    }

    /// <summary>Returns the scripted letters in order (the last one repeats).</summary>
    private sealed class ScriptedGateway(params string[] letters) : IAiGatewayService
    {
        public int Calls { get; private set; }
        public string LastUserInput { get; private set; } = "";

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**",
                TaskInstruction = "generate",
            };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            var letter = letters.Length == 0
                ? WritingModelAnswerBatchTests.ExemplarText()
                : letters[Math.Min(Calls, letters.Length - 1)];
            Calls++;
            LastUserInput = request.UserInput ?? "";
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                modelAnswerText = letter,
                whyThisWorks = new[] { "Grounded exemplar." },
                groundedFactReferences = new[] { "case-note-line:1" },
            });
            return Task.FromResult(new AiGatewayResult { Completion = json, ResolvedModel = "claude-sonnet-5" });
        }
    }
}
