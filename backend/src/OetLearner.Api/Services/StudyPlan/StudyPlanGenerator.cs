using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Planner;

/// <summary>
/// Materialises a personalised <see cref="StudyPlan"/> for a learner by combining
/// their <see cref="LearnerGoal"/>, recent <see cref="Attempt"/>s, due
/// <see cref="ReviewItem"/>s, and the best matching <see cref="StudyPlanTemplate"/>
/// for their tier. Deterministic over inputs — an identical input snapshot
/// produces an identical plan and is detected via <see cref="GenerationInputsHasher"/>.
/// </summary>
public class StudyPlanGenerator(
    LearnerDbContext db,
    StudyPlanTemplateSelector templateSelector,
    ContentPicker contentPicker,
    ReviewItemInjector reviewItemInjector,
    IStudyPlanEntitlementResolver entitlementResolver,
    ILogger<StudyPlanGenerator> logger,
    IPlanPersonalizer? planPersonalizer = null) : IStudyPlanGenerator
{
    public async Task<StudyPlanGenerationResult> GenerateAsync(
        string userId,
        StudyPlanGenerationTrigger trigger,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException($"Learner {userId} not found.");

        var goal = await db.Goals.FirstOrDefaultAsync(g => g.UserId == userId, cancellationToken);
        var tier = await entitlementResolver.ResolveTierAsync(userId, cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var targetDate = goal?.TargetExamDate ?? today.AddDays(56);
        var daysToExam = Math.Max(7, targetDate.DayNumber - today.DayNumber);
        var totalWeeks = Math.Max(1, (int)Math.Ceiling(daysToExam / 7.0));
        var minutesPerDay = ComputeMinutesPerDay(goal);

        var weakSubtests = ParseWeakSubtests(goal?.WeakSubtestsJson);
        var weights = ComputeWeights(goal, weakSubtests);

        var selection = await templateSelector.SelectAsync(
            totalWeeks,
            tier,
            weakSubtests,
            BandLabel(goal),
            user.ActiveProfessionId,
            cancellationToken);

        if (selection is null)
        {
            logger.LogWarning("No StudyPlanTemplate matched for user {UserId} totalWeeks={Weeks} tier={Tier}. Returning empty plan.",
                userId, totalWeeks, tier);
            return await PersistEmptyPlanAsync(userId, goal, totalWeeks, minutesPerDay, weights, tier, cancellationToken);
        }

        var inputsSnapshot = new
        {
            userId,
            tier,
            totalWeeks,
            minutesPerDay,
            targetDate,
            profession = user.ActiveProfessionId,
            band = BandLabel(goal),
            weakSubtests,
            templateId = selection.Template.Id,
            templateVersion = selection.Template.Version,
            weights
        };
        var inputsHash = GenerationInputsHasher.Hash(inputsSnapshot);

        var existingPlan = await db.StudyPlans
            .Where(p => p.UserId == userId && p.IsActive)
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingPlan is not null
            && trigger != StudyPlanGenerationTrigger.Manual
            && trigger != StudyPlanGenerationTrigger.AdminForce
            && string.Equals(existingPlan.GenerationInputsHash, inputsHash, StringComparison.OrdinalIgnoreCase))
        {
            return new StudyPlanGenerationResult(existingPlan.Id, existingPlan.Version, 0, 0, inputsHash, true, selection.Template.Id);
        }

        var preservedItems = existingPlan is null
            ? new List<StudyPlanItem>()
            : await db.StudyPlanItems
                .Where(i => i.StudyPlanId == existingPlan.Id)
                .Where(i => i.Status == StudyPlanItemStatus.Completed || i.Status == StudyPlanItemStatus.InProgress)
                .ToListAsync(cancellationToken);

        // Mark prior active plan inactive (keeps history).
        if (existingPlan is not null)
        {
            existingPlan.IsActive = false;
        }

        var nextVersion = (existingPlan?.Version ?? 0) + 1;
        var planId = $"plan-{Guid.NewGuid():N}";
        var windowStart = today;
        var windowEnd = today.AddDays(totalWeeks * 7);

        var plan = new StudyPlan
        {
            Id = planId,
            UserId = userId,
            Version = nextVersion,
            GeneratedAt = DateTimeOffset.UtcNow,
            State = AsyncState.Completed,
            Checkpoint = BuildCheckpointMessage(daysToExam, weights),
            WeakSkillFocus = string.Join(", ", weakSubtests),
            ExamFamilyCode = goal?.ExamFamilyCode ?? "oet",
            ExamTypeCode = goal?.ExamTypeCode ?? "OET",
            WeekNumber = 1,
            TotalWeeks = totalWeeks,
            PlanWindowStart = windowStart,
            PlanWindowEnd = windowEnd,
            TemplateId = selection.Template.Id,
            MinutesPerDayBudget = minutesPerDay,
            GenerationInputsHash = inputsHash,
            SubtestWeightsJson = JsonSerializer.Serialize(weights),
            IsPremiumPersonalized = false,
            EntitlementTierAtGeneration = tier,
            IsActive = true
        };
        db.StudyPlans.Add(plan);

        var alreadyPicked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preserved in preservedItems)
        {
            preserved.StudyPlanId = plan.Id;
            if (!string.IsNullOrWhiteSpace(preserved.SourceContentId)) alreadyPicked.Add(preserved.SourceContentId);
            db.StudyPlanItems.Update(preserved);
        }

        var newItems = await MaterialiseAsync(plan, selection, user.ActiveProfessionId, weakSubtests, weights, alreadyPicked, cancellationToken);

        // Phase 4: premium personalization seam. Only run for non-free tiers
        // and only when a personalizer is wired (DI optional).
        if (planPersonalizer is not null
            && !string.Equals(tier, StudyPlanEntitlementResolver.FreeTier, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var tweaks = await planPersonalizer.ApplyAsync(plan, newItems, weights, weakSubtests, cancellationToken);
                if (tweaks > 0)
                {
                    logger.LogInformation("Premium personalizer adjusted {Count} items on plan {PlanId}.", tweaks, plan.Id);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "PlanPersonalizer threw; falling back to rule-engine output for plan {PlanId}.", plan.Id);
            }
        }

        foreach (var item in newItems)
        {
            db.StudyPlanItems.Add(item);
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Generated study plan {PlanId} v{Version} for user {UserId}: {ItemCount} new items, {Preserved} preserved (trigger={Trigger}, template={Template}).",
            plan.Id, plan.Version, userId, newItems.Count, preservedItems.Count, trigger, selection.Template.Slug);

        return new StudyPlanGenerationResult(plan.Id, plan.Version, newItems.Count, preservedItems.Count, inputsHash, false, selection.Template.Id);
    }

    private async Task<List<StudyPlanItem>> MaterialiseAsync(
        StudyPlan plan,
        TemplateSelection selection,
        string? professionId,
        IReadOnlyCollection<string> weakSubtests,
        IReadOnlyDictionary<string, double> weights,
        ISet<string> alreadyPicked,
        CancellationToken cancellationToken)
    {
        var items = new List<StudyPlanItem>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var templateWeeks = selection.Body.Weeks
            .OrderBy(w => w.WeekIndex)
            .ToList();

        if (templateWeeks.Count == 0)
        {
            // No structured body — at least inject due review items so today has tasks.
            await AppendReviewItemsAsync(plan, today, 0, items, plan.UserId, plan.MinutesPerDayBudget, cancellationToken);
            return items;
        }

        var reviewBudgetPerDay = Math.Max(3, plan.MinutesPerDayBudget / 6);

        for (var w = 0; w < plan.TotalWeeks; w++)
        {
            var templateWeek = templateWeeks[Math.Min(w, templateWeeks.Count - 1)];
            for (var d = 0; d < templateWeek.Days.Count; d++)
            {
                var templateDay = templateWeek.Days[d];
                var dueDate = today.AddDays(w * 7 + d);
                if (dueDate > (plan.PlanWindowEnd ?? today.AddYears(1))) continue;

                var section = w == 0 && d == 0
                    ? StudyPlanSections.Today
                    : w == 0 ? StudyPlanSections.ThisWeek : StudyPlanSections.NextCheckpoint;

                var prioritySeed = (w * 10) + d;
                foreach (var slot in templateDay.Slots)
                {
                    if (slot.Kind == StudyPlanSlotKinds.SpacedRepReview)
                    {
                        var injected = await reviewItemInjector.SelectDueAsync(
                            plan.UserId,
                            Math.Max(1, slot.Minutes / 3),
                            slot.Minutes,
                            cancellationToken);

                        foreach (var review in injected)
                        {
                            items.Add(BuildItem(plan, slot, dueDate, section, prioritySeed++, weakSubtests, weights,
                                title: review.Title,
                                subtest: review.SubtestCode,
                                linkedReviewItemId: review.ReviewItemId,
                                contentId: null,
                                sourceContentId: null,
                                contentRoute: "/recalls",
                                durationOverride: review.DurationMinutes));
                        }
                        continue;
                    }

                    var resolved = await contentPicker.ResolveAsync(slot, plan.UserId, professionId, w, alreadyPicked, cancellationToken);
                    items.Add(BuildItem(plan, slot, dueDate, section, prioritySeed++, weakSubtests, weights,
                        title: BuildItemTitle(slot, resolved),
                        subtest: slot.Subtest,
                        linkedReviewItemId: null,
                        contentId: resolved.ContentId,
                        sourceContentId: resolved.ContentId,
                        contentRoute: resolved.Route,
                        durationOverride: resolved.DurationMinutes));
                }

                if (section == StudyPlanSections.Today)
                {
                    await AppendReviewItemsAsync(plan, dueDate, prioritySeed, items, plan.UserId, reviewBudgetPerDay, cancellationToken);
                }
            }
        }

        // Add checkpoint items.
        foreach (var checkpoint in selection.Body.Checkpoints)
        {
            var dueDate = today.AddDays((checkpoint.AfterWeek + 1) * 7);
            if (dueDate > (plan.PlanWindowEnd ?? today.AddYears(1))) continue;
            var subtest = checkpoint.Subtests.FirstOrDefault() ?? StudyPlanSubtestCodes.Mock;
            var slot = new StudyPlanTemplateSlot
            {
                Subtest = subtest,
                Kind = checkpoint.Kind,
                Minutes = checkpoint.Kind == StudyPlanSlotKinds.FullMock ? 180 : 45,
                RationaleHint = $"Week {checkpoint.AfterWeek + 1} checkpoint"
            };
            var resolved = await contentPicker.ResolveAsync(slot, plan.UserId, professionId, checkpoint.AfterWeek, alreadyPicked, cancellationToken);
            items.Add(BuildItem(plan, slot, dueDate, StudyPlanSections.NextCheckpoint, 999, weakSubtests, weights,
                title: BuildItemTitle(slot, resolved),
                subtest: subtest,
                linkedReviewItemId: null,
                contentId: resolved.ContentId,
                sourceContentId: resolved.ContentId,
                contentRoute: resolved.Route,
                durationOverride: resolved.DurationMinutes));
        }

        return items;
    }

    private async Task AppendReviewItemsAsync(
        StudyPlan plan,
        DateOnly dueDate,
        int priorityBase,
        List<StudyPlanItem> items,
        string userId,
        int budgetMinutes,
        CancellationToken cancellationToken)
    {
        var injected = await reviewItemInjector.SelectDueAsync(userId, Math.Max(1, budgetMinutes / 3), budgetMinutes, cancellationToken);
        foreach (var review in injected)
        {
            var slot = new StudyPlanTemplateSlot
            {
                Subtest = review.SubtestCode,
                Kind = StudyPlanSlotKinds.SpacedRepReview,
                Minutes = review.DurationMinutes,
                RationaleHint = "Due review item"
            };
            items.Add(BuildItem(plan, slot, dueDate, StudyPlanSections.Today, priorityBase++, Array.Empty<string>(), new Dictionary<string, double>(),
                title: review.Title,
                subtest: review.SubtestCode,
                linkedReviewItemId: review.ReviewItemId,
                contentId: null,
                sourceContentId: null,
                contentRoute: "/recalls",
                durationOverride: review.DurationMinutes));
        }
    }

    private static StudyPlanItem BuildItem(
        StudyPlan plan,
        StudyPlanTemplateSlot slot,
        DateOnly dueDate,
        string section,
        int priorityScore,
        IReadOnlyCollection<string> weakSubtests,
        IReadOnlyDictionary<string, double> weights,
        string title,
        string subtest,
        string? linkedReviewItemId,
        string? contentId,
        string? sourceContentId,
        string contentRoute,
        int durationOverride)
    {
        var rationale = RationaleBuilder.Build(slot, title, weights, weakSubtests);
        return new StudyPlanItem
        {
            Id = $"plan-item-{Guid.NewGuid():N}",
            StudyPlanId = plan.Id,
            Title = TrimMax(title, 200),
            SubtestCode = subtest,
            DurationMinutes = Math.Max(2, durationOverride),
            Rationale = rationale,
            DueDate = dueDate,
            Status = StudyPlanItemStatus.NotStarted,
            Section = section,
            ContentId = contentId,
            ItemType = MapItemType(slot.Kind),
            SourceContentId = sourceContentId,
            ContentRoute = contentRoute,
            LinkedReviewItemId = linkedReviewItemId,
            PriorityScore = priorityScore,
            WeekIndex = Math.Max(0, (dueDate.DayNumber - (plan.PlanWindowStart?.DayNumber ?? dueDate.DayNumber)) / 7),
            SlotKind = slot.Kind,
            TagsJson = slot.Tags is { Count: > 0 } ? JsonSerializer.Serialize(slot.Tags) : null
        };
    }

    private static string BuildItemTitle(StudyPlanTemplateSlot slot, ResolvedSlotContent resolved)
    {
        if (!string.IsNullOrWhiteSpace(resolved.Title) && resolved.Title.Length > 5)
        {
            return resolved.Title;
        }

        return slot.Kind switch
        {
            StudyPlanSlotKinds.FullMock => $"Full {slot.Subtest} mock",
            StudyPlanSlotKinds.MiniMock => $"{slot.Subtest} mini-mock",
            StudyPlanSlotKinds.ExpertReviewSubmission => $"Submit {slot.Subtest} attempt to tutor",
            StudyPlanSlotKinds.PronunciationDrill => "Pronunciation drill",
            StudyPlanSlotKinds.VocabularyFlashcards => "Vocabulary flashcards",
            _ => $"{Capitalise(slot.Subtest)} practice"
        };
    }

    private async Task<StudyPlanGenerationResult> PersistEmptyPlanAsync(
        string userId,
        LearnerGoal? goal,
        int totalWeeks,
        int minutesPerDay,
        IReadOnlyDictionary<string, double> weights,
        string tier,
        CancellationToken cancellationToken)
    {
        var existing = await db.StudyPlans.FirstOrDefaultAsync(p => p.UserId == userId && p.IsActive, cancellationToken);
        var nextVersion = (existing?.Version ?? 0) + 1;
        if (existing is not null) existing.IsActive = false;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var plan = new StudyPlan
        {
            Id = $"plan-{Guid.NewGuid():N}",
            UserId = userId,
            Version = nextVersion,
            GeneratedAt = DateTimeOffset.UtcNow,
            State = AsyncState.Completed,
            Checkpoint = "Awaiting study-plan templates",
            WeakSkillFocus = "",
            ExamFamilyCode = goal?.ExamFamilyCode ?? "oet",
            ExamTypeCode = goal?.ExamTypeCode ?? "OET",
            WeekNumber = 1,
            TotalWeeks = totalWeeks,
            PlanWindowStart = today,
            PlanWindowEnd = today.AddDays(totalWeeks * 7),
            MinutesPerDayBudget = minutesPerDay,
            SubtestWeightsJson = JsonSerializer.Serialize(weights),
            EntitlementTierAtGeneration = tier,
            IsActive = true
        };
        db.StudyPlans.Add(plan);
        await db.SaveChangesAsync(cancellationToken);
        return new StudyPlanGenerationResult(plan.Id, plan.Version, 0, 0, string.Empty, false, null);
    }

    private static int ComputeMinutesPerDay(LearnerGoal? goal)
    {
        if (goal is null) return 45;
        var weekly = goal.StudyHoursPerWeek * 60;
        if (weekly <= 0) return 45;
        return Math.Clamp(weekly / 7, 15, 240);
    }

    private static IReadOnlyCollection<string> ParseWeakSubtests(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json);
            if (list is null) return Array.Empty<string>();
            return list
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToLowerInvariant())
                .Distinct()
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyDictionary<string, double> ComputeWeights(LearnerGoal? goal, IReadOnlyCollection<string> weakSubtests)
    {
        var subtests = new[] { StudyPlanSubtestCodes.Reading, StudyPlanSubtestCodes.Listening, StudyPlanSubtestCodes.Writing, StudyPlanSubtestCodes.Speaking };
        var raw = new Dictionary<string, double>();
        foreach (var s in subtests)
        {
            var weakWeight = weakSubtests.Contains(s, StringComparer.OrdinalIgnoreCase) ? 1.0 : 0.3;
            var gap = GapForSubtest(goal, s);
            var combined = 0.5 * gap + 0.5 * weakWeight;
            raw[s] = Math.Clamp(combined, 0.1, 0.5);
        }

        var total = raw.Values.Sum();
        if (total <= 0) total = 1.0;
        return raw.ToDictionary(kvp => kvp.Key, kvp => Math.Round(kvp.Value / total, 4));
    }

    private static double GapForSubtest(LearnerGoal? goal, string subtest)
    {
        if (goal is null) return 0.5;
        var target = subtest switch
        {
            StudyPlanSubtestCodes.Writing => goal.TargetWritingScore,
            StudyPlanSubtestCodes.Speaking => goal.TargetSpeakingScore,
            StudyPlanSubtestCodes.Reading => goal.TargetReadingScore,
            StudyPlanSubtestCodes.Listening => goal.TargetListeningScore,
            _ => null
        };
        if (target is null or 0) return 0.5;
        // Without a current readiness signal in this slice we default to mid-gap.
        return 0.5;
    }

    private static string BuildCheckpointMessage(int daysToExam, IReadOnlyDictionary<string, double> weights)
    {
        var topWeak = weights.OrderByDescending(w => w.Value).FirstOrDefault();
        return $"{daysToExam} days to exam. Focus weighting leans toward {topWeak.Key}.";
    }

    private static string MapItemType(string slotKind) => slotKind switch
    {
        StudyPlanSlotKinds.FullMock or StudyPlanSlotKinds.MiniMock => "mock",
        StudyPlanSlotKinds.SpacedRepReview => "review",
        StudyPlanSlotKinds.VocabularyFlashcards => "vocabulary",
        StudyPlanSlotKinds.PronunciationDrill => "pronunciation",
        StudyPlanSlotKinds.ExpertReviewSubmission => "expert-review",
        _ => "practice"
    };

    private static string? BandLabel(LearnerGoal? goal)
    {
        if (goal is null) return null;
        var scores = new[] { goal.TargetWritingScore, goal.TargetSpeakingScore, goal.TargetReadingScore, goal.TargetListeningScore };
        var max = scores.Where(s => s.HasValue).Select(s => s!.Value).DefaultIfEmpty(0).Max();
        return max switch
        {
            >= 400 => "A",
            >= 350 => "B",
            >= 300 => "C+",
            > 0 => "C",
            _ => null
        };
    }

    private static string TrimMax(string s, int max) => s.Length <= max ? s : s[..max];
    private static string Capitalise(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
