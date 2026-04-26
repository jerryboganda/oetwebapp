using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using Xunit;

namespace OetLearner.Api.Tests;

public class MarketplaceServiceTests
{
    private static (LearnerDbContext db, MarketplaceService svc) Build()
    {
        var opts = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(opts);
        return (db, new MarketplaceService(db));
    }

    private static dynamic AsDynamic(object o) => (dynamic)o;

    private static MarketplaceSubmissionRequest MakeReq(
        string subtest = "writing",
        string title = "My Submission",
        string? description = "Description",
        string? payload = null,
        string? contentType = null,
        string? professionId = null,
        string? difficulty = null,
        string? tags = null,
        string? examFamily = null) => new(
            ExamFamilyCode: examFamily,
            SubtestCode: subtest,
            Title: title,
            Description: description,
            ContentPayloadJson: payload,
            ContentType: contentType,
            ProfessionId: professionId,
            Difficulty: difficulty,
            Tags: tags);

    // ── Contributor profile ─────────────────────────────────────────

    [Fact]
    public async Task GetOrCreateContributorProfileAsync_creates_new_profile_for_first_call()
    {
        var (db, svc) = Build();
        var profile = AsDynamic(
            await svc.GetOrCreateContributorProfileAsync("u1", CancellationToken.None));
        Assert.Equal("u1", (string)profile.userId);
        Assert.Equal("Contributor", (string)profile.displayName);
        Assert.Equal("unverified", (string)profile.verificationStatus);
        Assert.Equal(1, db.ContentContributors.Count(c => c.UserId == "u1"));
    }

    [Fact]
    public async Task GetOrCreateContributorProfileAsync_returns_existing_profile_on_second_call()
    {
        var (_, svc) = Build();
        var first = AsDynamic(await svc.GetOrCreateContributorProfileAsync("u1", CancellationToken.None));
        var second = AsDynamic(await svc.GetOrCreateContributorProfileAsync("u1", CancellationToken.None));
        Assert.Equal((string)first.id, (string)second.id);
    }

    [Fact]
    public async Task UpdateContributorProfileAsync_throws_when_no_profile()
    {
        var (_, svc) = Build();
        await Assert.ThrowsAsync<ApiException>(
            () => svc.UpdateContributorProfileAsync(
                "u1", new MarketplaceProfileUpdateRequest("New Name", null), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateContributorProfileAsync_updates_display_name_and_bio()
    {
        var (_, svc) = Build();
        await svc.GetOrCreateContributorProfileAsync("u1", CancellationToken.None);
        var updated = AsDynamic(await svc.UpdateContributorProfileAsync(
            "u1", new MarketplaceProfileUpdateRequest("Dr. Smith", "Senior nurse"), CancellationToken.None));
        Assert.Equal("Dr. Smith", (string)updated.displayName);
        Assert.Equal("Senior nurse", (string)updated.bio);
    }

    [Fact]
    public async Task UpdateContributorProfileAsync_leaves_unspecified_fields_unchanged()
    {
        var (_, svc) = Build();
        await svc.GetOrCreateContributorProfileAsync("u1", CancellationToken.None);
        await svc.UpdateContributorProfileAsync(
            "u1", new MarketplaceProfileUpdateRequest("Original", "Bio v1"), CancellationToken.None);
        var updated = AsDynamic(await svc.UpdateContributorProfileAsync(
            "u1", new MarketplaceProfileUpdateRequest(null, null), CancellationToken.None));
        Assert.Equal("Original", (string)updated.displayName);
        Assert.Equal("Bio v1", (string)updated.bio);
    }

    // ── Submissions ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateSubmissionAsync_throws_when_user_has_no_profile()
    {
        var (_, svc) = Build();
        await Assert.ThrowsAsync<ApiException>(
            () => svc.CreateSubmissionAsync("u1", MakeReq(), CancellationToken.None));
    }

    [Fact]
    public async Task CreateSubmissionAsync_increments_submission_count_and_defaults_fields()
    {
        var (db, svc) = Build();
        await svc.GetOrCreateContributorProfileAsync("u1", CancellationToken.None);
        var s = AsDynamic(await svc.CreateSubmissionAsync("u1", MakeReq(), CancellationToken.None));
        Assert.Equal("pending", (string)s.status);
        Assert.Equal("oet", (string)s.examFamilyCode);
        Assert.Equal("practice_task", (string)s.contentType);
        Assert.Equal("medium", (string)s.difficulty);
        var contributor = await db.ContentContributors.FirstAsync(c => c.UserId == "u1");
        Assert.Equal(1, contributor.SubmissionCount);
    }

    [Fact]
    public async Task CreateSubmissionAsync_uses_request_overrides_when_provided()
    {
        var (_, svc) = Build();
        await svc.GetOrCreateContributorProfileAsync("u1", CancellationToken.None);
        var req = MakeReq(
            examFamily: "ielts",
            payload: """{"q":"hi"}""",
            contentType: "case_study",
            difficulty: "hard",
            tags: "tag1,tag2");
        var s = AsDynamic(await svc.CreateSubmissionAsync("u1", req, CancellationToken.None));
        Assert.Equal("ielts", (string)s.examFamilyCode);
        Assert.Equal("case_study", (string)s.contentType);
        Assert.Equal("hard", (string)s.difficulty);
        Assert.Equal("tag1,tag2", (string)s.tags);
    }

    [Fact]
    public async Task GetMySubmissionsAsync_returns_empty_when_no_profile()
    {
        var (_, svc) = Build();
        dynamic page = await svc.GetMySubmissionsAsync("u1", 1, 10, CancellationToken.None);
        Assert.Equal(0, (int)page.total);
    }

    [Fact]
    public async Task GetMySubmissionsAsync_returns_only_users_submissions()
    {
        var (_, svc) = Build();
        await svc.GetOrCreateContributorProfileAsync("u1", CancellationToken.None);
        await svc.GetOrCreateContributorProfileAsync("u2", CancellationToken.None);
        await svc.CreateSubmissionAsync("u1", MakeReq(title: "U1-A"), CancellationToken.None);
        await svc.CreateSubmissionAsync("u1", MakeReq(title: "U1-B"), CancellationToken.None);
        await svc.CreateSubmissionAsync("u2", MakeReq(title: "U2-A"), CancellationToken.None);

        dynamic page = await svc.GetMySubmissionsAsync("u1", 1, 10, CancellationToken.None);
        Assert.Equal(2, (int)page.total);
    }

    [Fact]
    public async Task GetSubmissionAsync_throws_for_unknown_id()
    {
        var (_, svc) = Build();
        await Assert.ThrowsAsync<ApiException>(
            () => svc.GetSubmissionAsync("u1", "missing", CancellationToken.None));
    }

    [Fact]
    public async Task GetSubmissionAsync_returns_owners_pending_submission()
    {
        var (_, svc) = Build();
        await svc.GetOrCreateContributorProfileAsync("u1", CancellationToken.None);
        var created = AsDynamic(await svc.CreateSubmissionAsync(
            "u1", MakeReq(title: "Mine"), CancellationToken.None));
        dynamic got = await svc.GetSubmissionAsync("u1", (string)created.id, CancellationToken.None);
        Assert.Equal((string)created.id, (string)got.id);
    }

    [Fact]
    public async Task GetSubmissionAsync_hides_others_pending_submission()
    {
        var (_, svc) = Build();
        await svc.GetOrCreateContributorProfileAsync("u1", CancellationToken.None);
        await svc.GetOrCreateContributorProfileAsync("u2", CancellationToken.None);
        var created = AsDynamic(await svc.CreateSubmissionAsync(
            "u1", MakeReq(), CancellationToken.None));
        await Assert.ThrowsAsync<ApiException>(
            () => svc.GetSubmissionAsync("u2", (string)created.id, CancellationToken.None));
    }

    [Fact]
    public async Task GetSubmissionAsync_allows_viewing_others_approved_submission()
    {
        var (db, svc) = Build();
        await svc.GetOrCreateContributorProfileAsync("u1", CancellationToken.None);
        await svc.GetOrCreateContributorProfileAsync("u2", CancellationToken.None);
        var created = AsDynamic(await svc.CreateSubmissionAsync(
            "u1", MakeReq(), CancellationToken.None));
        // Manually approve.
        var s = await db.ContentSubmissions.FindAsync((string)created.id);
        s!.Status = "approved";
        await db.SaveChangesAsync();

        dynamic got = await svc.GetSubmissionAsync("u2", (string)created.id, CancellationToken.None);
        Assert.Equal("approved", (string)got.status);
    }

    // ── Browse ──────────────────────────────────────────────────────

    [Fact]
    public async Task BrowseContentAsync_returns_only_approved_submissions()
    {
        var (db, svc) = Build();
        await svc.GetOrCreateContributorProfileAsync("u1", CancellationToken.None);
        var pending = AsDynamic(await svc.CreateSubmissionAsync("u1", MakeReq(title: "Pending"), CancellationToken.None));
        var approvedReq = AsDynamic(await svc.CreateSubmissionAsync("u1", MakeReq(title: "Approved"), CancellationToken.None));

        var s = await db.ContentSubmissions.FindAsync((string)approvedReq.id);
        s!.Status = "approved";
        s.ApprovedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        dynamic page = await svc.BrowseContentAsync(null, null, null, 1, 10, CancellationToken.None);
        Assert.Equal(1, (int)page.total);
        _ = pending; // silence unused
    }

    [Fact]
    public async Task BrowseContentAsync_filters_by_search_substring_in_title()
    {
        var (db, svc) = Build();
        await svc.GetOrCreateContributorProfileAsync("u1", CancellationToken.None);
        var a = AsDynamic(await svc.CreateSubmissionAsync("u1", MakeReq(title: "Cardiology rounds"), CancellationToken.None));
        var b = AsDynamic(await svc.CreateSubmissionAsync("u1", MakeReq(title: "Pediatric notes"), CancellationToken.None));
        foreach (var id in new[] { (string)a.id, (string)b.id })
        {
            var s = await db.ContentSubmissions.FindAsync(id);
            s!.Status = "approved";
        }
        await db.SaveChangesAsync();

        dynamic page = await svc.BrowseContentAsync(null, null, "Cardiology", 1, 10, CancellationToken.None);
        Assert.Equal(1, (int)page.total);
    }

    [Fact]
    public async Task BrowseContentAsync_filters_by_subtest_and_examTypeCode()
    {
        var (db, svc) = Build();
        await svc.GetOrCreateContributorProfileAsync("u1", CancellationToken.None);
        var a = AsDynamic(await svc.CreateSubmissionAsync("u1", MakeReq(subtest: "writing", examFamily: "oet"), CancellationToken.None));
        var b = AsDynamic(await svc.CreateSubmissionAsync("u1", MakeReq(subtest: "reading", examFamily: "oet"), CancellationToken.None));
        var c = AsDynamic(await svc.CreateSubmissionAsync("u1", MakeReq(subtest: "writing", examFamily: "ielts"), CancellationToken.None));
        foreach (var id in new[] { (string)a.id, (string)b.id, (string)c.id })
        {
            var s = await db.ContentSubmissions.FindAsync(id);
            s!.Status = "approved";
        }
        await db.SaveChangesAsync();

        dynamic page = await svc.BrowseContentAsync("oet", "writing", null, 1, 10, CancellationToken.None);
        Assert.Equal(1, (int)page.total);
    }

    // ── Admin review ────────────────────────────────────────────────

    [Fact]
    public async Task GetPendingSubmissionsAsync_returns_pending_and_in_review()
    {
        var (db, svc) = Build();
        await svc.GetOrCreateContributorProfileAsync("u1", CancellationToken.None);
        var a = AsDynamic(await svc.CreateSubmissionAsync("u1", MakeReq(), CancellationToken.None));
        var b = AsDynamic(await svc.CreateSubmissionAsync("u1", MakeReq(), CancellationToken.None));
        var c = AsDynamic(await svc.CreateSubmissionAsync("u1", MakeReq(), CancellationToken.None));

        var sB = await db.ContentSubmissions.FindAsync((string)b.id);
        sB!.Status = "in_review";
        var sC = await db.ContentSubmissions.FindAsync((string)c.id);
        sC!.Status = "approved";
        await db.SaveChangesAsync();

        dynamic page = await svc.GetPendingSubmissionsAsync(1, 10, CancellationToken.None);
        Assert.Equal(2, (int)page.total);
        _ = a;
    }

    [Fact]
    public async Task ReviewSubmissionAsync_throws_for_invalid_decision()
    {
        var (_, svc) = Build();
        await svc.GetOrCreateContributorProfileAsync("u1", CancellationToken.None);
        var s = AsDynamic(await svc.CreateSubmissionAsync("u1", MakeReq(), CancellationToken.None));
        await Assert.ThrowsAsync<ApiException>(
            () => svc.ReviewSubmissionAsync("admin", (string)s.id,
                new MarketplaceReviewRequest("maybe", null, false), CancellationToken.None));
    }

    [Fact]
    public async Task ReviewSubmissionAsync_throws_for_unknown_id()
    {
        var (_, svc) = Build();
        await Assert.ThrowsAsync<ApiException>(
            () => svc.ReviewSubmissionAsync("admin", "missing",
                new MarketplaceReviewRequest("approved", null, false), CancellationToken.None));
    }

    [Fact]
    public async Task ReviewSubmissionAsync_rejects_marks_status_and_records_reviewer()
    {
        var (db, svc) = Build();
        await svc.GetOrCreateContributorProfileAsync("u1", CancellationToken.None);
        var s = AsDynamic(await svc.CreateSubmissionAsync("u1", MakeReq(), CancellationToken.None));

        var result = AsDynamic(await svc.ReviewSubmissionAsync("admin1", (string)s.id,
            new MarketplaceReviewRequest("rejected", "low quality", false), CancellationToken.None));

        Assert.Equal("rejected", (string)result.status);
        Assert.Equal("admin1", (string)result.reviewedBy);
        Assert.Equal("low quality", (string)result.reviewNotes);
        var contributor = await db.ContentContributors.FirstAsync();
        Assert.Equal(0, contributor.ApprovedCount);
    }

    [Fact]
    public async Task ReviewSubmissionAsync_approves_increments_approved_count_and_sets_approved_at()
    {
        var (db, svc) = Build();
        await svc.GetOrCreateContributorProfileAsync("u1", CancellationToken.None);
        var s = AsDynamic(await svc.CreateSubmissionAsync("u1", MakeReq(), CancellationToken.None));

        var result = AsDynamic(await svc.ReviewSubmissionAsync("admin1", (string)s.id,
            new MarketplaceReviewRequest("approved", "great", false), CancellationToken.None));

        Assert.Equal("approved", (string)result.status);
        Assert.NotNull((object?)result.approvedAt);
        var contributor = await db.ContentContributors.FirstAsync();
        Assert.Equal(1, contributor.ApprovedCount);
    }

    [Fact]
    public async Task ReviewSubmissionAsync_create_content_item_path_attempts_to_persist_a_draft_item()
    {
        // The current source builds a ContentItem without PublishedRevisionId,
        // which the EF model marks as required. Under the InMemory provider,
        // SaveChanges therefore throws. We pin the observable behavior so a
        // future fix to the source surfaces here as a test update, not a
        // silent change.
        var (_, svc) = Build();
        await svc.GetOrCreateContributorProfileAsync("u1", CancellationToken.None);
        var s = AsDynamic(await svc.CreateSubmissionAsync(
            "u1", MakeReq(payload: """{"x":1}"""), CancellationToken.None));
        var submissionId = (string)s.id;

        await Assert.ThrowsAnyAsync<Exception>(() =>
            svc.ReviewSubmissionAsync("admin1", submissionId,
                new MarketplaceReviewRequest("approved", null, true), CancellationToken.None));
    }
}
