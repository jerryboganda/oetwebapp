using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Seeding;

// Speaking module — hidden card-type taxonomy seed.
//
// Seeds the owner's 6 communication-function card types. The type is HIDDEN
// from students at all times (never serialised to a learner DTO); it is
// surfaced only on admin/tutor paths and passed to the AI scorer + human
// markers as marking guidance.
//
// Owner decision (2026-06-29): seed the communication-function set, but keep
// it fully editable from the admin panel (`/admin/content/speaking/card-types`
// over `/v1/admin/speaking/card-types`). To stay edit/delete-safe and avoid the
// resurrection bug that bit the Writing seeders, this is a ONE-OFF bootstrap:
// it only seeds when the table is completely empty. Once any row exists —
// including rows the admin renamed, reordered, or soft-deleted — the seeder
// no-ops forever, so admin changes are never overwritten or re-introduced on
// restart.
//
// Deterministic ids ("sct-seed-{slug}") let the role-play card seeder map its
// cards to these types by id. Wired into startup from `Program.cs` BEFORE the
// role-play card seeder so the FK target always exists.
public static class SpeakingCardTypeSeed
{
    public const string SeedIdPrefix = "sct-seed-";

    /// <summary>Deterministic id for a seeded card type, e.g.
    /// <c>SeedId("bad-news")</c> → <c>"sct-seed-bad-news"</c>.</summary>
    public static string SeedId(string slug) => $"{SeedIdPrefix}{slug}";

    private static readonly (string Slug, string Name, string Description)[] Types =
    {
        ("diagnosis", "Diagnosis / Explanation",
            "The candidate explains a diagnosis or clinical findings clearly: establishing what the "
            + "patient already knows, checking their reaction, and translating technical detail into "
            + "plain language."),
        ("counselling", "Counselling / Advice",
            "The candidate guides the patient through options or lifestyle/behaviour change, eliciting "
            + "the patient's ideas, concerns and expectations and agreeing a shared, realistic plan."),
        ("reassurance", "Reassurance (anxious patient)",
            "The patient is anxious or worried. The candidate must acknowledge the emotion, give honest "
            + "reassurance, and address the specific fears without dismissing them."),
        ("persuasion", "Persuasion / Adherence (reluctant patient)",
            "The patient resists advice or requests something inappropriate. The candidate negotiates "
            + "respectfully, explains the rationale, and reaches a plan the patient can accept."),
        ("bad-news", "Breaking bad news",
            "The candidate delivers serious or unexpected news with appropriate pacing — a warning shot, "
            + "silence, and emotional support — before moving to next steps."),
        ("health-education", "Health education / promotion",
            "The candidate educates the patient on prevention or self-management, prioritising the "
            + "highest-yield information and confirming understanding."),
    };

    public static async Task SeedAsync(LearnerDbContext db, CancellationToken ct = default)
    {
        // One-off bootstrap: only seed when the table is completely empty so an
        // admin rename / reorder / delete is never overwritten or resurrected.
        var anyExist = await db.SpeakingCardTypes.AsNoTracking().AnyAsync(ct);
        if (anyExist)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var sortOrder = 1;
        foreach (var (slug, name, description) in Types)
        {
            db.SpeakingCardTypes.Add(new SpeakingCardType
            {
                Id = SeedId(slug),
                Name = name,
                Description = description,
                SortOrder = sortOrder++,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
