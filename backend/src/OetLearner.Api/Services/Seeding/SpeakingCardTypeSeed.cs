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

    // The canonical taxonomy is VISIT TYPE, matching `tables.cardTypes` in
    // rulebooks/speaking/{profession}/rulebook.v1.json (authored by the owner)
    // and the way the owner's own source card sets are organised. The slugs
    // below are identical to the rulebook's so the rule engine, the AI scorer
    // and this table all name a card type the same way.
    //
    // (Before 2026-09 this seeded a *communication-function* taxonomy —
    // Diagnosis / Counselling / Reassurance / Persuasion / Bad news / Health
    // education — which matched neither the rulebook nor the source material,
    // so every card imported from the owner's sets would have been typed
    // against the wrong axis. See the accompanying migration for how existing
    // databases are realigned.)
    private static readonly (string Slug, string Name, string Description)[] Types =
    {
        ("first-visit-routine", "First visit — routine",
            "The patient is presenting for the first time with a non-urgent problem. The candidate takes "
            + "a history from scratch, establishes the reason for attendance, and works toward a "
            + "provisional explanation and plan."),
        ("first-visit-emergency", "First visit — emergency",
            "A first presentation in an urgent or emergency setting. The candidate must calm the patient "
            + "or relative, gather the critical history quickly, and explain immediate management without "
            + "losing empathy under time pressure."),
        ("follow-up", "Follow-up (second visit)",
            "The patient is returning for review of a problem already diagnosed or treated. The candidate "
            + "checks progress since the last visit, responds to what has changed, and adjusts the plan. "
            + "Look for 'coming back for', 'returning for', 'follow-up', 'review of' (see RULE_35)."),
        ("examination", "Examination card",
            "The card requires the candidate to explain, seek consent for, or act on a physical "
            + "examination — describing what will happen, why it is needed, and what was found."),
        ("already-known-patient", "Already known patient",
            "The candidate already knows this patient or has their notes to hand, so no full history is "
            + "needed. The task centres on addressing specific questions, concerns or new information."),
        ("breaking-bad-news", "Breaking bad news",
            "The candidate delivers serious or unexpected news with appropriate pacing — a warning shot, "
            + "silence, and emotional support — before moving to next steps (see rulebook section 06)."),
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
