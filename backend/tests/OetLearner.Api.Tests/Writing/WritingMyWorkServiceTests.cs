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
        Assert.Equal(
            new[] { $"draft:{practiceDraft}", $"draft:{revisionDraft}", $"submission:{freshGrading}", $"submission:{staleGrading}",
                $"submission:{revision}", $"submission:{failed}", $"submission:{graded}", $"submission:{queued}" },
            result.Items.Select(i => i.Key));
        var byKey = result.Items.ToDictionary(i => i.Key);

        var draft = byKey[$"draft:{practiceDraft}"];
        Assert.Equal(("draft", "active", "Task B", "LT-UR", 5, "writing", (int?)1200), (draft.State, draft.RawStatus, draft.Title, draft.LetterType, draft.WordCount, draft.Phase, draft.WritingSecondsRemaining));
        Assert.Equal(Action("resume", $"/writing/practice/session/{TaskB}"), Assert.Single(draft.Actions));
        // A revision draft resumes on the latest graded letter of its task.
        Assert.Equal(Action("resume", $"/writing/submissions/{revision}/revise"), Assert.Single(byKey[$"draft:{revisionDraft}"].Actions));
        Assert.True(byKey[$"draft:{revisionDraft}"].IsRevision);

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
        // Queued is always live grading, however old.
        Assert.Equal(("grading", false, "queued"), (byKey[$"submission:{queued}"].State, byKey[$"submission:{queued}"].CanRetry, byKey[$"submission:{queued}"].RawStatus));

        // No letter text anywhere in the payload.
        Assert.DoesNotContain("SECRET LETTER", JsonSerializer.Serialize(result));
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

    [Theory]
    [InlineData("graded", null, "graded", false)]
    [InlineData("failed", null, "failed", true)]
    [InlineData("queued", null, "grading", false)]
    [InlineData("preflight", null, "grading", false)]
    [InlineData("grading", 24, "grading", false)]
    [InlineData("grading", 25, "failed", true)]
    public void ComputeRetryState_UsesTheSharedLease(string status, int? claimedMinutesAgo, string state, bool canRetry)
    {
        var claimed = claimedMinutesAgo is { } m ? Now.AddMinutes(-m) : (DateTimeOffset?)null;
        Assert.Equal((state, canRetry, false), WritingMyWorkService.ComputeRetryState(status, claimed, Now));
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
