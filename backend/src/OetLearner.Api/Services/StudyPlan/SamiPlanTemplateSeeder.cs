using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Planner;

/// <summary>
/// SAMI Wave 1-PLANNER (hand-authored, idempotent): the short-horizon and
/// special-mode plan templates the starter seeder does not cover — exam-eve,
/// final 3-day, 7-day emergency, 14-day intensive, single-subtest recovery and
/// 20-minutes-a-day micro plans. Runs ALWAYS (insert-if-missing by id), unlike
/// <see cref="StudyPlanTemplateSeeder"/> which only fills an empty table, so a
/// production database that already seeded the three starter templates still
/// gains these. Tier rows mirror the starter pattern: free templates for every
/// tier, premium-only where the mode needs paid capacity.
/// </summary>
public class SamiPlanTemplateSeeder(LearnerDbContext db, ILogger<SamiPlanTemplateSeeder> logger)
{
    public async Task SeedIfMissingAsync(CancellationToken cancellationToken)
    {
        var existingIds = await db.StudyPlanTemplates.AsNoTracking()
            .Where(t => SamiTemplateIds.Contains(t.Id))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        var missing = SamiTemplateIds.Where(id => !existingIds.Contains(id)).ToList();
        if (missing.Count == 0) return;

        logger.LogInformation("Seeding SAMI plan templates: {Templates}", string.Join(',', missing));
        var now = DateTimeOffset.UtcNow;

        foreach (var template in BuildTemplates(now))
        {
            if (!missing.Contains(template.Id)) continue;
            db.StudyPlanTemplates.Add(template);
            string[] allTiers = [StudyPlanEntitlementResolver.FreeTier, StudyPlanEntitlementResolver.PremiumTier, StudyPlanEntitlementResolver.EliteTier];
            foreach (var tier in allTiers)
            {
                if (template.Id.StartsWith("tmpl-sami-20min", StringComparison.Ordinal)
                    && tier == StudyPlanEntitlementResolver.FreeTier)
                {
                    continue; // micro plans are a paid differentiator
                }
                db.StudyPlanTemplateTiers.Add(new StudyPlanTemplateTier
                {
                    Id = $"tpltier-{Guid.NewGuid():N}",
                    TemplateId = template.Id,
                    TierCode = tier,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public static readonly string[] SamiTemplateIds =
    [
        "tmpl-sami-exam-eve",
        "tmpl-sami-final-3d",
        "tmpl-sami-emergency-7d",
        "tmpl-sami-intensive-14d",
        "tmpl-sami-single-subtest",
        "tmpl-sami-20min",
    ];

    private static List<StudyPlanTemplate> BuildTemplates(DateTimeOffset now) =>
    [
        BuildExamEve(now),
        BuildFinal3Days(now),
        BuildEmergency7Days(now),
        BuildIntensive14Days(now),
        BuildSingleSubtest(now),
        Build20Minutes(now),
    ];

    private static List<StudyPlanTemplateDay> Days(params StudyPlanTemplateSlot[][] perDay)
    {
        var names = new[] { "mon", "tue", "wed", "thu", "fri", "sat", "sun" };
        return perDay.Select((slots, idx) => new StudyPlanTemplateDay
        {
            DayIndex = idx,
            DayOfWeek = names[idx],
            Slots = slots.ToList(),
        }).ToList();
    }

    private static StudyPlanTemplateSlot Slot(string subtest, string kind, int minutes, string hint, List<string>? tags = null)
        => new() { Subtest = subtest, Kind = kind, Minutes = minutes, RationaleHint = hint, Tags = tags };

    private static StudyPlanTemplate ExamEve(DateTimeOffset now)
    {
        var body = new StudyPlanTemplateBody
        {
            Weeks =
            [
                new StudyPlanTemplateWeek
                {
                    WeekIndex = 0,
                    Label = "Exam eve — logistics, warm-up, confidence",
                    Days = Days(
                        [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 20, "Re-read your saved rules and model-answer openings — nothing new")],
                        [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.MiniMock, 20, "One short warm-up set at exam pace")],
                        [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.MiniMock, 15, "Part A warm-up scan, strictly timed")],
                        [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.SpacedRepReview, 15, "Openings and empathy phrases out loud")],
                        [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 15, "Skim your corrected letters; note your two recurring fixes")],
                        [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.SpacedRepReview, 10, "Light review only — protect your sleep")],
                        [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.SpacedRepReview, 10, "Test-day logistics: ID, route, timings. Early night.")]
                    ),
                },
            ],
            Checkpoints = [],
        };
        return new StudyPlanTemplate
        {
            Id = "tmpl-sami-exam-eve",
            Slug = "sami-exam-eve",
            Name = "SAMI — Exam Eve / Final 24 Hours",
            Description = "Final-24-hours mode: warm-up, confidence and logistics only. Explicitly excludes new content.",
            ExamTypeCode = "OET",
            ExamFamilyCode = "oet",
            MinWeeks = 1,
            MaxWeeks = 1,
            TargetBand = null,
            ProfessionId = null,
            FocusTagsJson = JsonSerializer.Serialize(new[] { "exam-eve", "rehearsal", "no-new-content" }),
            DefaultMinutesPerDay = 20,
            TemplateBodyJson = JsonSerializer.Serialize(body),
            IsActive = true,
            Version = 1,
            CreatedBy = "system-seed",
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static StudyPlanTemplate Final3Days(DateTimeOffset now)
    {
        var body = new StudyPlanTemplateBody
        {
            Weeks =
            [
                new StudyPlanTemplateWeek
                {
                    WeekIndex = 0,
                    Label = "Final 3 days — error review and timed rehearsal",
                    Days = Days(
                        [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.MiniMock, 30, "Timed Part A+B set, then review every wrong answer"),
                         Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 20, "Review your last corrected letter")],
                        [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.MiniMock, 25, "Part A precision: spell drug names as you write"),
                         Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.SpacedRepReview, 15, "Role-play openings out loud")],
                        [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.MiniMock, 40, "One full letter under 40-minute exam timing")],
                        [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.MiniMock, 20, "Part C inference set — paraphrase hunting")],
                        [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.SpacedRepReview, 15, "Your recorded weak words, once")],
                        [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.SpacedRepReview, 20, "Light mixed warm-up + logistics check")],
                        [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.SpacedRepReview, 10, "Rest. Confidence. Early night.")]
                    ),
                },
            ],
            Checkpoints = [],
        };
        return new StudyPlanTemplate
        {
            Id = "tmpl-sami-final-3d",
            Slug = "sami-final-3d",
            Name = "SAMI — Final 3-Day Revision",
            Description = "Three-day final revision: timed rehearsal of your own weak spots, no new content.",
            ExamTypeCode = "OET",
            ExamFamilyCode = "oet",
            MinWeeks = 1,
            MaxWeeks = 1,
            TargetBand = null,
            ProfessionId = null,
            FocusTagsJson = JsonSerializer.Serialize(new[] { "final-revision", "rehearsal" }),
            DefaultMinutesPerDay = 45,
            TemplateBodyJson = JsonSerializer.Serialize(body),
            IsActive = true,
            Version = 1,
            CreatedBy = "system-seed",
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static StudyPlanTemplate Emergency7Days(DateTimeOffset now)
    {
        var body = new StudyPlanTemplateBody
        {
            Weeks =
            [
                new StudyPlanTemplateWeek
                {
                    WeekIndex = 0,
                    Label = "Emergency week — highest-yield error review and rehearsal",
                    Days = Days(
                        [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.MiniMock, 30, "Diagnostic mini-mock: find what still leaks"),
                         Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.DrillByTag, 20, "Your top writing error pattern", ["referral-letter"])],
                        [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.MiniMock, 25, "Part A precision under timing"),
                         Slot(StudyPlanSubtestCodes.Vocabulary, StudyPlanSlotKinds.SpacedRepReview, 10, "Your recorded weak terms")],
                        [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.NextUnattemptedPaper, 40, "Full letter, exam conditions")],
                        [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.MiniMock, 25, "Part C paraphrase set")],
                        [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.MiniMock, 25, "One role card, strict exam behaviour")],
                        [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.FullMock, 60, "Full timed reading paper")],
                        [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.SpacedRepReview, 20, "Error-log review + exam logistics")]
                    ),
                },
            ],
            Checkpoints =
            [
                new() { AfterWeek = 0, Kind = StudyPlanSlotKinds.MiniMock, Subtests = [StudyPlanSubtestCodes.Writing, StudyPlanSubtestCodes.Speaking] },
            ],
        };
        return new StudyPlanTemplate
        {
            Id = "tmpl-sami-emergency-7d",
            Slug = "sami-emergency-7d",
            Name = "SAMI — 7-Day Emergency",
            Description = "Seven-day emergency plan: mock-heavy rehearsal of evidenced weaknesses, new content cut to the minimum.",
            ExamTypeCode = "OET",
            ExamFamilyCode = "oet",
            MinWeeks = 1,
            MaxWeeks = 2,
            TargetBand = null,
            ProfessionId = null,
            FocusTagsJson = JsonSerializer.Serialize(new[] { "emergency", "rehearsal", "mock-heavy" }),
            DefaultMinutesPerDay = 60,
            TemplateBodyJson = JsonSerializer.Serialize(body),
            IsActive = true,
            Version = 1,
            CreatedBy = "system-seed",
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static StudyPlanTemplate Intensive14Days(DateTimeOffset now)
    {
        var body = new StudyPlanTemplateBody
        {
            Weeks =
            [
                new StudyPlanTemplateWeek
                {
                    WeekIndex = 0,
                    Label = "Intensive week 1 — strip your error patterns",
                    Days = Days(
                        [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.MiniMock, 30, "Timed set + error log"),
                         Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.DrillByTag, 25, "Weakest writing rule", ["referral-letter"])],
                        [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.MiniMock, 30, "Part A/B precision"),
                         Slot(StudyPlanSubtestCodes.Vocabulary, StudyPlanSlotKinds.SpacedRepReview, 10, "Weak terms review")],
                        [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.NextUnattemptedPaper, 40, "Full letter")],
                        [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.MiniMock, 25, "Difficult-patient role play"),
                         Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.DrillByTag, 15, "Paraphrase drill")],
                        [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.MiniMock, 30, "Part C inference")],
                        [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.MiniMock, 45, "Mixed timed set"),
                         Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 15, "Corrected-letter review")],
                        [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.SpacedRepReview, 20, "Week review: what moved, what did not")]
                    ),
                },
                new StudyPlanTemplateWeek
                {
                    WeekIndex = 1,
                    Label = "Intensive week 2 — exam-condition rehearsal",
                    Days = Days(
                        [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.FullMock, 60, "Full reading under exam timing")],
                        [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.NextUnattemptedPaper, 40, "Full letter + self-check against your rules")],
                        [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.FullMock, 45, "Full listening under exam timing")],
                        [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.MiniMock, 30, "Two role cards back to back")],
                        [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.MiniMock, 40, "Letter under 40 minutes strictly")],
                        [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.MiniMock, 30, "Weak-subtest rehearsal")],
                        [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.SpacedRepReview, 25, "Error-log final pass + logistics")]
                    ),
                },
            ],
            Checkpoints =
            [
                new() { AfterWeek = 0, Kind = StudyPlanSlotKinds.FullMock, Subtests = [StudyPlanSubtestCodes.Reading, StudyPlanSubtestCodes.Listening] },
                new() { AfterWeek = 1, Kind = StudyPlanSlotKinds.MiniMock, Subtests = [StudyPlanSubtestCodes.Writing, StudyPlanSubtestCodes.Speaking] },
            ],
        };
        return new StudyPlanTemplate
        {
            Id = "tmpl-sami-intensive-14d",
            Slug = "sami-intensive-14d",
            Name = "SAMI — 14-Day Intensive",
            Description = "Two-week intensive: strip error patterns in week one, exam-condition rehearsal in week two.",
            ExamTypeCode = "OET",
            ExamFamilyCode = "oet",
            MinWeeks = 2,
            MaxWeeks = 2,
            TargetBand = null,
            ProfessionId = null,
            FocusTagsJson = JsonSerializer.Serialize(new[] { "intensive", "error-dna", "rehearsal" }),
            DefaultMinutesPerDay = 90,
            TemplateBodyJson = JsonSerializer.Serialize(body),
            IsActive = true,
            Version = 1,
            CreatedBy = "system-seed",
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static StudyPlanTemplate SingleSubtest(DateTimeOffset now)
    {
        var body = new StudyPlanTemplateBody
        {
            Weeks = Enumerable.Range(0, 4).Select(w => new StudyPlanTemplateWeek
            {
                WeekIndex = w,
                Label = $"Resit recovery — {new[] { "diagnose", "drill", "rehearse", "confirm" }[w]}",
                Days = Days(
                    [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.WeakSkillFocus, 40, "Focused work on the resit sub-test")],
                    [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.DrillByTag, 25, "Targeted drill on your recorded errors")],
                    [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.NextUnattemptedPaper, 40, "Full sub-test task")],
                    [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 15, "Review corrected work")],
                    [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.WeakSkillFocus, 30, "Second focused session")],
                    [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.MiniMock, 40, "Timed mini assessment")],
                    [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.SpacedRepReview, 15, "Progress check — keep passed sub-tests warm, not heavy")]
                ),
            }).ToList(),
            Checkpoints = [],
        };
        return new StudyPlanTemplate
        {
            Id = "tmpl-sami-single-subtest",
            Slug = "sami-single-subtest",
            Name = "SAMI — Single-Subtest Resit Recovery",
            Description = "Writing-only style recovery plan: concentrates on one sub-test while keeping passed sub-tests warm.",
            ExamTypeCode = "OET",
            ExamFamilyCode = "oet",
            MinWeeks = 2,
            MaxWeeks = 16,
            TargetBand = null,
            ProfessionId = null,
            FocusTagsJson = JsonSerializer.Serialize(new[] { "resit", "single-subtest", "recovery" }),
            DefaultMinutesPerDay = 45,
            TemplateBodyJson = JsonSerializer.Serialize(body),
            IsActive = true,
            Version = 1,
            CreatedBy = "system-seed",
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static StudyPlanTemplate Build20Minutes(DateTimeOffset now)
    {
        var body = new StudyPlanTemplateBody
        {
            Weeks = Enumerable.Range(0, 8).Select(w => new StudyPlanTemplateWeek
            {
                WeekIndex = w,
                Label = $"Micro week {w + 1} — one thing, done well",
                Days = Days(
                    [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.WeakSkillFocus, 20, "One short weakness-focused set")],
                    [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.WeakSkillFocus, 20, "One short weakness-focused set")],
                    [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.DrillByTag, 20, "Micro writing drill")],
                    [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.PronunciationDrill, 15, "Out-loud practice")],
                    [Slot(StudyPlanSubtestCodes.Vocabulary, StudyPlanSlotKinds.SpacedRepReview, 15, "Spaced review")],
                    [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.MiniMock, 25, "Weekend: a longer timed set")],
                    [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.SpacedRepReview, 15, "Review the week's error log")]
                ),
            }).ToList(),
            Checkpoints = [],
        };
        return new StudyPlanTemplate
        {
            Id = "tmpl-sami-20min",
            Slug = "sami-20min",
            Name = "SAMI — 20 Minutes a Day",
            Description = "Micro plan for very busy candidates (shift workers, on-call weeks): one 20-minute high-yield block per day.",
            ExamTypeCode = "OET",
            ExamFamilyCode = "oet",
            MinWeeks = 2,
            MaxWeeks = 16,
            TargetBand = null,
            ProfessionId = null,
            FocusTagsJson = JsonSerializer.Serialize(new[] { "micro", "shift-worker", "20min" }),
            DefaultMinutesPerDay = 20,
            TemplateBodyJson = JsonSerializer.Serialize(body),
            IsActive = true,
            Version = 1,
            CreatedBy = "system-seed",
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
