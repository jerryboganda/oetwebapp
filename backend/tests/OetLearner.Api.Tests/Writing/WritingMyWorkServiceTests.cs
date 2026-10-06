using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Writing;

/// <summary>WAI-06 Post Submissions list: drafts + submissions in one list with
/// server-computed states and actions, keyset paging, learner isolation and
/// no letter text.</summary>
public sealed class WritingMyWorkServiceTests
{
    private const string User = "my-work-user";
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TaskA = Guid.Parse("a0000000-0000-4000-8000-000000000001");
    private static readonly Guid TaskB = Guid.Parse("b0000000-0000-4000-8000-000000000002");

    [Fact]
    public async Task List_UnionsDraftsAndSubmissions_WithStatesActionsAndTitles()
    {
        await using var db = await SeedAsync();
        var graded = AddSubmission(db, TaskA, WritingSubmissionStatuses.Graded, minutesAgo: 50);
        var failed = AddSubmission(db, TaskB, WritingSubmissionStatuses.Failed, minutesAgo: 40);
        var queued = AddSubmission(db, TaskA, WritingSubmissionStatuses.Queued, minutesAgo: 600);
        var freshGrading = AddSubmission(db, TaskB, WritingSubmissionStatuses.Grading, minutesAgo: 20, claimedMinutesAgo: 5);
        var staleGrading = AddSubmission(db, TaskA, WritingSubmissionStatuses.Grading, minutesAgo: 30, claimedMinutesAgo: 26);
        var revision = AddSubmission(db, TaskA, WritingSubmissionStatuses.Graded, minutesAgo: 35, isRevision: true);
        var held = AddSubmission(db, TaskB, WritingSubmissionStatuses.Graded, minutesAgo: 5);                // graded inside the 15-minute release window
        AddSubmission(db, TaskA, WritingSubmissionStatuses.Graded, minutesAgo: 1, mode: "mock");             // mock: never listed
        AddSubmission(db, TaskA, WritingSubmissionStatuses.Graded, minutesAgo: 1, userId: "someone-else");   // another learner
        var practiceDraft = AddDraft(db, TaskB, "practice", minutesAgo: 10);
        var revisionDraft = AddDraft(db, TaskA, "revision", minutesAgo: 15);
        AddDraft(db, TaskA, "mock", minutesAgo: 2);
        AddDraft(db, TaskA, "practice", minutesAgo: 3, status: WritingDraftStatuses.Submitted);
        AddDraft(db, TaskA, "practice", minutesAgo: 4, userId: "someone-else");
        db.FreeSampleUses.Add(new FreeSampleUse
        {
            Id = "fsu-1", ClaimId = "fsc-1", UserId = User, Subtest = "writing",
            ResourceKind = FreeSampleUse.KindWritingSubmission, ResourceId = graded.ToString("N"), CreatedAt = Now,
        });
        await db.SaveChangesAsync();

        var result = await Service(db).ListAsync(User, limit: null, before: null, default);

        Assert.False(result.HasMore);
        Assert.Equal(Now, result.ServerNow);
        // Revise & Resubmit is retired: a leftover revision draft is never listed or resumed.
        Assert.Equal(
            new[] { $"submission:{held}", $"draft:{practiceDraft}", $"submission:{freshGrading}", $"submission:{staleGrading}",
                $"submission:{revision}", $"submission:{failed}", $"submission:{graded}", $"submission:{queued}" },
            result.Items.Select(i => i.Key));
        Assert.DoesNotContain(result.Items, i => i.Key == $"draft:{revisionDraft}");
        var byKey = result.Items.ToDictionary(i => i.Key);

        var draft = byKey[$"draft:{practiceDraft}"];
        Assert.Equal(("draft", "active", "Task B", "LT-UR", 5, "writing", (int?)1200), (draft.State, draft.RawStatus, draft.Title, draft.LetterType, draft.WordCount, draft.Phase, draft.WritingSecondsRemaining));
        Assert.Equal(Action("resume", $"/writing/practice/session/{TaskB}"), Assert.Single(draft.Actions));
        Assert.Null(draft.ReleaseState);

        // Graded 5 minutes ago: still "being assessed" - never open_result, countdown anchored to SubmittedAt + 15 min.
        var heldItem = byKey[$"submission:{held}"];
        Assert.Equal(("grading", "grading", false, "held"), (heldItem.State, heldItem.RawStatus, heldItem.CanRetry, heldItem.ReleaseState));
        Assert.Equal(Now.AddMinutes(10), heldItem.ReleaseAt);
        Assert.Equal(new[] { Action("wait", $"/writing/submissions/{held}/grading"), Action("view_letter", $"/writing/submissions/{held}") }, heldItem.Actions);

        var gradedItem = byKey[$"submission:{graded}"];
        Assert.Equal(("graded", false, true), (gradedItem.State, gradedItem.CanRetry, gradedItem.IsFreeSample));
        Assert.Equal(new[] { Action("open_result", $"/writing/submissions/{graded}/results"), Action("view_letter", $"/writing/submissions/{graded}") }, gradedItem.Actions);
        Assert.False(byKey[$"submission:{revision}"].IsFreeSample);

        Assert.Equal(("failed", true), (byKey[$"submission:{failed}"].State, byKey[$"submission:{failed}"].CanRetry));
        Assert.Equal(Action("retry", $"/writing/submissions/{failed}/grading"), byKey[$"submission:{failed}"].Actions[0]);
        // An orphaned claim past the 25-minute lease is recoverable; a live one is not.
        Assert.Equal(("failed", true, "grading"), (byKey[$"submission:{staleGrading}"].State, byKey[$"submission:{staleGrading}"].CanRetry, byKey[$"submission:{staleGrading}"].RawStatus));
        Assert.Equal(("grading", false), (byKey[$"submission:{freshGrading}"].State, byKey[$"submission:{freshGrading}"].CanRetry));
        Assert.Equal(Action("wait", $"/writing/submissions/{freshGrading}/grading"), byKey[$"submission:{freshGrading}"].Actions[0]);
        // Queued reads as live grading, and offers Retry once nothing picked it up within the
        // 25-minute lease (shared rule, WritingGradeRecovery.IsStaleQueued): this one is 10 h old.
        Assert.Equal(("grading", true, "queued"), (byKey[$"submission:{queued}"].State, byKey[$"submission:{queued}"].CanRetry, byKey[$"submission:{queued}"].RawStatus));
        Assert.Equal(Action("retry", $"/writing/submissions/{queued}/grading"), byKey[$"submission:{queued}"].Actions[0]);

        // No letter text anywhere in the payload.
        Assert.DoesNotContain("SECRET LETTER", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task List_AnAllowlistedAccount_IsNeverHeld()
    {
        await using var db = await SeedAsync();
        db.Users.Add(new LearnerUser
        {
            Id = User, DisplayName = "Owner", Email = "drahmedhesham.work@gmail.com", AccountStatus = "active",
            CreatedAt = Now, LastActiveAt = Now,
        });
        var justGraded = AddSubmission(db, TaskA, WritingSubmissionStatuses.Graded, minutesAgo: 1);
        await db.SaveChangesAsync();

        var item = Assert.Single((await Service(db).ListAsync(User, limit: null, before: null, default)).Items);

        Assert.Equal(($"submission:{justGraded}", "graded", "released", (DateTimeOffset?)null), (item.Key, item.State, item.ReleaseState, item.ReleaseAt));
        Assert.Equal("open_result", item.Actions[0].Kind);
    }

    [Fact]
    public async Task List_PagesWithTheLastItemsActivityTime()
    {
        await using var db = await SeedAsync();
        for (var i = 1; i <= 5; i++) AddSubmission(db, TaskA, WritingSubmissionStatuses.Graded, minutesAgo: i * 10);
        AddDraft(db, TaskB, "practice", minutesAgo: 25);
        await db.SaveChangesAsync();
        var service = Service(db);

        var first = await service.ListAsync(User, limit: 4, before: null, default);
        Assert.True(first.HasMore);
        Assert.Equal(4, first.Items.Count);
        var second = await service.ListAsync(User, limit: 4, before: first.Items[^1].LastActivityAt, default);
        Assert.False(second.HasMore);

        var all = first.Items.Concat(second.Items).ToList();
        Assert.Equal(6, all.Select(i => i.Key).Distinct().Count());
        Assert.Equal(all.Select(i => i.LastActivityAt).OrderDescending(), all.Select(i => i.LastActivityAt));
        Assert.Equal(6, (await service.ListAsync(User, limit: 0, before: null, default)).Items.Count); // 0 falls back to the default (20)
    }

    // status, claimedMinutesAgo, submittedMinutesAgo, nextAutoRetryInMinutes (negative = due in the past),
    // autoRetryCount, failureRetryable, expected (state, canRetry, autoRetrying)
    [Theory]
    [InlineData("graded", null, 1, null, 0, null, "graded", false, false)]
    [InlineData("failed", null, 1, null, 0, null, "failed", true, false)]     // legacy failed row (null verdict) = retryable
    [InlineData("failed", null, 1, null, 4, true, "failed", true, false)]     // auto-retries spent, still retryable by hand
    [InlineData("failed", null, 1, null, 0, false, "failed", false, false)]   // final failure (task not ready, manual review)
    [InlineData("queued", null, 1, null, 0, null, "grading", false, false)]
    [InlineData("queued", null, 1, -2, 2, true, "grading", false, true)]      // server re-queued it: live grading, auto-retrying
    [InlineData("queued", null, 40, null, 0, null, "grading", true, false)]   // nobody picked it up within the lease: Retry
    [InlineData("preflight", null, 1, null, 0, null, "grading", false, false)]
    [InlineData("grading", 24, 30, null, 0, null, "grading", false, false)]
    [InlineData("grading", 25, 30, null, 0, null, "failed", true, false)]
    public void ComputeRetryState_FollowsTheGradingStatusRules(
        string status, int? claimedMinutesAgo, int submittedMinutesAgo, int? nextAutoRetryMinutes,
        int autoRetryCount, bool? failureRetryable, string state, bool canRetry, bool autoRetrying)
    {
        var claimed = claimedMinutesAgo is { } m ? Now.AddMinutes(-m) : (DateTimeOffset?)null;
        var next = nextAutoRetryMinutes is { } n ? Now.AddMinutes(n) : (DateTimeOffset?)null;
        Assert.Equal(
            (state, canRetry, autoRetrying),
            WritingMyWorkService.ComputeRetryState(
                status, claimed, Now.AddMinutes(-submittedMinutesAgo), next, autoRetryCount, failureRetryable, Now));
    }

    private static WritingMyWorkActionResponse Action(string kind, string href) => new(kind, href);

    private static WritingMyWorkService Service(LearnerDbContext db) => new(db, new FixedClock());

    private static async Task<LearnerDbContext> SeedAsync()
    {
        var db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        foreach (var (id, title, letterType) in new[] { (TaskA, "Task A", "LT-RR"), (TaskB, "Task B", "LT-UR") })
        {
            db.WritingScenarios.Add(new WritingScenario
            {
                Id = id, Title = title, LetterType = letterType, Profession = "medicine", Status = "published",
                AuthorId = "admin-1", CreatedAt = Now, UpdatedAt = Now,
            });
        }
        await db.SaveChangesAsync();
        return db;
    }

    private static Guid AddSubmission(
        LearnerDbContext db, Guid scenarioId, string status, int minutesAgo, int? claimedMinutesAgo = null,
        bool isRevision = false, string mode = "practice", string userId = User)
    {
        var id = Guid.NewGuid();
        db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = id, UserId = userId, ScenarioId = scenarioId, Mode = mode, IsRevision = isRevision,
            LetterContent = "SECRET LETTER", LetterContentHash = id.ToString("N"), WordCount = 7, Status = status,
            ClaimedAt = claimedMinutesAgo is { } c ? Now.AddMinutes(-c) : null,
            StartedAt = Now.AddMinutes(-minutesAgo), SubmittedAt = Now.AddMinutes(-minutesAgo), CreatedAt = Now.AddMinutes(-minutesAgo),
        });
        return id;
    }

    private static Guid AddDraft(
        LearnerDbContext db, Guid scenarioId, string mode, int minutesAgo, string status = WritingDraftStatuses.Active, string userId = User)
    {
        var id = Guid.NewGuid();
        db.WritingDraftsV2.Add(new WritingDraftV2
        {
            Id = id, UserId = userId, ScenarioId = scenarioId, Mode = mode, Content = "SECRET LETTER draft text",
            WordCount = 5, Status = status, Phase = "writing", ReadingSecondsRemaining = 0, WritingSecondsRemaining = 1200,
            LastSavedAt = Now.AddMinutes(-minutesAgo), CreatedAt = Now.AddMinutes(-minutesAgo), AttemptStartedAt = Now.AddMinutes(-minutesAgo),
        });
        return id;
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
