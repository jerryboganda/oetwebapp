using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Seeding;

// Phase 3 of the OET Speaking module roadmap.
//
// Seeds the warm-up question pool used by `ConversationHub.SpeakingRoleplay`
// when bootstrapping the unscored warm-up conversation.
//
// Two storage tiers:
//   1. **Code-defined** static dictionary keyed by profession code +
//      a "universal" pool. The hub reads from this dictionary directly
//      so warm-up questions are available even on a fresh database.
//   2. **Database catalogue** marker rows in `SpeakingSharedResource`
//      with `Kind = WarmUpQuestions` so admins can see in the
//      shared-resources UI that the seed has run. The MediaAsset row
//      is a virtual placeholder (StoragePath = `seed://...`) because
//      the actual question text lives in this file. A future admin
//      tool may replace these markers with admin-uploaded PDFs.
//
// Idempotent on the seeded marker id prefix `swu-seed-`.
public static class SpeakingWarmUpSeed
{
    public const string SeedIdPrefix = "swu-seed-";
    private const string SeederUserId = "system-speaking-warmup-seed";
    private const string UniversalKey = "_universal";

    // ─────────────────────────────────────────────────────────────────
    // Code-defined question pool — 15 per top-4 profession + 10 universal
    // ─────────────────────────────────────────────────────────────────

    private static readonly ImmutableDictionary<string, ImmutableArray<string>> Pool = BuildPool();

    /// <summary>
    /// Returns the merged warm-up question pool for the given profession.
    /// Falls back to the universal pool when the profession is unknown.
    /// Profession-specific questions always sort first.
    /// </summary>
    public static IReadOnlyList<string> GetQuestions(string? professionId)
    {
        var key = NormaliseProfession(professionId);
        var universal = Pool[UniversalKey];
        if (string.IsNullOrEmpty(key) || !Pool.TryGetValue(key, out var profQuestions))
        {
            return universal;
        }
        var merged = new List<string>(profQuestions.Length + universal.Length);
        merged.AddRange(profQuestions);
        merged.AddRange(universal);
        return merged;
    }

    /// <summary>
    /// Seeds the database catalogue rows so admins can see the
    /// warm-up question pool entry under shared resources. Safe to call
    /// repeatedly — the seeder probes for the existing seeded markers
    /// before inserting.
    /// </summary>
    public static async Task SeedAsync(LearnerDbContext db, CancellationToken ct = default)
    {
        var alreadySeeded = await db.SpeakingSharedResources
            .AsNoTracking()
            .AnyAsync(r => r.Id.StartsWith(SeedIdPrefix), ct);
        if (alreadySeeded)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var (key, _) in Pool)
        {
            var slug = key == UniversalKey ? "universal" : key;
            var mediaId = $"ma-seed-warmup-{slug}";
            var resourceId = $"{SeedIdPrefix}{slug}";

            // Skip if a placeholder media asset already exists — guards
            // against partial seeds from earlier runs.
            var hasMedia = await db.MediaAssets.AsNoTracking()
                .AnyAsync(m => m.Id == mediaId, ct);
            if (!hasMedia)
            {
                db.MediaAssets.Add(new MediaAsset
                {
                    Id = mediaId,
                    OriginalFilename = $"warmup-{slug}.json",
                    MimeType = "application/json",
                    Format = "json",
                    SizeBytes = 0,
                    StoragePath = $"seed://speaking/warmup/{slug}.json",
                    Status = MediaAssetStatus.Ready,
                    MediaKind = "document",
                    UploadedBy = SeederUserId,
                    UploadedAt = now,
                    ProcessedAt = now,
                });
            }

            db.SpeakingSharedResources.Add(new SpeakingSharedResource
            {
                Id = resourceId,
                Kind = SpeakingSharedResourceKinds.WarmUpQuestions,
                Title = key == UniversalKey
                    ? "Warm-up questions (Universal)"
                    : $"Warm-up questions ({CapitaliseProfession(key)})",
                ProfessionId = key == UniversalKey ? null : key,
                MediaAssetId = mediaId,
                Status = ContentStatus.Published,
                PublishedAt = now,
                EffectiveFrom = now,
                UploadedByUserId = SeederUserId,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    private static string NormaliseProfession(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return raw.Trim().ToLowerInvariant() switch
        {
            "nursing" => "nursing",
            "medicine" or "medical" or "doctor" => "medicine",
            "pharmacy" or "pharmacist" => "pharmacy",
            "physiotherapy" or "physio" => "physiotherapy",
            _ => string.Empty,
        };
    }

    private static string CapitaliseProfession(string key) => key switch
    {
        "nursing" => "Nursing",
        "medicine" => "Medicine",
        "pharmacy" => "Pharmacy",
        "physiotherapy" => "Physiotherapy",
        _ => key,
    };

    private static ImmutableDictionary<string, ImmutableArray<string>> BuildPool()
    {
        return ImmutableDictionary.CreateRange(new[]
        {
            new KeyValuePair<string, ImmutableArray<string>>(UniversalKey, ImmutableArray.Create(
                "Could you tell me your full name, please?",
                "Where are you joining us from today?",
                "Could you spell your family name for me, please?",
                "How long have you been preparing for the OET?",
                "What made you decide to take this exam?",
                "How are you feeling about the test today?",
                "Tell me a little about your typical week.",
                "What do you enjoy doing in your free time?",
                "Have you taken any other English exams before?",
                "Is there anything you would like to ask before we start the role-play?")),

            new KeyValuePair<string, ImmutableArray<string>>("nursing", ImmutableArray.Create(
                "Could you tell me about your current role in nursing?",
                "Which clinical setting do you work in at the moment?",
                "How long have you been working as a nurse?",
                "What kind of patients do you usually look after?",
                "What drew you to nursing originally?",
                "Which country are you hoping to nurse in once you pass OET?",
                "Have you completed any specialist training, for example in critical care or paediatrics?",
                "Tell me about a part of nursing that you find especially rewarding.",
                "What is the most challenging aspect of your shift work?",
                "Do you work primarily in a hospital or in community settings?",
                "How do you typically support a patient who is anxious before a procedure?",
                "What kind of nursing role do you hope to take on next?",
                "How do you usually prepare for a busy shift?",
                "Are you currently working alongside a multidisciplinary team?",
                "What inspired you to broaden your career internationally?")),

            new KeyValuePair<string, ImmutableArray<string>>("medicine", ImmutableArray.Create(
                "Could you tell me about your current role in medicine?",
                "Which speciality are you working in at the moment?",
                "How many years have you been practising?",
                "What kind of clinical setting are you most experienced in?",
                "What attracted you to medicine in the first place?",
                "Which country are you hoping to practise in once you pass OET?",
                "Have you undertaken any postgraduate training so far?",
                "Tell me about a part of medicine that you find especially fulfilling.",
                "What is the most demanding part of your current role?",
                "How do you usually manage long on-call shifts?",
                "What kind of medical role do you hope to step into next?",
                "How do you typically explain complicated diagnoses to patients?",
                "Do you work mainly with adults, children, or both?",
                "Are you involved in any teaching or research alongside clinical work?",
                "What drew you to internationally recognised qualifications such as OET?")),

            new KeyValuePair<string, ImmutableArray<string>>("pharmacy", ImmutableArray.Create(
                "Could you tell me about your current role in pharmacy?",
                "Do you work mainly in community pharmacy or in a hospital?",
                "How long have you been working as a pharmacist?",
                "Which kind of medicines do you dispense most often?",
                "What drew you to pharmacy as a career?",
                "Which country are you hoping to work in after passing OET?",
                "Have you completed any specialist pharmacy training?",
                "Tell me about a part of pharmacy that you find especially rewarding.",
                "What is the most challenging aspect of patient counselling for you?",
                "How do you usually explain side effects to a new patient?",
                "Do you have experience leading a pharmacy team?",
                "How do you keep up with new medications and guidelines?",
                "Have you ever managed a medicines reconciliation on admission?",
                "What kind of pharmacy role do you hope to move into next?",
                "How do you typically support patients with complex regimens?")),

            new KeyValuePair<string, ImmutableArray<string>>("physiotherapy", ImmutableArray.Create(
                "Could you tell me about your current role in physiotherapy?",
                "Which patient group do you work with most often?",
                "How long have you been practising as a physiotherapist?",
                "Do you work mainly in a hospital, a clinic, or community settings?",
                "What drew you to physiotherapy originally?",
                "Which country are you hoping to work in after passing OET?",
                "Have you completed any specialist training, for example in neurorehab or sports physio?",
                "Tell me about a part of physiotherapy that you find especially satisfying.",
                "What is the most challenging part of your caseload at the moment?",
                "How do you usually explain a home-exercise programme to a new patient?",
                "Are you experienced in working with a multidisciplinary rehab team?",
                "What kind of physiotherapy role do you hope to step into next?",
                "How do you motivate a patient who is struggling with their rehab?",
                "Have you ever supervised physiotherapy students or assistants?",
                "What inspired you to seek an internationally recognised qualification?")),
        });
    }
}
