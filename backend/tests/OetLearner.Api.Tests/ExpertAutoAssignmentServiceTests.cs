using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Expert;

namespace OetLearner.Api.Tests;

public class ExpertAutoAssignmentServiceTests
{
    private sealed class FixedClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _utcNow = start;
        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private static (LearnerDbContext db, ExpertAutoAssignmentService svc, FixedClock clock, NoopNotifier notifier) Build(
        ExpertAutoAssignmentOptions? optionsOverride = null)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var clock = new FixedClock(new DateTimeOffset(2026, 5, 22, 12, 0, 0, TimeSpan.Zero));
        var runtimeSettings = TestRuntimeSettingsProvider.FromExpertAutoAssignmentOptions(optionsOverride ?? new ExpertAutoAssignmentOptions());
        var notifier = new NoopNotifier();
        var svc = new ExpertAutoAssignmentService(db, clock, notifier, NullLogger<ExpertAutoAssignmentService>.Instance, runtimeSettings);
        return (db, svc, clock, notifier);
    }

    private sealed class NoopNotifier : IExpertAssignmentNotifier
    {
        public List<(string ExpertId, string ReviewId)> Assigned { get; } = new();
        public List<(string ExpertId, string ReviewId, string Reason)> Released { get; } = new();

        public Task NotifyAssignedAsync(string expertUserId, string reviewRequestId, string? professionId, string turnaroundOption, DateTimeOffset slaDueAt, CancellationToken ct)
        {
            Assigned.Add((expertUserId, reviewRequestId));
            return Task.CompletedTask;
        }

        public Task NotifyReleasedAsync(string expertUserId, string reviewRequestId, string reason, DateTimeOffset slaDueAt, CancellationToken ct)
        {
            Released.Add((expertUserId, reviewRequestId, reason));
            return Task.CompletedTask;
        }
    }

    private static async Task SeedExpertAsync(LearnerDbContext db, string id, string? specialtiesJson = null)
    {
        db.ExpertUsers.Add(new ExpertUser
        {
            Id = id,
            AuthAccountId = $"auth-{id}",
            DisplayName = $"Expert {id}",
            Email = $"{id}@example.com",
            SpecialtiesJson = specialtiesJson ?? "[]",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<(string requestId, string attemptId)> SeedPendingWritingRequestAsync(
        LearnerDbContext db, string profession, string turnaround = "standard")
    {
        var paperId = $"paper-{Guid.NewGuid():N}";
        db.ContentPapers.Add(new ContentPaper
        {
            Id = paperId,
            SubtestCode = "writing",
            Title = "T",
            Slug = paperId,
            ProfessionId = profession,
            LetterType = "routine_referral",
            Status = ContentStatus.Published,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            PublishedRevisionId = paperId,
        });
        var attemptId = $"att-{Guid.NewGuid():N}";
        db.Attempts.Add(new Attempt
        {
            Id = attemptId,
            UserId = "learner-1",
            ContentId = paperId,
            SubtestCode = "writing",
            State = AttemptState.Submitted,
            StartedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            Context = "{}",
            Mode = "exam",
        });
        var reviewRequestId = $"rr-{Guid.NewGuid():N}";
        db.ReviewRequests.Add(new ReviewRequest
        {
            Id = reviewRequestId,
            AttemptId = attemptId,
            SubtestCode = "writing",
            State = ReviewRequestState.Submitted,
            TurnaroundOption = turnaround,
            PaymentSource = "wallet",
            PriceSnapshot = 25m,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return (reviewRequestId, attemptId);
    }

    [Fact]
    public async Task Auto_assigns_to_expert_with_lowest_active_load()
    {
        var (db, svc, _, _) = Build();
        await SeedExpertAsync(db, "expert-busy");
        await SeedExpertAsync(db, "expert-quiet");
        // Pre-load busy expert with two active assignments.
        for (var i = 0; i < 2; i++)
        {
            db.ExpertReviewAssignments.Add(new ExpertReviewAssignment
            {
                Id = $"era-existing-{i}",
                ReviewRequestId = $"rr-existing-{i}",
                AssignedReviewerId = "expert-busy",
                AssignedAt = DateTimeOffset.UtcNow,
                ClaimState = ExpertAssignmentState.Assigned,
            });
        }
        await db.SaveChangesAsync();

        var (newRequest, _) = await SeedPendingWritingRequestAsync(db, "medicine");
        var assigned = await svc.ProcessPendingAssignmentsAsync(default);

        Assert.Equal(1, assigned);
        var assignment = await db.ExpertReviewAssignments
            .Where(a => a.ReviewRequestId == newRequest)
            .FirstAsync();
        Assert.Equal("expert-quiet", assignment.AssignedReviewerId);
        Assert.Equal(ExpertAssignmentState.Assigned, assignment.ClaimState);
        Assert.Equal("auto_assign", assignment.ReasonCode);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Filters_by_profession_competency()
    {
        var (db, svc, _, _) = Build();
        await SeedExpertAsync(db, "expert-medicine", specialtiesJson: "[\"medicine\"]");
        await SeedExpertAsync(db, "expert-nursing", specialtiesJson: "[\"nursing\"]");

        var (req, _) = await SeedPendingWritingRequestAsync(db, "nursing");
        await svc.ProcessPendingAssignmentsAsync(default);
        var assignment = await db.ExpertReviewAssignments.FirstAsync(a => a.ReviewRequestId == req);
        Assert.Equal("expert-nursing", assignment.AssignedReviewerId);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Generalist_expert_picks_up_any_profession()
    {
        var (db, svc, _, _) = Build();
        // Empty specialties → generalist.
        await SeedExpertAsync(db, "expert-generalist", specialtiesJson: "[]");
        var (req, _) = await SeedPendingWritingRequestAsync(db, "podiatry");
        await svc.ProcessPendingAssignmentsAsync(default);
        var assignment = await db.ExpertReviewAssignments.FirstAsync(a => a.ReviewRequestId == req);
        Assert.Equal("expert-generalist", assignment.AssignedReviewerId);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Skips_when_no_eligible_expert_available()
    {
        var (db, svc, _, _) = Build();
        await SeedExpertAsync(db, "expert-medicine", specialtiesJson: "[\"medicine\"]");
        // Request needs nursing — medicine specialist is not eligible.
        await SeedPendingWritingRequestAsync(db, "nursing");
        var assigned = await svc.ProcessPendingAssignmentsAsync(default);
        Assert.Equal(0, assigned);
        Assert.Equal(0, await db.ExpertReviewAssignments.CountAsync());
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Idempotent_when_request_already_assigned()
    {
        var (db, svc, _, _) = Build();
        await SeedExpertAsync(db, "expert-1");
        var (req, _) = await SeedPendingWritingRequestAsync(db, "medicine");
        await svc.ProcessPendingAssignmentsAsync(default);
        // A second call must not create a second assignment for the same request.
        var second = await svc.ProcessPendingAssignmentsAsync(default);
        Assert.Equal(0, second);
        Assert.Equal(1, await db.ExpertReviewAssignments.CountAsync(a => a.ReviewRequestId == req));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Max_active_assignments_per_expert_caps_load()
    {
        var (db, svc, _, _) = Build(new ExpertAutoAssignmentOptions { MaxActiveAssignmentsPerExpert = 2 });
        await SeedExpertAsync(db, "only-expert");
        // Pre-load two active assignments — at cap.
        for (var i = 0; i < 2; i++)
        {
            db.ExpertReviewAssignments.Add(new ExpertReviewAssignment
            {
                Id = $"era-cap-{i}",
                ReviewRequestId = $"rr-cap-{i}",
                AssignedReviewerId = "only-expert",
                AssignedAt = DateTimeOffset.UtcNow,
                ClaimState = ExpertAssignmentState.Assigned,
            });
        }
        await db.SaveChangesAsync();
        await SeedPendingWritingRequestAsync(db, "medicine");

        var assigned = await svc.ProcessPendingAssignmentsAsync(default);
        Assert.Equal(0, assigned);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Sla_escalation_releases_overdue_standard_assignment()
    {
        var (db, svc, clock, _) = Build();
        await SeedExpertAsync(db, "expert-1");
        var (req, _) = await SeedPendingWritingRequestAsync(db, "medicine");
        await svc.ProcessPendingAssignmentsAsync(default);

        // Move forward 49 hours — past the 48h standard SLA.
        clock.Advance(TimeSpan.FromHours(49));
        var released = await svc.ProcessSlaEscalationsAsync(default);
        Assert.Equal(1, released);

        var assignment = await db.ExpertReviewAssignments
            .OrderByDescending(a => a.AssignedAt)
            .FirstAsync(a => a.ReviewRequestId == req && a.ClaimState == ExpertAssignmentState.Released);
        Assert.Equal("sla_overdue", assignment.ReasonCode);
        var snapshot = await db.ExpertSlaSnapshots.FirstAsync(s => s.ReviewRequestId == req);
        Assert.False(snapshot.WasMet);
        Assert.Equal("overdue", snapshot.SlaState);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Sla_uses_express_hours_for_express_turnaround()
    {
        var (db, svc, clock, _) = Build();
        await SeedExpertAsync(db, "expert-1");
        var (req, _) = await SeedPendingWritingRequestAsync(db, "medicine", turnaround: "express");
        await svc.ProcessPendingAssignmentsAsync(default);

        // Move forward 13 hours — past the 12h express SLA but not standard.
        clock.Advance(TimeSpan.FromHours(13));
        var released = await svc.ProcessSlaEscalationsAsync(default);
        Assert.Equal(1, released);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Disabled_options_skip_assignment()
    {
        var (db, svc, _, _) = Build(new ExpertAutoAssignmentOptions { Enabled = false });
        await SeedExpertAsync(db, "expert-1");
        await SeedPendingWritingRequestAsync(db, "medicine");
        Assert.Equal(0, await svc.ProcessPendingAssignmentsAsync(default));
        await db.DisposeAsync();
    }
}
