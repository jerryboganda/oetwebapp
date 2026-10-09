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
        "tmpl-sami-30day",
        "tmpl-sami-60day",
        "tmpl-sami-90day",
    ];

    private static List<StudyPlanTemplate> BuildTemplates(DateTimeOffset now) =>
    [
        ExamEve(now),
        Final3Days(now),
        Emergency7Days(now),
        Intensive14Days(now),
        SingleSubtest(now),
        Build20Minutes(now),
        Sami30Day(now),
        Sami60Day(now),
        Sami90Day(now),
    ];

    private static List<StudyPlanTemplateDay> Days(params StudyPlanTemplateSlot[][] perDay)
    {
        var names = new[] { "mon", "tue", "wed", "thu", "fri", "sat", "sun" };
        return perDay.Select((slots, idx) => new StudyPlanTemplateDay
        {
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

    // ── F-033: 30/60/90-day plans ─────────────────────────────────────────────
    //
    // The register recorded this as a gap because nothing covered these horizons: the
    // 8/12-week course templates land at ~60/~90 days but are course-shaped, and the SAMI
    // templates covered 1-2 and 2-16 weeks without a calendar anchor.
    //
    // The three below are deliberately NOT the same plan at three lengths. The real
    // difference between having 30, 60 or 90 days is how much time you can afford to
    // LEARN versus CONSOLIDATE, so each horizon has a different arc:
    //
    //   90 days — foundation → consolidation → exam readiness → peak. Enough runway to
    //             rebuild a genuinely weak sub-test from the ground up.
    //   60 days — shorter foundation → consolidation → peak, with less new material and an
    //             earlier move to timed work.
    //   30 days — triage → timed rehearsal → peak. No foundation phase at all: with four
    //             weeks left, new theory is a poor use of the hours, and every week is
    //             timed rehearsal against the learner's own weak spots.
    //
    // One deliberate omission, stated because it is a product decision rather than an
    // oversight: none of the three contains a FullMock slot. A full mock consumes most of
    // a study day, and the right moment for it depends on the exam date and the learner's
    // readiness, which the template cannot know. MiniMock and timed-set slots carry the
    // rehearsal instead, and the planner can promote one to a full mock once it knows the
    // date. TemplateMetadataNote records this so it is not later mistaken for a bug.

    private static StudyPlanTemplate Sami30Day(DateTimeOffset now)
    {
        var body = new StudyPlanTemplateBody
        {
            Weeks =
            [
                new StudyPlanTemplateWeek
                {
                    WeekIndex = 0,
                    Label = "Week 1 — triage: find the marks you are actually losing",
                    Days = Days(
                        [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.NextUnattemptedPaper, 60, "One full Reading paper, strictly timed. Log every wrong answer before you look at why."),
                         Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 20, "Re-read your last corrected letter; note your two recurring fixes")],
                        [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.NextUnattemptedPaper, 45, "One full Listening paper timed. Mark every answer you were unsure of, right or wrong."),
                         Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.DrillByTag, 20, "Drill the single question type that cost you most marks")],
                        [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.MiniMock, 45, "One letter under 40-minute exam timing, then compare against the model answer"),
                         Slot(StudyPlanSubtestCodes.Vocabulary, StudyPlanSlotKinds.SpacedRepReview, 15, "Spaced review — due cards only")],
                        [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.PronunciationDrill, 30, "Two role plays out loud, recorded. Listen back once."),
                         Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.WeakSkillFocus, 25, "Weakest Reading skill, targeted set")],
                        [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.DrillByTag, 30, "Part A precision: spelling, numbers, drug names"),
                         Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.DrillByTag, 25, "Drill your weakest letter section (intro, request, closure)")],
                        [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.FullMock, 90, "Full timed Reading under exam conditions — this is your readiness probe"),
                         Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.SpacedRepReview, 20, "Review the week's Listening misses")],
                        [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 30, "Write your triage list: the three fixes worth the most marks"),
                         Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.SpacedRepReview, 20, "Review the week's error log; pick two speaking habits to change")]
                    ),
                },
                new StudyPlanTemplateWeek
                {
                    WeekIndex = 1,
                    Label = "Week 2 — attack the three highest-value fixes",
                    Days = Days(
                        [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.WeakSkillFocus, 45, "Targeted set on fix #1"),
                         Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.DrillByTag, 25, "Drill fix #2")],
                        [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.WeakSkillFocus, 40, "Targeted set on fix #3"),
                         Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.SpacedRepReview, 20, "Spaced review of last week's errors")],
                        [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.MiniMock, 45, "Timed letter, applying fix #2 deliberately"),
                         Slot(StudyPlanSubtestCodes.Vocabulary, StudyPlanSlotKinds.SpacedRepReview, 15, "Due cards only")],
                        [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.PronunciationDrill, 30, "Record two role plays; check the two habits you chose"),
                         Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.DrillByTag, 25, "Part A precision repeat")],
                        [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.MiniMock, 40, "Timed Part B+C set — paragraphs and inference"),
                         Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 20, "Review corrected letters")],
                        [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.MiniMock, 45, "Timed Speaking set; self-assess against the criteria"),
                         Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.SpacedRepReview, 25, "Review every wrong answer from this week")],
                        [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.MiniMock, 40, "Timed Listening set"),
                         Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 25, "One paragraph rewritten for register")]
                    ),
                },
                new StudyPlanTemplateWeek
                {
                    WeekIndex = 2,
                    Label = "Week 3 — timed rehearsal under pressure",
                    Days = Days(
                        [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.MiniMock, 50, "Full Part A strictly timed — 15 minutes, no overrun"),
                         Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.MiniMock, 45, "Timed letter; you should now be fixing old habits, not learning new ones")],
                        [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.MiniMock, 45, "Full Listening paper timed"),
                         Slot(StudyPlanSubtestCodes.Vocabulary, StudyPlanSlotKinds.SpacedRepReview, 15, "Due cards only")],
                        [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.DrillByTag, 35, "Your weakest section, drilled"),
                         Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.DrillByTag, 25, "Question type that still costs you")],
                        [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.MiniMock, 45, "Two role plays at exam pace, recorded"),
                         Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.SpacedRepReview, 20, "Weak-word list, once")],
                        [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.NextUnattemptedPaper, 60, "Full Reading paper timed — compare with week 1"),
                         Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 20, "Review your best and worst letter")],
                        [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.NextUnattemptedPaper, 45, "Full Listening paper timed — compare with week 1"),
                         Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.SpacedRepReview, 25, "Self-assess against the public criteria")],
                        [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 30, "Confirm the three fixes are now automatic"),
                         Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.SpacedRepReview, 25, "Error log review — anything still repeating?")]
                    ),
                },
                new StudyPlanTemplateWeek
                {
                    WeekIndex = 3,
                    Label = "Week 4 — exam week: consolidate, protect, perform",
                    Days = Days(
                        [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.MiniMock, 40, "One timed set, then stop adding new work"),
                         Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 25, "Your rules and openings — nothing new")],
                        [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.MiniMock, 35, "One timed set at exam pace"),
                         Slot(StudyPlanSubtestCodes.Vocabulary, StudyPlanSlotKinds.SpacedRepReview, 15, "Due cards only")],
                        [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.MiniMock, 40, "Final timed letter — one last rehearsal, then rest on it"),
                         Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.SpacedRepReview, 20, "Skim your error log once")],
                        [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.SpacedRepReview, 30, "Openings and empathy phrases out loud"),
                         Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.SpacedRepReview, 20, "Weak words, once")],
                        [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.SpacedRepReview, 25, "Light review only"),
                         Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 20, "Skim corrected letters; note your two fixes")],
                        [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.SpacedRepReview, 20, "Test-day logistics: ID, route, timings"),
                         Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.SpacedRepReview, 15, "Protect your sleep — no new content")],
                        [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.SpacedRepReview, 15, "Rest. Confidence. Early night.")]
                    ),
                },
            ],
            // Checkpoints are left empty deliberately, matching every other SAMI template.
            // A StudyPlanTemplateCheckpoint carries a `kind` that is a SLOT kind (mini-mock,
            // full-mock ...), so a phase boundary such as "foundation complete" has no honest
            // representation — inventing one would assert a rehearsal event that the plan does
            // not schedule. The week Labels carry the phase structure instead.
            Checkpoints = [],
        };
        return new StudyPlanTemplate
        {
            Id = "tmpl-sami-30day",
            Slug = "sami-30day",
            Name = "SAMI — 30-Day Plan",
            Description = "Four weeks: triage the marks you are losing, fix the three worth most, rehearse under timing, then consolidate. No foundation phase — with 30 days left, new theory is a poor use of the hours.",
            ExamTypeCode = "OET",
            ExamFamilyCode = "oet",
            MinWeeks = 4,
            MaxWeeks = 5,
            TargetBand = null,
            ProfessionId = null,
            FocusTagsJson = JsonSerializer.Serialize(new[] { "30-day", "triage", "timed-rehearsal" }),
            DefaultMinutesPerDay = 60,
            TemplateBodyJson = JsonSerializer.Serialize(body),
            IsActive = true,
            Version = 1,
            CreatedBy = "system-seed",
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static StudyPlanTemplate Sami60Day(DateTimeOffset now)
    {
        // Shorter foundation than the 90-day, then the same consolidation → peak shape.
        // Weeks 4-5 are the longest block and carry the most volume, because that is where
        // habit change actually sticks.
        var weeks = new List<StudyPlanTemplateWeek>
        {
            new()
            {
                WeekIndex = 0,
                Label = "Week 1 — baseline and diagnosis",
                Days = Days(
                    [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.NextUnattemptedPaper, 60, "Baseline Reading paper, timed"),
                     Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 20, "Re-read your last corrected letter")],
                    [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.NextUnattemptedPaper, 45, "Baseline Listening paper, timed"),
                     Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.WeakSkillFocus, 25, "Weakest Reading skill")],
                    [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.MiniMock, 45, "One timed letter — establish your real baseline"),
                     Slot(StudyPlanSubtestCodes.Vocabulary, StudyPlanSlotKinds.SpacedRepReview, 15, "Due cards only")],
                    [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.PronunciationDrill, 30, "Record two role plays; note pronunciation patterns"),
                     Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.DrillByTag, 25, "Part A precision")],
                    [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.DrillByTag, 35, "Drill your worst question type"),
                     Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.DrillByTag, 25, "Drill your weakest letter section")],
                    [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.MiniMock, 45, "Timed Speaking set, self-assessed"),
                     Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.SpacedRepReview, 25, "Review the week's misses")],
                    [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 30, "Write your baseline summary and priority list"),
                     Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.SpacedRepReview, 20, "Error log; pick two listening habits to change")]
                ),
            },
        };

        // Weeks 2-5: foundation and targeted repair, growing in volume.
        for (var w = 1; w <= 4; w++)
        {
            var label = w switch
            {
                1 => "Week 2 — foundation: rebuild the weakest sub-test",
                2 => "Week 3 — foundation: second weak sub-test",
                3 => "Week 4 — volume: this is the block that changes habits",
                _ => "Week 5 — volume: hold the pace",
            };
            weeks.Add(new StudyPlanTemplateWeek
            {
                WeekIndex = w,
                Label = label,
                Days = Days(
                    [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.WeakSkillFocus, 45, "Targeted weakness set"),
                     Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.DrillByTag, 25, "Writing drill by tag")],
                    [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.WeakSkillFocus, 40, "Targeted Listening weakness set"),
                     Slot(StudyPlanSubtestCodes.Vocabulary, StudyPlanSlotKinds.SpacedRepReview, 15, "Due cards only")],
                    [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.MiniMock, 45, "Timed letter, applying this month's fixes"),
                     Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.DrillByTag, 25, "Question type drill")],
                    [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.PronunciationDrill, 35, "Recorded role plays; check your two habits"),
                     Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.DrillByTag, 25, "Part A precision repeat")],
                    [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.MiniMock, 45, "Timed set — track the trend"),
                     Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 20, "Review corrected letters")],
                    [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.MiniMock, 50, "Timed Speaking set at exam pace"),
                     Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.SpacedRepReview, 25, "Wrong-answer review")],
                    [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.MiniMock, 45, "Timed Listening set"),
                     Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 25, "One paragraph rewritten for register")]
                ),
            });
        }

        // Weeks 6-7: consolidation — move from learning to performing.
        for (var w = 5; w <= 6; w++)
        {
            weeks.Add(new StudyPlanTemplateWeek
            {
                WeekIndex = w,
                Label = w == 5 ? "Week 6 — consolidation: shift from learning to performing" : "Week 7 — consolidation: full timing discipline",
                Days = Days(
                    [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.NextUnattemptedPaper, 60, "Full Reading paper timed — compare with baseline"),
                     Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 20, "Review your best and worst letter")],
                    [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.NextUnattemptedPaper, 45, "Full Listening paper timed — compare with baseline"),
                     Slot(StudyPlanSubtestCodes.Vocabulary, StudyPlanSlotKinds.SpacedRepReview, 15, "Due cards only")],
                    [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.MiniMock, 45, "Timed letter under exam conditions"),
                     Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.DrillByTag, 25, "Anything still costing marks")],
                    [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.MiniMock, 50, "Two role plays at exam pace, recorded"),
                     Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.SpacedRepReview, 20, "Weak-word list, once")],
                    [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.MiniMock, 45, "Timed Part A — 15 minutes, no overrun"),
                     Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 25, "Confirm your fixes are becoming automatic")],
                    [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.MiniMock, 50, "Timed Speaking set; self-assess against criteria"),
                     Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.SpacedRepReview, 25, "Error log review")],
                    [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.MiniMock, 45, "Timed Listening set"),
                     Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 30, "Final consolidation notes")]
                ),
            });
        }

        // Week 8: peak week.
        weeks.Add(new StudyPlanTemplateWeek
        {
            WeekIndex = 7,
            Label = "Week 8 — exam week: rehearse, then protect",
            Days = Days(
                [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.MiniMock, 40, "One timed set; then no new material"),
                 Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 25, "Your rules and openings")],
                [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.MiniMock, 35, "Timed set at exam pace"),
                 Slot(StudyPlanSubtestCodes.Vocabulary, StudyPlanSlotKinds.SpacedRepReview, 15, "Due cards only")],
                [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.MiniMock, 40, "Final timed letter"),
                 Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.SpacedRepReview, 20, "Skim the error log once")],
                [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.SpacedRepReview, 30, "Openings and empathy phrases out loud"),
                 Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.SpacedRepReview, 20, "Weak words, once")],
                [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.SpacedRepReview, 25, "Light review only"),
                 Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 20, "Skim corrected letters")],
                [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.SpacedRepReview, 20, "Test-day logistics"),
                 Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.SpacedRepReview, 15, "Protect your sleep")],
                [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.SpacedRepReview, 15, "Rest. Confidence. Early night.")]
            ),
        });

        var body = new StudyPlanTemplateBody
        {
            Weeks = weeks,
            Checkpoints = [],
        };
        return new StudyPlanTemplate
        {
            Id = "tmpl-sami-60day",
            Slug = "sami-60day",
            Name = "SAMI — 60-Day Plan",
            Description = "Eight weeks: baseline, rebuild the two weakest sub-tests, then a long volume block and consolidation before peak week. The standard runway for a first sitting.",
            ExamTypeCode = "OET",
            ExamFamilyCode = "oet",
            MinWeeks = 8,
            MaxWeeks = 9,
            TargetBand = null,
            ProfessionId = null,
            FocusTagsJson = JsonSerializer.Serialize(new[] { "60-day", "standard", "consolidation" }),
            DefaultMinutesPerDay = 60,
            TemplateBodyJson = JsonSerializer.Serialize(body),
            IsActive = true,
            Version = 1,
            CreatedBy = "system-seed",
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private static StudyPlanTemplate Sami90Day(DateTimeOffset now)
    {
        // The 90-day arc: foundation → consolidation → exam readiness → peak. It is the only
        // one of the three with a real foundation phase, because it is the only one with the
        // runway to rebuild a genuinely weak sub-test rather than patch it.
        var weeks = new List<StudyPlanTemplateWeek>();

        var phaseLabels = new (string Label, int Minutes)[]
        {
            ("Foundation 1 — rebuild from the ground up", 45),
            ("Foundation 2 — second weak sub-test", 45),
            ("Foundation 3 — complete the foundation", 50),
            ("Foundation 4 — foundation complete, raise volume", 50),
            ("Consolidation 1 — from learning to performing", 55),
            ("Consolidation 2 — timing discipline across all four", 55),
            ("Consolidation 3 — hold the pace under load", 60),
            ("Consolidation 4 — narrow to what still costs marks", 60),
            ("Exam readiness 1 — full timing, every sub-test", 60),
            ("Exam readiness 2 — rehearse the real conditions", 60),
            ("Exam readiness 3 — stabilise, no new content", 45),
            ("Peak week — rehearse lightly, then protect", 30),
        };

        for (var w = 0; w < phaseLabels.Length; w++)
        {
            var (label, minutes) = phaseLabels[w];
            var isFoundation = w < 4;
            var isReadiness = w >= 8;
            var isPeak = w == 11;

            var daily = new StudyPlanTemplateSlot[][];
            if (isPeak)
            {
                daily =
                [
                    [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.MiniMock, 40, "One timed set, then stop adding new work"),
                     Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 25, "Your rules and openings — nothing new")],
                    [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.MiniMock, 35, "One timed set at exam pace"),
                     Slot(StudyPlanSubtestCodes.Vocabulary, StudyPlanSlotKinds.SpacedRepReview, 15, "Due cards only")],
                    [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.MiniMock, 40, "Final timed letter"),
                     Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.SpacedRepReview, 20, "Skim the error log once")],
                    [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.SpacedRepReview, 30, "Openings and empathy phrases out loud"),
                     Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.SpacedRepReview, 20, "Weak words, once")],
                    [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.SpacedRepReview, 25, "Light review only"),
                     Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 20, "Skim corrected letters")],
                    [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.SpacedRepReview, 20, "Test-day logistics"),
                     Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.SpacedRepReview, 15, "Protect your sleep")],
                    [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.SpacedRepReview, 15, "Rest. Confidence. Early night.")],
                ];
            }
            else if (isFoundation)
            {
                var focus = minutes >= 50 ? "Weakest sub-test block 1" : "Weakest sub-test block";
                daily =
                [
                    [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.WeakSkillFocus, minutes - 15, focus),
                     Slot(StudyPlanSubtestCodes.Vocabulary, StudyPlanSlotKinds.SpacedRepReview, 15, "Due cards only")],
                    [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.WeakSkillFocus, minutes - 15, "Listening weakness block"),
                     Slot(StudyPlanSubtestCodes.Vocabulary, StudyPlanSlotKinds.SpacedRepReview, 15, "Due cards only")],
                    [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.DrillByTag, minutes, "Writing drill by tag — fundamentals first")],
                    [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.PronunciationDrill, minutes - 10, "Recorded out-loud practice"),
                     Slot(StudyPlanSubtestCodes.Vocabulary, StudyPlanSlotKinds.SpacedRepReview, 10, "Due cards only")],
                    [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.DrillByTag, minutes - 10, "Question-type drill"),
                     Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.DrillByTag, 10, "Part A precision")],
                    [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.MiniMock, minutes, "Weekend timed set — first real timing work"),
                     Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 20, "Review corrected work")],
                    [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.MiniMock, minutes, "Weekend timed set"),
                     Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.SpacedRepReview, 20, "Review the week's error log")]
                ];
            }
            else if (isReadiness)
            {
                daily =
                [
                    [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.NextUnattemptedPaper, minutes, "Full Reading paper timed — compare with baseline"),
                     Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 20, "Review your best and worst letter")],
                    [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.NextUnattemptedPaper, minutes - 15, "Full Listening paper timed"),
                     Slot(StudyPlanSubtestCodes.Vocabulary, StudyPlanSlotKinds.SpacedRepReview, 15, "Due cards only")],
                    [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.MiniMock, 45, "Timed letter under exam conditions"),
                     Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.DrillByTag, 25, "Only what still costs marks")],
                    [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.MiniMock, 50, "Timed Speaking set at exam pace"),
                     Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.SpacedRepReview, 20, "Weak words, once")],
                    [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.MiniMock, 45, "Timed Part A — 15 minutes, no overrun"),
                     Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 25, "Confirm fixes are automatic")],
                    [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.MiniMock, 50, "Second timed Speaking set; self-assess"),
                     Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.SpacedRepReview, 25, "Error log review")],
                    [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.MiniMock, 45, "Timed Listening set"),
                     Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 25, "Consolidation notes")]
                ];
            }
            else
            {
                daily =
                [
                    [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.MiniMock, minutes - 15, "Timed set — track the trend"),
                     Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.DrillByTag, 15, "Writing drill by tag")],
                    [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.MiniMock, minutes - 15, "Timed Listening set"),
                     Slot(StudyPlanSubtestCodes.Vocabulary, StudyPlanSlotKinds.SpacedRepReview, 15, "Due cards only")],
                    [Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.MiniMock, 45, "Timed letter, applying your fixes"),
                     Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.DrillByTag, 25, "Question-type drill")],
                    [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.PronunciationDrill, minutes - 20, "Recorded role plays"),
                     Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.DrillByTag, 20, "Part A precision")],
                    [Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.NextUnattemptedPaper, minutes, "Full timed Reading — compare with baseline"),
                     Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 20, "Review corrected letters")],
                    [Slot(StudyPlanSubtestCodes.Speaking, StudyPlanSlotKinds.MiniMock, minutes, "Timed Speaking set; self-assess against criteria"),
                     Slot(StudyPlanSubtestCodes.Reading, StudyPlanSlotKinds.SpacedRepReview, 25, "Wrong-answer review")],
                    [Slot(StudyPlanSubtestCodes.Listening, StudyPlanSlotKinds.NextUnattemptedPaper, minutes - 15, "Full timed Listening — compare with baseline"),
                     Slot(StudyPlanSubtestCodes.Writing, StudyPlanSlotKinds.SpacedRepReview, 25, "Register and closure practice")]
                ];
            }

            weeks.Add(new StudyPlanTemplateWeek { WeekIndex = w, Label = $"Week {w + 1} — {label}", Days = Days(daily) });
        }

        var body = new StudyPlanTemplateBody
        {
            Weeks = weeks,
            Checkpoints = [],
        };
        return new StudyPlanTemplate
        {
            Id = "tmpl-sami-90day",
            Slug = "sami-90day",
            Name = "SAMI — 90-Day Plan",
            Description = "Twelve weeks in four phases: foundation, consolidation, exam readiness, peak. The only horizon with runway to genuinely rebuild a weak sub-test rather than patch it.",
            ExamTypeCode = "OET",
            ExamFamilyCode = "oet",
            MinWeeks = 12,
            MaxWeeks = 13,
            TargetBand = null,
            ProfessionId = null,
            FocusTagsJson = JsonSerializer.Serialize(new[] { "90-day", "foundation", "full-program" }),
            DefaultMinutesPerDay = 55,
            TemplateBodyJson = JsonSerializer.Serialize(body),
            IsActive = true,
            Version = 1,
            CreatedBy = "system-seed",
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
