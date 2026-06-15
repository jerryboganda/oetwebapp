using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.Writing;

public sealed record WritingDailyPlanItemView(
    Guid Id,
    int Ordinal,
    string ItemKind,
    string? FocusSkill,
    string? FocusCriterion,
    int EstimatedMinutes,
    string Title,
    string Description,
    string ActionHref,
    string? ContentId,
    string Status);

public sealed record WritingDailyPlanView(
    DateOnly Date,
    IReadOnlyList<WritingDailyPlanItemView> Items,
    int TotalMinutes,
    int CompletedCount,
    int RegenerationsRemaining);

public interface IWritingDailyPlanServiceV2
{
    Task<WritingDailyPlanView> GetTodayAsync(string userId, CancellationToken ct);
    Task<WritingDailyPlanView> RegenerateAsync(string userId, CancellationToken ct);
    Task<WritingDailyPlanItemView> MarkCompleteAsync(string userId, Guid itemId, CancellationToken ct);

    // ── V2 endpoint contract adapters ────────────────────────────────────────
    Task<WritingTodayPlanResponseV2> GetTodayPlanAsync(string userId, CancellationToken ct);
    Task<WritingTodayPlanResponseV2> RegenerateTodayPlanAsync(string userId, CancellationToken ct);
    Task<WritingTodayPlanItemResponseV2> MarkItemCompleteAsync(string userId, Guid itemId, CancellationToken ct);
}

public sealed class WritingDailyPlanServiceV2(
    LearnerDbContext db,
    TimeProvider clock,
    IWritingPracticeSelectionService picker,
    IRuntimeSettingsProvider settingsProvider,
    ILogger<WritingDailyPlanServiceV2> logger) : IWritingDailyPlanServiceV2
{
    private const int RegenerationsMetaKey_PayloadVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // DB-over-env daily regen budget (admin-configurable, 30s cache).
    private async Task<int> MaxDailyPlanRegenerationsPerDayAsync(CancellationToken ct)
        => (await settingsProvider.GetAsync(ct)).Writing.MaxDailyPlanRegenerationsPerDay;

    public async Task<WritingDailyPlanView> GetTodayAsync(string userId, CancellationToken ct)
    {
        var date = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var items = await LoadPlanAsync(userId, date, ct);
        if (items.Count == 0)
        {
            items = await GeneratePlanAsync(userId, date, regenerationCount: 0, ct);
        }
        return BuildView(date, items, await MaxDailyPlanRegenerationsPerDayAsync(ct));
    }

    public async Task<WritingDailyPlanView> RegenerateAsync(string userId, CancellationToken ct)
    {
        var date = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var existing = await LoadPlanAsync(userId, date, ct);
        var regenCount = existing.Sum(i => RegenerationCountFromPayload(i.PayloadJson));
        var maxRegens = await MaxDailyPlanRegenerationsPerDayAsync(ct);
        if (regenCount >= maxRegens)
        {
            throw ApiException.Validation(
                "writing_today_regeneration_exhausted",
                "You have used your daily plan regeneration for today.");
        }
        db.WritingDailyPlanItems.RemoveRange(existing);
        await db.SaveChangesAsync(ct);
        var fresh = await GeneratePlanAsync(userId, date, regenCount + 1, ct);
        return BuildView(date, fresh, maxRegens);
    }

    public async Task<WritingDailyPlanItemView> MarkCompleteAsync(string userId, Guid itemId, CancellationToken ct)
    {
        var item = await db.WritingDailyPlanItems.FirstOrDefaultAsync(i => i.Id == itemId && i.UserId == userId, ct)
            ?? throw ApiException.NotFound("writing_plan_item_not_found", "Writing plan item was not found.");
        if (item.Status != "completed")
        {
            item.Status = "completed";
            item.CompletedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
        return ToView(item);
    }

    private async Task<List<WritingDailyPlanItem>> LoadPlanAsync(string userId, DateOnly date, CancellationToken ct)
        => await db.WritingDailyPlanItems
            .Where(i => i.UserId == userId && i.PlanDate == date)
            .OrderBy(i => i.Ordinal)
            .ToListAsync(ct);

    private async Task<List<WritingDailyPlanItem>> GeneratePlanAsync(string userId, DateOnly date, int regenerationCount, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var profile = await db.LearnerWritingProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var pathway = await db.LearnerWritingPathways.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var focus = profile is null ? new List<string>() : DeserializeStringList(profile.LetterTypeFocusJson);
        var weakness = pathway is null ? new Dictionary<string, double>() : DeserializeDoubleMap(pathway.WeaknessVectorJson);

        var picks = await picker.PickAsync(userId, new WritingPracticeSelectionRequest(
            Profession: profile?.Profession ?? "medicine",
            LetterTypeFocus: focus,
            WeaknessVector: weakness,
            DesiredItemCount: 3,
            Difficulty: 3), ct);

        var stage = profile?.CurrentStage ?? "foundation";
        var items = new List<WritingDailyPlanItem>();
        var ordinal = 1;
        items.Add(NewPlanItem(userId, date, ordinal++, "sentence_drill",
            picks.FirstOrDefault()?.FocusSkill ?? "W6",
            picks.FirstOrDefault()?.FocusCriterion ?? "c4",
            10,
            "Sentence drill",
            "Rehearse the sentence-level pattern behind your most frequent Writing weakness.",
            "/writing/drills",
            null, stage, now, regenerationCount));

        var firstLetter = picks.FirstOrDefault(p => p.PickKind == "letter");
        if (firstLetter is not null)
        {
            items.Add(NewPlanItem(userId, date, ordinal++, "full_letter",
                firstLetter.FocusSkill,
                firstLetter.FocusCriterion,
                45,
                $"Full letter",
                "Complete a full letter and submit for grading.",
                $"/writing/practice/session/{firstLetter.ContentRefId}",
                firstLetter.ContentRefId, stage, now, regenerationCount));
        }

        items.Add(NewPlanItem(userId, date, ordinal++, "canon_review",
            "W6", "c4", 5,
            "Review one canon rule",
            "Browse Dr Ahmed's style canon and check your recent recurring violations.",
            "/writing/canon",
            null, stage, now, regenerationCount));

        db.WritingDailyPlanItems.AddRange(items);
        await db.SaveChangesAsync(ct);
        return items;
    }

    private WritingDailyPlanView BuildView(DateOnly date, IReadOnlyList<WritingDailyPlanItem> items, int maxDailyPlanRegenerationsPerDay)
    {
        var remaining = Math.Max(0, maxDailyPlanRegenerationsPerDay - items.Sum(i => RegenerationCountFromPayload(i.PayloadJson)));
        return new WritingDailyPlanView(
            date,
            items.Select(ToView).ToList(),
            items.Sum(i => i.EstimatedMinutes),
            items.Count(i => i.Status == "completed"),
            remaining);
    }

    private static WritingDailyPlanItemView ToView(WritingDailyPlanItem item)
        => new(item.Id, item.Ordinal, item.ItemType, item.FocusSkill, item.FocusCriterion, item.EstimatedMinutes,
            item.Title, item.Description, item.ActionHref, item.ContentId, item.Status);

    private static int RegenerationCountFromPayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("regen", out var el) && el.ValueKind == JsonValueKind.Number
                ? el.GetInt32()
                : 0;
        }
        catch (JsonException) { return 0; }
    }

    private static WritingDailyPlanItem NewPlanItem(
        string userId, DateOnly date, int ordinal, string itemType, string? focusSkill, string? focusCriterion,
        int minutes, string title, string description, string href, string? contentId, string stage, DateTimeOffset now, int regenerationCount)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PlanDate = date,
            Ordinal = ordinal,
            ItemType = itemType,
            FocusSkill = focusSkill,
            FocusCriterion = focusCriterion,
            EstimatedMinutes = minutes,
            Title = title,
            Description = description,
            ActionHref = href,
            ContentId = contentId,
            PayloadJson = JsonSerializer.Serialize(new { stage, regen = regenerationCount, v = RegenerationsMetaKey_PayloadVersion }, JsonOptions),
            Status = "pending",
            CreatedAt = now,
        };

    private static List<string> DeserializeStringList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    private static IReadOnlyDictionary<string, double> DeserializeDoubleMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, double>();
        try { return JsonSerializer.Deserialize<Dictionary<string, double>>(json, JsonOptions) ?? new(); }
        catch (JsonException) { return new Dictionary<string, double>(); }
    }

    public async Task<WritingTodayPlanResponseV2> GetTodayPlanAsync(string userId, CancellationToken ct)
        => WritingV2ResponseMapper.ToResponse(await GetTodayAsync(userId, ct));

    public async Task<WritingTodayPlanResponseV2> RegenerateTodayPlanAsync(string userId, CancellationToken ct)
        => WritingV2ResponseMapper.ToResponse(await RegenerateAsync(userId, ct));

    public async Task<WritingTodayPlanItemResponseV2> MarkItemCompleteAsync(string userId, Guid itemId, CancellationToken ct)
        => WritingV2ResponseMapper.ToResponse(await MarkCompleteAsync(userId, itemId, ct));
}
