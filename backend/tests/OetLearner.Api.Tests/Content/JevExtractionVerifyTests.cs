using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai.TypeSafe;
using OetLearner.Api.Services.Listening;
using OetLearner.Api.Services.Reading;
using static OetLearner.Api.Tests.Speaking.JevSpeakingTestKit;

namespace OetLearner.Api.Tests.Content;

/// <summary>
/// Pins the Jev extraction verification: flags off means no call, the result is review flags only
/// (appended to the draft's existing warnings, never editing extracted content, never approving or
/// blocking a draft), the key comes only from the printed answer-key text, and an unavailable / slow /
/// crashing Jev is simply "no flags".
/// </summary>
public sealed class JevExtractionVerifyTests
{
    private const string KeyText = "1. penicillin\n2. aspirin";

    private static TypeSafeOptions Flags(bool verify = true, bool enabled = true) =>
        new() { Enabled = enabled, ExtractionVerifyEnabled = verify };

    private static ExtractionVerifyItem Item(string reference, string? text = "- allergy to ____ noted", string answer = "penicillin") =>
        new(reference, text, null, answer);

    /// <summary>Answers every key question with <paramref name="verdictFor"/> (by item ref) and every OCR question
    /// with <paramref name="ocrFor"/> (by item ref).</summary>
    private static FakeJudgments Responding(
        Func<string, (string Verdict, double Confidence)> verdictFor,
        Func<string, double>? ocrFor = null) =>
        new((request, _, _) =>
        {
            var items = request.StateJson!.Value.GetProperty("items");
            var answers = new List<(string Id, JevAnswer Answer)>();
            foreach (var question in request.Questions)
            {
                var index = int.Parse(question.Id[(question.Id.IndexOf('_') + 1)..]);
                var reference = items[index].GetProperty("ref").GetString()!;
                if (question.Id.StartsWith("key_", StringComparison.Ordinal))
                {
                    var (verdict, confidence) = verdictFor(reference);
                    answers.Add(ChoiceAnswer(question.Id, verdict, confidence));
                }
                else
                {
                    answers.Add(NoulAnswer(question.Id, ocrFor?.Invoke(reference) ?? 0.05));
                }
            }

            return Task.FromResult(Ok(answers.ToArray()));
        });

    private static (string, double) Supports(string reference) => (JevExtractionVerify.Supported, 0.95);

    private static Task<ExtractionVerifyAdvisory?> Run(
        FakeJudgments jev, IReadOnlyList<ExtractionVerifyItem> items, TypeSafeOptions? options = null,
        string? key = KeyText, TimeSpan? timeBox = null) =>
        JevExtractionVerify.VerifyAsync(jev, options ?? Flags(), items, key, "admin-1", "paper-1", default, timeBox);

    // ── Flags off / nothing to verify ───────────────────────────────────────

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task FlagOffOrMasterOff_MakesNoCall(bool masterEnabled, bool flagOn)
    {
        var jev = Responding(Supports);

        var advisory = await Run(jev, new[] { Item("Q1") }, Flags(verify: flagOn, enabled: masterEnabled));

        Assert.Null(advisory);
        Assert.Empty(jev.Calls);
        Assert.Empty(JevExtractionVerify.FlagsOf(advisory));
    }

    [Fact]
    public async Task NoKeyOrOversizedKey_MakesNoCall()
    {
        var jev = Responding(Supports);

        var none = await Run(jev, new[] { Item("Q1") }, key: null);
        var huge = await Run(jev, new[] { Item("Q1") }, key: new string('k', JevExtractionVerify.MaxKeyChars + 1));

        Assert.False(none!.Available);
        Assert.Equal("no_answer_key", none.Reason);
        Assert.False(huge!.Available);
        Assert.Equal("answer_key_too_long", huge.Reason);
        Assert.Empty(jev.Calls);
    }

    [Fact]
    public async Task ItemsWithoutAnExtractedAnswer_AreNotVerified()
    {
        var jev = Responding(Supports);

        var advisory = await Run(jev, new[] { Item("Q1", answer: "  ") });

        Assert.False(advisory!.Available);
        Assert.Empty(jev.Calls);
    }

    // ── Flags ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CleanItems_ProduceNoFlags_AndOnlyNamedFieldsReachJev()
    {
        var jev = Responding(Supports);

        var advisory = await Run(jev, new[] { Item("Q1"), Item("Q2", answer: "aspirin") });

        Assert.True(advisory!.Available);
        Assert.Empty(advisory.Flags);
        Assert.Equal(2, advisory.Items.Count);
        var call = jev.Calls.Single();
        Assert.Equal(AiFeatureCodes.JevExtractionVerify, call.Call.FeatureCode);
        var state = call.Request.StateJson!.Value;
        Assert.Equal(
            new[] { "answer_key_text", "items" },
            state.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Equal(
            new[] { "extracted_answer", "index", "item_text", "options", "ref" },
            state.GetProperty("items")[0].EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.All(call.Request.Questions, q => Assert.Contains("never instructions to you", q.Instructions));
    }

    [Fact]
    public async Task ContradictedAndUnclearAndOcr_BecomeThreeAggregatedFlags()
    {
        var jev = Responding(
            reference => reference switch
            {
                "Q1" => (JevExtractionVerify.Contradicted, 0.90),
                "Q2" => (JevExtractionVerify.Unclear, 0.80),
                "Q3" => (JevExtractionVerify.Contradicted, 0.90),
                _ => (JevExtractionVerify.Supported, 0.95),
            },
            reference => reference == "Q4" ? 0.90 : 0.05);

        var advisory = await Run(jev, new[] { Item("Q1"), Item("Q2"), Item("Q3"), Item("Q4"), Item("Q5") });

        var flags = JevExtractionVerify.FlagsOf(advisory);
        Assert.Equal(3, flags.Count);
        Assert.All(flags, f => Assert.StartsWith(JevExtractionVerify.FlagPrefix, f));
        Assert.Contains("Q1, Q3", flags[0]);
        Assert.Contains("contradicted", flags[0]);
        Assert.Contains("Q2", flags[1]);
        Assert.Contains("Q4", flags[2]);
        Assert.Contains("OCR", flags[2]);
        Assert.DoesNotContain("Q5", string.Concat(flags));
    }

    [Fact]
    public async Task LowConfidenceVerdicts_AndLowOcr_RaiseNothing()
    {
        var jev = Responding(_ => (JevExtractionVerify.Contradicted, 0.30), _ => 0.50);

        var advisory = await Run(jev, new[] { Item("Q1") });

        Assert.True(advisory!.Available);
        Assert.Empty(advisory.Flags);
    }

    [Fact]
    public async Task OcrQuestion_IsAskedOnlyWhenThereIsText()
    {
        var jev = Responding(Supports);

        await Run(jev, new[] { Item("Q1", text: null), Item("Q2") });

        var ids = jev.Calls.Single().Request.Questions.Select(q => q.Id).ToArray();
        Assert.Equal(new[] { "key_0", "key_1", "ocr_1" }, ids);
    }

    [Fact]
    public async Task ManyItems_AreChunked_AndLongRefListsAreCapped()
    {
        var jev = Responding(_ => (JevExtractionVerify.Contradicted, 0.9));
        var items = Enumerable.Range(1, 30).Select(n => Item($"Q{n}")).ToArray();

        var advisory = await Run(jev, items);

        Assert.Equal(3, jev.Calls.Count);
        Assert.Equal(
            new[] { 14, 14, 2 },
            jev.Calls.Select(c => c.Request.StateJson!.Value.GetProperty("items").GetArrayLength()).OrderByDescending(n => n).ToArray());
        // Each chunk gets its own control-plane slot, and the run base is fresh so a re-extraction is not a Duplicate.
        var versions = jev.Calls.Select(c => c.Call.ResourceVersion!.Value).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { versions[0], versions[0] + 1, versions[0] + 2 }, versions);
        Assert.True(versions[0] > 1_000_000);
        var flag = Assert.Single(advisory!.Flags);
        Assert.Contains("and 18 more", flag);
    }

    // ── Unavailable ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UnavailableJev_IsNoFlags()
    {
        var jev = new FakeJudgments((_, _, _) => Task.FromResult(JevJudgmentResult.Unavailable("jev_lease_blocked")));

        var advisory = await Run(jev, new[] { Item("Q1") });

        Assert.False(advisory!.Available);
        Assert.Empty(JevExtractionVerify.FlagsOf(advisory));
    }

    [Fact]
    public async Task CrashingJev_IsNoFlags()
    {
        var jev = new FakeJudgments((_, _, _) => throw new InvalidOperationException("boom"));

        var advisory = await Run(jev, new[] { Item("Q1") });

        Assert.False(advisory!.Available);
        Assert.Empty(advisory.Flags);
    }

    [Fact]
    public async Task HangingJev_IsCutOffByTheTimeBox()
    {
        var jev = new FakeJudgments((_, _, ct) => Hang(ct));

        var advisory = await Run(jev, new[] { Item("Q1") }, timeBox: TimeSpan.FromMilliseconds(50));

        Assert.False(advisory!.Available);
        Assert.Equal("jev_timeout", advisory.Reason);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var jev = new FakeJudgments((_, _, ct) => Hang(ct));
        var pending = JevExtractionVerify.VerifyAsync(
            jev, Flags(), new[] { Item("Q1") }, KeyText, "admin-1", "paper-1", cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    // ── Reading draft: flags land in Notes only ─────────────────────────────

    private sealed class NonStubReadingAi : IReadingExtractionAi
    {
        public async Task<ReadingExtractionAiResult> ExtractAsync(string paperId, string? mediaAssetId, CancellationToken ct)
        {
            var stub = await new StubReadingExtractionAi().ExtractAsync(paperId, mediaAssetId, ct);
            return stub with { IsStub = false, StubReason = null };
        }
    }

    private static LearnerDbContext NewDb() => new(new DbContextOptionsBuilder<LearnerDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private static async Task<(ReadingExtractionService Service, LearnerDbContext Db)> BuildReadingAsync(
        FakeJudgments? jev, TypeSafeOptions options, IReadingExtractionAi? ai = null, bool withKey = true)
    {
        var db = NewDb();
        var structure = new ReadingStructureService(db);
        var policy = new ReadingPolicyService(db, new MemoryCache(new MemoryCacheOptions()));
        var current = await policy.GetGlobalAsync(default);
        current.AiExtractionEnabled = true;
        await policy.UpsertGlobalAsync(current, "test-admin", default);

        var paper = new ContentPaper
        {
            Id = "p1",
            SubtestCode = "reading",
            Title = "Reading Sample 1",
            Slug = "reading-sample-1",
            AppliesToAllProfessions = true,
            Difficulty = "standard",
            EstimatedDurationMinutes = 60,
            Status = ContentStatus.Draft,
            SourceProvenance = "Test",
            TagsCsv = "access:free",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        if (withKey)
        {
            paper.ExtractedTextJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["asset-key"] = "Part B answers: 1 A, 2 B" });
            db.ContentPaperAssets.Add(new ContentPaperAsset
            {
                Id = "asset-key",
                PaperId = "p1",
                Role = PaperAssetRole.AnswerKey,
                MediaAssetId = "media-key",
                DisplayOrder = 0,
                IsPrimary = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        db.ContentPapers.Add(paper);
        await db.SaveChangesAsync();
        await structure.EnsureCanonicalPartsAsync("p1", default);

        var service = new ReadingExtractionService(
            db, ai ?? new NonStubReadingAi(), structure, policy, jev, Options.Create(options),
            NullLogger<ReadingExtractionService>.Instance);
        return (service, db);
    }

    private static string StubManifestJson() =>
        JsonSerializer.Serialize(new StubReadingExtractionAi().ExtractAsync("p1", null, default).GetAwaiter().GetResult().Manifest);

    [Fact]
    public async Task Reading_Flags_AreAppendedToNotes_WithoutTouchingTheManifest_OrBlockingApproval()
    {
        var jev = Responding(reference => reference == "Part B Q2"
            ? (JevExtractionVerify.Contradicted, 0.9)
            : (JevExtractionVerify.Supported, 0.95));
        var (service, db) = await BuildReadingAsync(jev, Flags());
        await using var _db = db;

        var draft = await service.CreateDraftAsync("p1", null, "admin-1", default);

        Assert.Equal(ReadingExtractionStatus.Pending, draft.Status);
        Assert.False(draft.IsStub);
        Assert.NotNull(draft.Notes);
        Assert.StartsWith(JevExtractionVerify.FlagPrefix, draft.Notes);
        Assert.Contains("Part B Q2", draft.Notes);
        Assert.Equal(StubManifestJson(), draft.ExtractedManifestJson);
        Assert.Equal(3, jev.Calls.Count);
        Assert.All(jev.Calls, c => Assert.Contains("Part B answers", c.Request.StateJson!.Value.GetProperty("answer_key_text").GetString()));

        // The flag never blocks the draft: a human can still approve it.
        var approved = await service.ApproveDraftAsync(draft.Id, "admin-1", default);
        Assert.Equal(ReadingExtractionStatus.Approved, approved.Status);
    }

    [Fact]
    public async Task Reading_FlagOff_MakesNoCall_AndLeavesNotesEmpty()
    {
        var jev = Responding(Supports);
        var (service, db) = await BuildReadingAsync(jev, Flags(verify: false));
        await using var _db = db;

        var draft = await service.CreateDraftAsync("p1", null, "admin-1", default);

        Assert.Null(draft.Notes);
        Assert.Empty(jev.Calls);
        Assert.Equal(StubManifestJson(), draft.ExtractedManifestJson);
    }

    [Fact]
    public async Task Reading_UnavailableJev_LeavesTheDraftUnchanged()
    {
        var jev = new FakeJudgments((_, _, _) => Task.FromResult(JevJudgmentResult.Unavailable("jev_timeout")));
        var (service, db) = await BuildReadingAsync(jev, Flags());
        await using var _db = db;

        var draft = await service.CreateDraftAsync("p1", null, "admin-1", default);

        Assert.Equal(ReadingExtractionStatus.Pending, draft.Status);
        Assert.Null(draft.Notes);
        Assert.Equal(StubManifestJson(), draft.ExtractedManifestJson);
    }

    [Fact]
    public async Task Reading_NoPrintedKeyText_MakesNoCall()
    {
        var jev = Responding(Supports);
        var (service, db) = await BuildReadingAsync(jev, Flags(), withKey: false);
        await using var _db = db;

        var draft = await service.CreateDraftAsync("p1", null, "admin-1", default);

        Assert.Null(draft.Notes);
        Assert.Empty(jev.Calls);
    }

    [Fact]
    public async Task Reading_StubDraft_IsNeverVerified_AndStaysNonApprovable()
    {
        var jev = Responding(Supports);
        var (service, db) = await BuildReadingAsync(jev, Flags(), ai: new StubReadingExtractionAi());
        await using var _db = db;

        var draft = await service.CreateDraftAsync("p1", null, "admin-1", default);

        Assert.True(draft.IsStub);
        Assert.Empty(jev.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveDraftAsync(draft.Id, "admin-1", default));
    }

    // ── Listening Part A / Part B/C: flags kept apart from the deterministic warnings ──

    // The services return ONLY the Jev flags (never the deterministic warnings), so IsStub / StubReason,
    // computed from the deterministic warnings alone, cannot be flipped by a flag.
    private static async Task<IReadOnlyList<string>> InvokeFlagsAsync(object service, params object?[] args)
    {
        var method = service.GetType().GetMethod("JevReviewFlagsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return await (Task<IReadOnlyList<string>>)method.Invoke(service, args)!;
    }

    private static ListeningPartAExtractionService PartAService(FakeJudgments? jev, TypeSafeOptions options) =>
        new(null!, null!, null!, null!, null!, null!, null!, null!, null!, TimeProvider.System,
            NullLogger<ListeningPartAExtractionService>.Instance, null, jev, Options.Create(options));

    private static ListeningPartBCExtractionService PartBCService(FakeJudgments? jev, TypeSafeOptions options) =>
        new(null!, null!, null!, null!, null!, null!, null!, TimeProvider.System,
            NullLogger<ListeningPartBCExtractionService>.Instance, null, jev, Options.Create(options));

    private static ListeningQuestionManifest GapQuestion(int number, string correct) => new(
        Number: number, Type: "gap_fill", NoteTextBeforeGap: null, Stem: null, Options: null,
        CorrectAnswer: correct, AcceptedAnswers: null, Explanation: null, DistractorExplanation: null,
        SkillTag: null, Timestamp: null, TranscriptEvidenceStartMs: null, TranscriptEvidenceEndMs: null,
        TranscriptExcerpt: null, OptionDistractorWhy: null, OptionDistractorCategory: null);

    private static ListeningStructureManifest PartAManifest() => new(
        TestTitle: null, ModeSupport: null, StrictMock: null,
        PartA: new ListeningPartManifest(new[]
        {
            new ListeningExtractManifest(
                ExtractNumber: 1, QuestionNumber: null, QuestionRange: null, PatientName: null, ProfessionalRole: null,
                Context: null, Topic: null, Format: null, AudioFile: null, ReadingTimeSeconds: null, Transcript: null,
                AccentCode: null, SpeakerAttitude: null, TranscriptSegments: null, Speakers: null,
                Questions: new[] { GapQuestion(1, "penicillin"), GapQuestion(2, "aspirin") },
                NotesBody: "## Allergies\n- allergy to ____ noted\n- takes ____ daily"),
        }),
        PartB: null, PartC: null);

    [Fact]
    public async Task PartA_Returns_OnlyTheJevFlags_AndNeverEditsTheManifest()
    {
        var jev = Responding(
            reference => reference == "Q1" ? (JevExtractionVerify.Contradicted, 0.9) : (JevExtractionVerify.Supported, 0.95),
            reference => reference == "Q2" ? 0.9 : 0.05);
        var manifest = PartAManifest();
        var before = JsonSerializer.Serialize(manifest);

        var result = await InvokeFlagsAsync(
            PartAService(jev, Flags()), manifest, KeyText, "admin-1", "p1", CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.All(result, f => Assert.StartsWith(JevExtractionVerify.FlagPrefix, f));
        Assert.Contains("Q1", result[0]);
        Assert.Contains("Q2", result[1]);
        Assert.Equal(before, JsonSerializer.Serialize(manifest));

        var items = jev.Calls.Single().Request.StateJson!.Value.GetProperty("items");
        Assert.Equal("- allergy to ____ noted", items[0].GetProperty("item_text").GetString());
        Assert.Equal("- takes ____ daily", items[1].GetProperty("item_text").GetString());
        Assert.Equal("penicillin", items[0].GetProperty("extracted_answer").GetString());
    }

    [Fact]
    public async Task PartA_FlagOff_NoKey_Unavailable_ReturnNoFlags()
    {
        var manifest = PartAManifest();

        var off = Responding(Supports);
        Assert.Empty(await InvokeFlagsAsync(
            PartAService(off, Flags(verify: false)), manifest, KeyText, "a", "p1", CancellationToken.None));
        Assert.Empty(off.Calls);

        var noKey = Responding(Supports);
        Assert.Empty(await InvokeFlagsAsync(
            PartAService(noKey, Flags()), manifest, null, "a", "p1", CancellationToken.None));
        Assert.Empty(noKey.Calls);

        var down = new FakeJudgments((_, _, _) => Task.FromResult(JevJudgmentResult.Unavailable("jev_timeout")));
        Assert.Empty(await InvokeFlagsAsync(
            PartAService(down, Flags()), manifest, KeyText, "a", "p1", CancellationToken.None));

        var boom = new FakeJudgments((_, _, _) => throw new InvalidOperationException("boom"));
        Assert.Empty(await InvokeFlagsAsync(
            PartAService(boom, Flags()), manifest, KeyText, "a", "p1", CancellationToken.None));

        Assert.Empty(await InvokeFlagsAsync(
            PartAService(null, Flags()), manifest, KeyText, "a", "p1", CancellationToken.None));
    }

    [Fact]
    public async Task PartBC_Returns_OnlyTheJevFlags()
    {
        var jev = Responding(
            reference => reference == "Q26" ? (JevExtractionVerify.Contradicted, 0.9) : (JevExtractionVerify.Supported, 0.95));
        IReadOnlyList<ListeningPartBCAnswer> answers = new[]
        {
            new ListeningPartBCAnswer(25, "B", null, "What is the speaker's main point?", "Alpha", "Beta", "Gamma"),
            new ListeningPartBCAnswer(26, "A", null, "Why did the manager call?", "One", "Two", "Three"),
        };

        var result = await InvokeFlagsAsync(
            PartBCService(jev, Flags()), answers, "25. B
26. C", "admin-1", "p1", "B", CancellationToken.None);

        var flag = Assert.Single(result);
        Assert.StartsWith(JevExtractionVerify.FlagPrefix, flag);
        Assert.Contains("Q26", flag);
        Assert.Equal("B", answers[0].CorrectAnswer);
        Assert.Equal("A", answers[1].CorrectAnswer);

        var item = jev.Calls.Single().Request.StateJson!.Value.GetProperty("items")[0];
        Assert.Equal("What is the speaker's main point?", item.GetProperty("item_text").GetString());
        Assert.Equal("A: Alpha
B: Beta
C: Gamma", item.GetProperty("options").GetString());
        Assert.Equal("p1:B", jev.Calls.Single().Call.ResourceId);
    }

    [Fact]
    public async Task PartBC_FlagOff_MakesNoCall_AndReturnsNoFlags()
    {
        var jev = Responding(Supports);
        IReadOnlyList<ListeningPartBCAnswer> answers = new[] { new ListeningPartBCAnswer(25, "B", null, "Stem?", "a", "b", "c") };

        var result = await InvokeFlagsAsync(
            PartBCService(jev, Flags(verify: false)), answers, "25. B", "admin-1", "p1", "B", CancellationToken.None);

        Assert.Empty(result);
        Assert.Empty(jev.Calls);
    }
}
