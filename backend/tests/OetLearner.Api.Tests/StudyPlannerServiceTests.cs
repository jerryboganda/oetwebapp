using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.StudyPlanner;

namespace OetLearner.Api.Tests;

public class StudyPlannerServiceTests
{
    private static (LearnerDbContext db, StudyPlannerAdminService admin, StudyPlannerRuleEngine engine, StudyPlannerService planner) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var admin = new StudyPlannerAdminService(db);
        var engine = new StudyPlannerRuleEngine();
        var planner = new StudyPlannerService(db, engine);
        return (db, admin, engine, planner);
    }

    [Fact]
    public async Task Task_template_crud_happy_path()
    {
        var (db, admin, _, _) = Build();

        var created = await admin.CreateTaskTemplateAsync(new TaskTemplateCreate(
            Slug: "writing-referral-1",
            Title: "Writing: Referral Letter",
            SubtestCode: "writing",
            ItemType: "practice",
            DurationMinutes: 45,
            RationaleMarkdown: "Builds referral letter conciseness.",
            ProfessionScope: new[] { "medicine" },
            ExamFamilyCode: "oet",
            TargetCountries: null,
            DifficultyMin: 2, DifficultyMax: 4,
            DefaultSection: "today",
            DefaultContentPaperId: null,
            TagsCsv: "writing,referral"), "admin-1", default);

        Assert.NotNull(created.Id);
        Assert.Equal("writing", created.SubtestCode);

        var got = await admin.GetTaskTemplateAsync(created.Id, default);
        Assert.NotNull(got);
        Assert.Equal("Writing: Referral Letter", got!.Title);

        var updated = await admin.UpdateTaskTemplateAsync(created.Id, new TaskTemplateUpdate(
            Title: "Writing: Referral Letter v2",
            ItemType: null, DurationMinutes: 60, RationaleMarkdown: null,
            ProfessionScope: null, TargetCountries: null, DifficultyMin: null,
            DifficultyMax: null, DefaultSection: null, DefaultContentPaperId: null,
            TagsCsv: null, IsArchived: null), "admin-1", default);
        Assert.Equal(60, updated.DurationMinutes);
        Assert.Equal("Writing: Referral Letter v2", updated.Title);

        await admin.ArchiveTaskTemplateAsync(created.Id, "admin-1", default);
        var archived = await admin.GetTaskTemplateAsync(created.Id, default);
        Assert.True(archived!.IsArchived);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task Task_template_rejects_duplicate_slug()
    {
        var (db, admin, _, _) = Build();
        var dto = new TaskTemplateCreate("dup-slug", "t", "writing", "practice", 30, "r",
            null, "oet", null, null, null, null, null, null);
        await admin.CreateTaskTemplateAsync(dto, "admin-1", default);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            admin.CreateTaskTemplateAsync(dto, "admin-1", default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Task_template_rejects_invalid_subtest()
    {
        var (db, admin, _, _) = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            admin.CreateTaskTemplateAsync(new TaskTemplateCreate(
                "bad", "T", "INVALID_SUBTEST", "practice", 30, "r",
                null, "oet", null, null, null, null, null, null), "admin-1", default));
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Plan_template_with_items_can_be_created_and_queried()
    {
        var (db, admin, _, _) = Build();

        var t1 = await admin.CreateTaskTemplateAsync(new TaskTemplateCreate(
            "t1", "Writing Practice", "writing", "practice", 30, "r", null, "oet", null, null, null, null, null, null),
            "admin-1", default);
        var t2 = await admin.CreateTaskTemplateAsync(new TaskTemplateCreate(
            "t2", "Speaking Role Play", "speaking", "roleplay", 20, "r", null, "oet", null, null, null, null, null, null),
            "admin-1", default);

        var plan = await admin.CreatePlanTemplateAsync(new PlanTemplateCreate(
            "std-8w", "Standard 8-Week", "", 8, 10, "oet"), "admin-1", default);

        await admin.ReplacePlanTemplateItemsAsync(plan.Id, new[]
        {
            new PlanTemplateItemUpsert(null, t1.Id, 0, 0, "today", 80, true, null, 0),
            new PlanTemplateItemUpsert(null, t2.Id, 0, 1, "thisWeek", 60, true, null, 1),
        }, "admin-1", default);

        var detail = await admin.GetPlanTemplateDetailAsync(plan.Id, default);
        Assert.NotNull(detail);
        Assert.Equal(2, detail!.Items.Count);
        Assert.Equal("today", detail.Items[0].Section);

        await db.DisposeAsync();
    }

    [Fact]
    public void Rule_engine_matches_profession_filter()
    {
        var engine = new StudyPlannerRuleEngine();
        var rule = new StudyPlanAssignmentRule
        {
            Id = "r1", Name = "Medicine only", ExamFamilyCode = "oet",
            Priority = 100, Weight = 50, IsActive = true,
            TargetTemplateId = "tpl-1",
            ConditionJson = """{"professions":["medicine"]}""",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var ctxMed = new LearnerPlanContext("u1", "medicine", "oet", null, null, null, null, null, null, null, Array.Empty<string>());
        var ctxNur = new LearnerPlanContext("u2", "nursing", "oet", null, null, null, null, null, null, null, Array.Empty<string>());

        Assert.Equal("tpl-1", engine.Match(ctxMed, new[] { rule }).TemplateId);
        Assert.Null(engine.Match(ctxNur, new[] { rule }).TemplateId);
    }

    [Fact]
    public void Rule_engine_weights_and_priority_resolve_ties()
    {
        var engine = new StudyPlannerRuleEngine();
        var r1 = new StudyPlanAssignmentRule
        {
            Id = "r1", Name = "A", ExamFamilyCode = "oet", IsActive = true,
            Priority = 50, Weight = 10, TargetTemplateId = "tpl-A",
            ConditionJson = "{}", CreatedAt = DateTimeOffset.UtcNow,
        };
        var r2 = new StudyPlanAssignmentRule
        {
            Id = "r2", Name = "B", ExamFamilyCode = "oet", IsActive = true,
            Priority = 50, Weight = 90, TargetTemplateId = "tpl-B",
            ConditionJson = "{}", CreatedAt = DateTimeOffset.UtcNow.AddSeconds(1),
        };
        var ctx = new LearnerPlanContext("u", null, "oet", null, null, null, null, null, null, null, Array.Empty<string>());
        // r2 has higher weight → wins
        Assert.Equal("tpl-B", engine.Match(ctx, new[] { r1, r2 }).TemplateId);
    }

    [Fact]
    public void Rule_engine_weeks_to_exam_filter()
    {
        var engine = new StudyPlannerRuleEngine();
        var r = new StudyPlanAssignmentRule
        {
            Id = "r", Name = "Close to exam", ExamFamilyCode = "oet", IsActive = true,
            Priority = 10, Weight = 50, TargetTemplateId = "crunch",
            ConditionJson = """{"maxWeeksToExam":4}""", CreatedAt = DateTimeOffset.UtcNow,
        };
        var close = new LearnerPlanContext("u1", null, "oet", null, 3, null, null, null, null, null, Array.Empty<string>());
        var far = new LearnerPlanContext("u2", null, "oet", null, 12, null, null, null, null, null, Array.Empty<string>());
        Assert.Equal("crunch", engine.Match(close, new[] { r }).TemplateId);
        Assert.Null(engine.Match(far, new[] { r }).TemplateId);
    }

    [Fact]
    public async Task Generator_materialises_template_into_items()
    {
        var (db, admin, _, planner) = Build();

        // Seed minimal goal so the context is sane
        db.Goals.Add(new LearnerGoal
        {
            Id = Guid.NewGuid(), UserId = "u-1",
            ProfessionId = "medicine", TargetCountry = "UK",
            TargetWritingScore = 350, StudyHoursPerWeek = 10,
            WeakSubtestsJson = """["writing"]""",
            DraftStateJson = "{}", ExamFamilyCode = "oet", ExamTypeCode = "oet",
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var t1 = await admin.CreateTaskTemplateAsync(new TaskTemplateCreate(
            "w1", "Writing Drill", "writing", "practice", 45, "Build conciseness",
            null, "oet", null, 1, 5, "today", null, ""), "admin-1", default);

        var plan = await admin.CreatePlanTemplateAsync(new PlanTemplateCreate(
            "plan-std", "Std", "", 8, 10, "oet"), "admin-1", default);

        await admin.ReplacePlanTemplateItemsAsync(plan.Id, new[]
        {
            new PlanTemplateItemUpsert(null, t1.Id, 0, 0, "today", 80, true, null, 0),
        }, "admin-1", default);

        await admin.CreateRuleAsync(new AssignmentRuleCreate(
            "All medicine", "oet", 100, 50,
            """{"professions":["medicine"]}""", plan.Id, true), "admin-1", default);

        var generated = await planner.GenerateForLearnerAsync("u-1", "test", default);
        Assert.Equal(plan.Id, generated.TemplateId);

        var items = await planner.GetItemsAsync("u-1", default);
        Assert.Single(items);
        Assert.Equal("Writing Drill", items[0].Title);
        Assert.Equal("today", items[0].Section);

        // A log entry exists
        var log = await db.StudyPlanGenerationLogs.FirstAsync();
        Assert.Equal(plan.Id, log.TemplateId);
        Assert.Equal(1, log.ItemCount);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task Generator_falls_back_to_any_template_when_no_rule_matches()
    {
        var (db, admin, _, planner) = Build();

        var t = await admin.CreateTaskTemplateAsync(new TaskTemplateCreate(
            "x", "Generic Drill", "listening", "drill", 15, "r", null, "oet", null, null, null, null, null, null), "admin-1", default);
        var plan = await admin.CreatePlanTemplateAsync(new PlanTemplateCreate(
            "gen", "Generic", "", 4, 5, "oet"), "admin-1", default);
        await admin.ReplacePlanTemplateItemsAsync(plan.Id, new[]
        {
            new PlanTemplateItemUpsert(null, t.Id, 0, 0, "today", 50, true, null, 0),
        }, "admin-1", default);

        // No rules + no goal → fallback path picks the only non-archived template.
        var generated = await planner.GenerateForLearnerAsync("no-profile-user", "test", default);
        Assert.Equal(plan.Id, generated.TemplateId);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task Drift_policy_thresholds_are_respected()
    {
        var (db, admin, _, planner) = Build();
        var plan = await planner.GetOrCreatePlanAsync("u-1", default);
        db.StudyPlanItems.Add(new StudyPlanItem
        {
            Id = "spi-1", StudyPlanId = plan.Id, Title = "Old", SubtestCode = "writing",
            DurationMinutes = 30, Rationale = "", ItemType = "practice", Section = "today",
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
            Status = StudyPlanItemStatus.NotStarted,
        });
        await db.SaveChangesAsync();

        // Default policy (mild=3, mod=7, sev=14). 10 days overdue → moderate.
        var report = await planner.DetectDriftAsync("u-1", allowAutoRegen: false, default);
        Assert.Equal("moderate", report.Level);
        Assert.True(report.OverdueItems >= 1);
        Assert.True(report.DriftDays >= 10);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task Drift_policy_auto_regen_triggers_generator_when_severe()
    {
        var (db, admin, _, planner) = Build();

        var t = await admin.CreateTaskTemplateAsync(new TaskTemplateCreate(
            "y", "Listening Drill", "listening", "drill", 15, "r", null, "oet", null, null, null, null, null, null), "admin-1", default);
        var plan = await admin.CreatePlanTemplateAsync(new PlanTemplateCreate(
            "p", "P", "", 4, 5, "oet"), "admin-1", default);
        await admin.ReplacePlanTemplateItemsAsync(plan.Id, new[]
        {
            new PlanTemplateItemUpsert(null, t.Id, 0, 0, "today", 50, true, null, 0),
        }, "admin-1", default);

        var learnerPlan = await planner.GetOrCreatePlanAsync("u-2", default);
        db.StudyPlanItems.Add(new StudyPlanItem
        {
            Id = "spi-old", StudyPlanId = learnerPlan.Id, Title = "Old", SubtestCode = "writing",
            DurationMinutes = 30, Rationale = "", ItemType = "practice", Section = "today",
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-20)),
            Status = StudyPlanItemStatus.NotStarted,
        });
        await db.SaveChangesAsync();

        var report = await planner.DetectDriftAsync("u-2", allowAutoRegen: true, default);
        Assert.Equal("severe", report.Level);
        Assert.True(report.ShouldRegenerate);
        // After auto regen, old item should be removed (new generator clears NotStarted items).
        var items = await planner.GetItemsAsync("u-2", default);
        Assert.DoesNotContain(items, i => i.Id == "spi-old");

        await db.DisposeAsync();
    }

    [Fact]
    public async Task Ics_export_produces_valid_vcalendar()
    {
        var (db, _, _, planner) = Build();
        var plan = await planner.GetOrCreatePlanAsync("u-3", default);
        db.StudyPlanItems.Add(new StudyPlanItem
        {
            Id = "spi-ics", StudyPlanId = plan.Id, Title = "Writing Practice",
            SubtestCode = "writing", DurationMinutes = 45, Rationale = "Focus on conciseness",
            ItemType = "practice", Section = "today",
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            Status = StudyPlanItemStatus.NotStarted,
        });
        await db.SaveChangesAsync();
        var ics = await planner.ExportIcsAsync("u-3", default);
        Assert.StartsWith("BEGIN:VCALENDAR", ics);
        Assert.Contains("Writing Practice", ics);
        Assert.Contains("END:VEVENT", ics);
        Assert.Contains("END:VCALENDAR", ics);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task Drift_policy_update_validates_ordering()
    {
        var (db, admin, _, _) = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            admin.UpdateDriftPolicyAsync("oet", new DriftPolicyUpdate(
                MildDays: 10, ModerateDays: 5, SevereDays: 14,
                MildCopy: null, ModerateCopy: null, SevereCopy: null, OnTrackCopy: null,
                AutoRegenerateOnModerate: null, AutoRegenerateOnSevere: null), "admin-1", default));
        await db.DisposeAsync();
    }

    [Fact]
    public void StartUrl_resolves_to_authored_content_paper()
    {
        var i = new StudyPlanItem
        {
            Id = "spi", StudyPlanId = "p", Title = "T", SubtestCode = "writing",
            DurationMinutes = 45, Rationale = "", Section = "today", ItemType = "practice",
            Status = StudyPlanItemStatus.NotStarted,
            ContentPaperId = "paper-123",
        };
        var url = StudyPlannerService.BuildStartUrl(i);
        Assert.Equal("/writing/tasks/paper-123", url);
    }

    [Fact]
    public void StartUrl_falls_back_to_subtest_root_without_content()
    {
        var i = new StudyPlanItem
        {
            Id = "spi", StudyPlanId = "p", Title = "T", SubtestCode = "reading",
            DurationMinutes = 30, Rationale = "", Section = "today", ItemType = "practice",
            Status = StudyPlanItemStatus.NotStarted,
        };
        Assert.Equal("/reading", StudyPlannerService.BuildStartUrl(i));
    }
}
