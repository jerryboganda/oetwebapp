using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai.TypeSafe;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// The Jev check on a generated live-voice projection is a second opinion on
/// deterministic content, so a disabled / unavailable / slow Jev must never
/// stop a learner from starting voice. Only an explicit negative verdict
/// (Noul below 0.5) asks for owner input.
/// </summary>
public sealed class JevLiveVoiceContentReadinessTests
{
    private const string Grounded = "jev_live_voice_projection_grounded";
    private const string Consistent = "jev_live_voice_projection_consistent";

    private sealed class FakeJudgments(Func<CancellationToken, Task<JevJudgmentResult>> respond) : ITypeSafeJudgmentService
    {
        public int Calls { get; private set; }

        public Task<JevJudgmentResult> AskAsync(JevJudgmentRequest request, JevCallMetadata call, CancellationToken ct)
        {
            Calls++;
            return respond(ct);
        }
    }

    private static FakeJudgments Returning(JevJudgmentResult result) => new(_ => Task.FromResult(result));

    private static JevJudgmentResult Verdict(double grounded, double consistent) =>
        new(JevCallStatus.Ok, "jev-1.13.0",
            new Dictionary<string, JevAnswer>
            {
                [Grounded] = new(JevQuestionKind.Noul, new JevNoulAnswer(grounded), null, null),
                [Consistent] = new(JevQuestionKind.Noul, new JevNoulAnswer(consistent), null, null),
            }, 100, 5, null);

    private static LearnerDbContext NewDb() => new(new DbContextOptionsBuilder<LearnerDbContext>()
        .UseInMemoryDatabase($"live-voice-readiness-{Guid.NewGuid():N}")
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private static RolePlayCard NewCard() => new()
    {
        Id = $"rpc-{Guid.NewGuid():N}",
        ContentItemId = "ci-1",
        ProfessionId = "medicine",
        ScenarioTitle = "Post-operative pain review",
        Setting = "Hospital ward",
        CandidateRole = "Doctor",
        InterlocutorRole = "Patient",
        Background = "Day two after knee surgery.",
        Task1 = "Take a history",
        PatientEmotion = "anxious",
        CommunicationGoal = "Reassure and plan analgesia",
        ClinicalTopic = "pain management",
        Difficulty = "core",
        CriteriaFocusJson = "[]",
        Disclaimer = "Practice estimate only.",
        Status = ContentStatus.Published,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static LiveVoiceContentReadinessService Service(LearnerDbContext db, ITypeSafeJudgmentService? jev) =>
        new(db, jev, NullLogger<LiveVoiceContentReadinessService>.Instance);

    private static Task<InterlocutorScript> StoredScriptAsync(LearnerDbContext db, RolePlayCard card) =>
        db.InterlocutorScripts.AsNoTracking().SingleAsync(s => s.RolePlayCardId == card.Id);

    [Theory]
    [InlineData("disabled", "jev_disabled")]
    [InlineData("unavailable", "jev_unavailable")]
    [InlineData("ok_without_verdict", "jev_unavailable")]
    public async Task Prepare_WhenJevGivesNoJudgment_DoesNotBlockTheLearner(string outcome, string expectedStatus)
    {
        await using var db = NewDb();
        var card = NewCard();
        var result = outcome switch
        {
            "disabled" => JevJudgmentResult.Disabled("typesafe_disabled"),
            "unavailable" => JevJudgmentResult.Unavailable("jev_unavailable"),
            _ => new JevJudgmentResult(JevCallStatus.Ok, "jev-1.13.0", new Dictionary<string, JevAnswer>(), 100, 5, null),
        };

        var readiness = await Service(db, Returning(result)).PrepareAsync(card, null, CancellationToken.None);

        Assert.False(readiness.NeedsOwnerInput);
        Assert.True(readiness.Generated);
        Assert.Equal(expectedStatus, readiness.JevValidationStatus);
        var stored = await StoredScriptAsync(db, card);
        Assert.False(stored.NeedsOwnerInput);
        Assert.Equal(expectedStatus, stored.JevValidationStatus);
    }

    [Fact]
    public async Task Prepare_WithoutAJevService_DoesNotBlockTheLearner()
    {
        await using var db = NewDb();

        var readiness = await Service(db, null).PrepareAsync(NewCard(), null, CancellationToken.None);

        Assert.False(readiness.NeedsOwnerInput);
        Assert.Equal("jev_unavailable", readiness.JevValidationStatus);
    }

    [Fact]
    public async Task Prepare_WhenJevIsSlow_TimesOutWithoutBlockingTheLearner()
    {
        await using var db = NewDb();
        var slow = new FakeJudgments(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return JevJudgmentResult.Disabled("unreachable");
        });

        var readiness = await Service(db, slow).PrepareAsync(NewCard(), null, CancellationToken.None);

        Assert.False(readiness.NeedsOwnerInput);
        Assert.Equal("jev_pending", readiness.JevValidationStatus);
    }

    [Fact]
    public async Task Prepare_WhenCallerCancels_StillPropagatesTheCancellation()
    {
        await using var db = NewDb();
        using var cts = new CancellationTokenSource();
        var slow = new FakeJudgments(async ct =>
        {
            cts.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return JevJudgmentResult.Disabled("unreachable");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service(db, slow).PrepareAsync(NewCard(), null, cts.Token));
    }

    [Fact]
    public async Task Prepare_WhenJevValidates_MarksTheProjectionValidated()
    {
        await using var db = NewDb();
        var card = NewCard();

        var readiness = await Service(db, Returning(Verdict(0.9, 0.9))).PrepareAsync(card, null, CancellationToken.None);

        Assert.False(readiness.NeedsOwnerInput);
        Assert.Equal("jev_validated", readiness.JevValidationStatus);
    }

    [Theory]
    [InlineData(0.1, 0.9)]
    [InlineData(0.9, 0.2)]
    [InlineData(0.49, 0.49)]
    public async Task Prepare_OnAnExplicitNegativeVerdict_AsksForOwnerInput(double grounded, double consistent)
    {
        await using var db = NewDb();
        var card = NewCard();

        var readiness = await Service(db, Returning(Verdict(grounded, consistent))).PrepareAsync(card, null, CancellationToken.None);

        Assert.True(readiness.NeedsOwnerInput);
        Assert.Equal("jev_owner_input_required", readiness.JevValidationStatus);
        Assert.True((await StoredScriptAsync(db, card)).NeedsOwnerInput);
    }

    [Fact]
    public async Task SkippedProjection_IsReusedWithoutRewritingOrReAskingJev()
    {
        await using var db = NewDb();
        var card = NewCard();
        var jev = Returning(JevJudgmentResult.Unavailable("jev_unavailable"));
        var service = Service(db, jev);
        await service.PrepareAsync(card, null, CancellationToken.None);
        db.ChangeTracker.Clear(); // production loads the stored script AsNoTracking in a fresh scope
        var stored = await StoredScriptAsync(db, card);

        var again = await service.PrepareAsync(card, stored, CancellationToken.None);

        Assert.NotNull(LiveVoiceContentReadinessService.TryResolveExisting(card, stored));
        Assert.False(again.NeedsOwnerInput);
        Assert.Equal(1, jev.Calls);
    }

    [Fact]
    public async Task OwnerInputRequiredProjection_IsRegeneratedAndReJudgedOnTheNextCall()
    {
        await using var db = NewDb();
        var card = NewCard();
        var jev = Returning(Verdict(0.1, 0.9));
        var service = Service(db, jev);
        await service.PrepareAsync(card, null, CancellationToken.None);
        db.ChangeTracker.Clear(); // production loads the stored script AsNoTracking in a fresh scope
        var stored = await StoredScriptAsync(db, card);

        Assert.Null(LiveVoiceContentReadinessService.TryResolveExisting(card, stored));
        await service.PrepareAsync(card, stored, CancellationToken.None);

        Assert.Equal(2, jev.Calls);
    }

    [Fact]
    public async Task AuthoredScript_NeedingOwnerInput_StillBlocks()
    {
        await using var db = NewDb();
        var card = NewCard();
        var authored = new InterlocutorScript
        {
            Id = "is-authored",
            RolePlayCardId = card.Id,
            ContentOrigin = "authored",
            NeedsOwnerInput = true,
            OpeningResponse = "Doctor, my knee hurts.",
            HiddenInformation = "",
            ResistanceLevel = ResistanceLevel.Low,
            ClosingCue = "Accept advice",
            EmotionalState = "anxious",
            LayLanguageTriggersJson = "[]",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var jev = Returning(Verdict(0.9, 0.9));

        var readiness = await Service(db, jev).PrepareAsync(card, authored, CancellationToken.None);

        Assert.True(readiness.NeedsOwnerInput);
        Assert.Equal(0, jev.Calls);
    }
}
