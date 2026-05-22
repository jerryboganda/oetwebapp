using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Planner;

/// <summary>
/// Idempotent seeder: if the StudyPlanTemplates table is empty, inserts three
/// starter templates (free 8-week, premium 12-week targeted, premium retake
/// rescue 4-week) so launch isn't broken. Admin can later edit, duplicate, or
/// delete via the authoring UI without re-running the seeder.
/// </summary>
public class StudyPlanTemplateSeeder(LearnerDbContext db, ILogger<StudyPlanTemplateSeeder> logger)
{
    public async Task SeedIfEmptyAsync(CancellationToken cancellationToken)
    {
        var anyTemplate = await db.StudyPlanTemplates.AnyAsync(cancellationToken);
        if (anyTemplate)
        {
            return;
        }

        logger.LogInformation("StudyPlanTemplates table empty — seeding starter templates.");

        var now = DateTimeOffset.UtcNow;
        var freeTemplate = BuildFreeStandardTemplate(now);
        var premiumTargeted = BuildPremiumTargetedTemplate(now);
        var retakeRescue = BuildPremiumRetakeRescueTemplate(now);

        db.StudyPlanTemplates.AddRange(freeTemplate, premiumTargeted, retakeRescue);

        db.StudyPlanTemplateTiers.AddRange(
            new StudyPlanTemplateTier { Id = $"tpltier-{Guid.NewGuid():N}", TemplateId = freeTemplate.Id, TierCode = StudyPlanEntitlementResolver.FreeTier },
            new StudyPlanTemplateTier { Id = $"tpltier-{Guid.NewGuid():N}", TemplateId = freeTemplate.Id, TierCode = StudyPlanEntitlementResolver.PremiumTier },
            new StudyPlanTemplateTier { Id = $"tpltier-{Guid.NewGuid():N}", TemplateId = freeTemplate.Id, TierCode = StudyPlanEntitlementResolver.EliteTier },
            new StudyPlanTemplateTier { Id = $"tpltier-{Guid.NewGuid():N}", TemplateId = premiumTargeted.Id, TierCode = StudyPlanEntitlementResolver.PremiumTier },
            new StudyPlanTemplateTier { Id = $"tpltier-{Guid.NewGuid():N}", TemplateId = premiumTargeted.Id, TierCode = StudyPlanEntitlementResolver.EliteTier },
            new StudyPlanTemplateTier { Id = $"tpltier-{Guid.NewGuid():N}", TemplateId = retakeRescue.Id, TierCode = StudyPlanEntitlementResolver.PremiumTier },
            new StudyPlanTemplateTier { Id = $"tpltier-{Guid.NewGuid():N}", TemplateId = retakeRescue.Id, TierCode = StudyPlanEntitlementResolver.EliteTier });

        await db.SaveChangesAsync(cancellationToken);
    }

    private static StudyPlanTemplate BuildFreeStandardTemplate(DateTimeOffset now)
    {
        var body = new StudyPlanTemplateBody
        {
            Weeks = Enumerable.Range(0, 8).Select(w => new StudyPlanTemplateWeek
            {
                WeekIndex = w,
                Label = w switch
                {
                    0 => "Foundation",
                    1 => "Pace + Accuracy",
                    2 => "Writing Mechanics",
                    3 => "Speaking Fluency",
                    4 => "Mid-Plan Mock",
                    5 => "Targeted Drilling",
                    6 => "Mock Conditions",
                    _ => "Final Polish"
                },
                Days = BuildBalancedWeek(w)
            }).ToList(),
            Checkpoints = new List<StudyPlanTemplateCheckpoint>
            {
                new() { AfterWeek = 3, Kind = StudyPlanSlotKinds.MiniMock, Subtests = new List<string>{StudyPlanSubtestCodes.Reading, StudyPlanSubtestCodes.Listening} },
                new() { AfterWeek = 6, Kind = StudyPlanSlotKinds.FullMock, Subtests = new List<string>{StudyPlanSubtestCodes.Reading, StudyPlanSubtestCodes.Listening, StudyPlanSubtestCodes.Writing, StudyPlanSubtestCodes.Speaking} }
            }
        };

        return new StudyPlanTemplate
        {
            Id = "tmpl-free-8wk-standard",
            Slug = "free-8wk-standard",
            Name = "Free Tier — Standard 8-Week",
            Description = "Balanced 8-week plan across all four OET subtests for free-tier learners.",
            ExamTypeCode = "OET",
            ExamFamilyCode = "oet",
            MinWeeks = 6,
            MaxWeeks = 10,
            TargetBand = null,
            ProfessionId = null,
            FocusTagsJson = JsonSerializer.Serialize(new[] { "balanced", "starter" }),
            DefaultMinutesPerDay = 60,
            TemplateBodyJson = JsonSerializer.Serialize(body),
            IsActive = true,
            Version = 1,
            CreatedBy = "system-seed",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static StudyPlanTemplate BuildPremiumTargetedTemplate(DateTimeOffset now)
    {
        var body = new StudyPlanTemplateBody
        {
            Weeks = Enumerable.Range(0, 12).Select(w => new StudyPlanTemplateWeek
            {
                WeekIndex = w,
                Label = $"Block {w / 4 + 1} — Week {(w % 4) + 1}",
                Days = BuildPremiumWeek(w)
            }).ToList(),
            Checkpoints = new List<StudyPlanTemplateCheckpoint>
            {
                new() { AfterWeek = 3, Kind = StudyPlanSlotKinds.MiniMock, Subtests = new List<string>{StudyPlanSubtestCodes.Reading} },
                new() { AfterWeek = 7, Kind = StudyPlanSlotKinds.FullMock, Subtests = new List<string>{StudyPlanSubtestCodes.Reading, StudyPlanSubtestCodes.Listening, StudyPlanSubtestCodes.Writing, StudyPlanSubtestCodes.Speaking} },
                new() { AfterWeek = 10, Kind = StudyPlanSlotKinds.FullMock, Subtests = new List<string>{StudyPlanSubtestCodes.Reading, StudyPlanSubtestCodes.Listening, StudyPlanSubtestCodes.Writing, StudyPlanSubtestCodes.Speaking} }
            }
        };

        return new StudyPlanTemplate
        {
            Id = "tmpl-prem-12wk-targeted",
            Slug = "premium-12wk-targeted",
            Name = "Premium — 12-Week Targeted",
            Description = "Premium-tier 12-week plan with heavier weak-skill focus, weekly mini-mocks and expert review cadence.",
            ExamTypeCode = "OET",
            ExamFamilyCode = "oet",
            MinWeeks = 10,
            MaxWeeks = 16,
            TargetBand = "B",
            ProfessionId = null,
            FocusTagsJson = JsonSerializer.Serialize(new[] { "premium", "weak-skill-focus" }),
            DefaultMinutesPerDay = 75,
            TemplateBodyJson = JsonSerializer.Serialize(body),
            IsActive = true,
            Version = 1,
            CreatedBy = "system-seed",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static StudyPlanTemplate BuildPremiumRetakeRescueTemplate(DateTimeOffset now)
    {
        var body = new StudyPlanTemplateBody
        {
            Weeks = Enumerable.Range(0, 4).Select(w => new StudyPlanTemplateWeek
            {
                WeekIndex = w,
                Label = w switch { 0 => "Diagnose", 1 => "Stabilise", 2 => "Push Hard", _ => "Final Sprint" },
                Days = BuildRetakeRescueWeek(w)
            }).ToList(),
            Checkpoints = new List<StudyPlanTemplateCheckpoint>
            {
                new() { AfterWeek = 1, Kind = StudyPlanSlotKinds.MiniMock, Subtests = new List<string>{StudyPlanSubtestCodes.Writing, StudyPlanSubtestCodes.Speaking} },
                new() { AfterWeek = 2, Kind = StudyPlanSlotKinds.FullMock, Subtests = new List<string>{StudyPlanSubtestCodes.Reading, StudyPlanSubtestCodes.Listening, StudyPlanSubtestCodes.Writing, StudyPlanSubtestCodes.Speaking} },
                new() { AfterWeek = 3, Kind = StudyPlanSlotKinds.FullMock, Subtests = new List<string>{StudyPlanSubtestCodes.Reading, StudyPlanSubtestCodes.Listening, StudyPlanSubtestCodes.Writing, StudyPlanSubtestCodes.Speaking} }
            }
        };

        return new StudyPlanTemplate
        {
            Id = "tmpl-prem-4wk-retake",
            Slug = "premium-4wk-retake",
            Name = "Premium — Retake Rescue 4-Week",
            Description = "High-intensity 4-week plan for retakes: daily expert review, diagnostic-first, mock-heavy.",
            ExamTypeCode = "OET",
            ExamFamilyCode = "oet",
            MinWeeks = 2,
            MaxWeeks = 5,
            TargetBand = "B",
            ProfessionId = null,
            FocusTagsJson = JsonSerializer.Serialize(new[] { "retake-rescue", "intensive", "premium" }),
            DefaultMinutesPerDay = 90,
            TemplateBodyJson = JsonSerializer.Serialize(body),
            IsActive = true,
            Version = 1,
            CreatedBy = "system-seed",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static List<StudyPlanTemplateDay> BuildBalancedWeek(int weekIndex)
    {
        var days = new[] { "mon", "tue", "wed", "thu", "fri", "sat", "sun" };
        return days.Select((d, idx) =>
        {
            var slots = new List<StudyPlanTemplateSlot>
            {
                new() { Subtest = StudyPlanSubtestCodes.Vocabulary, Kind = StudyPlanSlotKinds.SpacedRepReview, Minutes = 15, RationaleHint = "Daily vocab review" }
            };

            switch (idx)
            {
                case 0:
                    slots.Add(new StudyPlanTemplateSlot { Subtest = StudyPlanSubtestCodes.Reading, Kind = StudyPlanSlotKinds.NextUnattemptedPaper, Minutes = 30, RationaleHint = "Build reading pace" });
                    slots.Add(new StudyPlanTemplateSlot { Subtest = StudyPlanSubtestCodes.Writing, Kind = StudyPlanSlotKinds.DrillByTag, Minutes = 25, Tags = new List<string> { "referral-letter" } });
                    break;
                case 1:
                    slots.Add(new StudyPlanTemplateSlot { Subtest = StudyPlanSubtestCodes.Listening, Kind = StudyPlanSlotKinds.NextUnattemptedPaper, Minutes = 30, RationaleHint = "Listening accuracy" });
                    slots.Add(new StudyPlanTemplateSlot { Subtest = StudyPlanSubtestCodes.Speaking, Kind = StudyPlanSlotKinds.PronunciationDrill, Minutes = 15 });
                    break;
                case 2:
                    slots.Add(new StudyPlanTemplateSlot { Subtest = StudyPlanSubtestCodes.Writing, Kind = StudyPlanSlotKinds.NextUnattemptedPaper, Minutes = 40, RationaleHint = "Full writing task" });
                    break;
                case 3:
                    slots.Add(new StudyPlanTemplateSlot { Subtest = StudyPlanSubtestCodes.Speaking, Kind = StudyPlanSlotKinds.DrillByTag, Minutes = 30, Tags = new List<string> { "role-play" } });
                    slots.Add(new StudyPlanTemplateSlot { Subtest = StudyPlanSubtestCodes.Reading, Kind = StudyPlanSlotKinds.DrillByTag, Minutes = 20 });
                    break;
                case 4:
                    slots.Add(new StudyPlanTemplateSlot { Subtest = StudyPlanSubtestCodes.Listening, Kind = StudyPlanSlotKinds.DrillByTag, Minutes = 30 });
                    slots.Add(new StudyPlanTemplateSlot { Subtest = StudyPlanSubtestCodes.Vocabulary, Kind = StudyPlanSlotKinds.VocabularyFlashcards, Minutes = 15 });
                    break;
                case 5:
                    slots.Add(new StudyPlanTemplateSlot { Subtest = StudyPlanSubtestCodes.Reading, Kind = StudyPlanSlotKinds.NextUnattemptedPaper, Minutes = 45 });
                    break;
                case 6:
                    slots.Add(new StudyPlanTemplateSlot { Subtest = StudyPlanSubtestCodes.Mock, Kind = StudyPlanSlotKinds.MiniMock, Minutes = 45, RationaleHint = "Weekly checkpoint" });
                    break;
            }

            return new StudyPlanTemplateDay { DayOfWeek = d, Slots = slots };
        }).ToList();
    }

    private static List<StudyPlanTemplateDay> BuildPremiumWeek(int weekIndex)
    {
        var days = BuildBalancedWeek(weekIndex);
        // Premium adds an expert-review submission every Wednesday after week 1.
        if (weekIndex >= 1 && days.Count > 2)
        {
            days[2].Slots.Add(new StudyPlanTemplateSlot
            {
                Subtest = StudyPlanSubtestCodes.Writing,
                Kind = StudyPlanSlotKinds.ExpertReviewSubmission,
                Minutes = 10,
                RationaleHint = "Premium tutor review"
            });
        }
        return days;
    }

    private static List<StudyPlanTemplateDay> BuildRetakeRescueWeek(int weekIndex)
    {
        var days = new[] { "mon", "tue", "wed", "thu", "fri", "sat", "sun" };
        return days.Select((d, idx) =>
        {
            var slots = new List<StudyPlanTemplateSlot>
            {
                new() { Subtest = StudyPlanSubtestCodes.Vocabulary, Kind = StudyPlanSlotKinds.SpacedRepReview, Minutes = 15 }
            };
            slots.Add(new StudyPlanTemplateSlot
            {
                Subtest = idx % 2 == 0 ? StudyPlanSubtestCodes.Writing : StudyPlanSubtestCodes.Speaking,
                Kind = StudyPlanSlotKinds.WeakSkillFocus,
                Minutes = 45
            });
            slots.Add(new StudyPlanTemplateSlot
            {
                Subtest = idx < 3 ? StudyPlanSubtestCodes.Reading : StudyPlanSubtestCodes.Listening,
                Kind = StudyPlanSlotKinds.NextUnattemptedPaper,
                Minutes = 30
            });
            if (idx == 6)
            {
                slots.Add(new StudyPlanTemplateSlot
                {
                    Subtest = StudyPlanSubtestCodes.Writing,
                    Kind = StudyPlanSlotKinds.ExpertReviewSubmission,
                    Minutes = 10
                });
            }
            return new StudyPlanTemplateDay { DayOfWeek = d, Slots = slots };
        }).ToList();
    }
}
