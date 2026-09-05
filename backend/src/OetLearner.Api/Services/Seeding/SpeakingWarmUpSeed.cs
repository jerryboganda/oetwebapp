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
    // Code-defined question pool — 15 per profession + the universal set
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// One warm-up prompt. <paramref name="ModelAnswer"/> is the sample answer a
    /// learner can study after the unscored warm-up; it is <c>null</c> for
    /// prompts that have no published model answer.
    ///
    /// <para>Before 2026-09 the pool was a bare <c>ImmutableArray&lt;string&gt;</c>,
    /// so the owner's intro-question material — which is authored as
    /// question + model-answer PAIRS — had nowhere to live and only half of it
    /// could be represented.</para>
    /// </summary>
    public sealed record WarmUpPrompt(string Question, string? ModelAnswer = null);

    private static readonly ImmutableDictionary<string, ImmutableArray<WarmUpPrompt>> Pool = BuildPool();

    /// <summary>
    /// Returns the merged warm-up prompts for the given profession, model
    /// answers included. Falls back to the universal pool when the profession
    /// is unknown. Profession-specific prompts always sort first.
    /// </summary>
    public static IReadOnlyList<WarmUpPrompt> GetPrompts(string? professionId)
    {
        var key = NormaliseProfession(professionId);
        var universal = Pool[UniversalKey];
        if (string.IsNullOrEmpty(key) || !Pool.TryGetValue(key, out var profPrompts))
        {
            return universal;
        }
        var merged = new List<WarmUpPrompt>(profPrompts.Length + universal.Length);
        merged.AddRange(profPrompts);
        merged.AddRange(universal);
        return merged;
    }

    /// <summary>
    /// Question text only — the shape <c>ConversationHub.SpeakingRoleplay</c>
    /// consumes when driving the warm-up conversation.
    /// </summary>
    public static IReadOnlyList<string> GetQuestions(string? professionId)
        => GetPrompts(professionId).Select(p => p.Question).ToList();

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

    /// <summary>Question-only prompts (no published model answer yet).</summary>
    private static ImmutableArray<WarmUpPrompt> ToPrompts(params string[] questions)
        => questions.Select(q => new WarmUpPrompt(q)).ToImmutableArray();

    /// <summary>
    /// Canonicalise a profession id onto a pool key. Every OET profession in
    /// <c>PROFESSION_CATALOG</c> is recognised, so a profession that has no
    /// dedicated pool yet still resolves to its own key rather than being
    /// silently flattened to "unknown" — adding a pool later just works.
    /// Unrecognised ids fall back to the universal pool.
    /// </summary>
    private static string NormaliseProfession(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return raw.Trim().ToLowerInvariant() switch
        {
            "nursing" or "nurse" => "nursing",
            "medicine" or "medical" or "doctor" => "medicine",
            "pharmacy" or "pharmacist" => "pharmacy",
            "physiotherapy" or "physio" => "physiotherapy",
            "dentistry" or "dental" or "dentist" => "dentistry",
            "radiography" or "radiographer" => "radiography",
            "other-allied-health" or "allied-health" => "other-allied-health",
            _ => string.Empty,
        };
    }

    private static string CapitaliseProfession(string key) => key switch
    {
        "nursing" => "Nursing",
        "medicine" => "Medicine",
        "pharmacy" => "Pharmacy",
        "physiotherapy" => "Physiotherapy",
        "dentistry" => "Dentistry",
        "radiography" => "Radiography",
        "other-allied-health" => "Modified Allied Health Profession",
        _ => key,
    };

    private static ImmutableDictionary<string, ImmutableArray<WarmUpPrompt>> BuildPool()
    {
        return ImmutableDictionary.CreateRange(new[]
        {
            // The universal set is the owner's own authoritative intro-question
            // material ("Speaking Intro Qs — SAME FOR ALL PROFESSIONS"),
            // transcribed VERBATIM including its original wording and typos.
            // Do not "correct" these strings: they are published study material
            // and are expected to match the owner's PDF word for word.
            //
            // The model answers are exemplars written in a doctor's voice (that
            // is how the source authors them). They are shown to the learner as
            // sample answers, not as something to recite.
            new KeyValuePair<string, ImmutableArray<WarmUpPrompt>>(UniversalKey, ImmutableArray.Create(
                new WarmUpPrompt("What is your name?",
                    "My name is (your first name + last name)"),
                new WarmUpPrompt("What is your profession?",
                    "My profession is medicine"),
                new WarmUpPrompt("Why are you taking the OET?",
                    "Honestly I'm taking the OET to complete Australian medical council registration process "
                    + "because it is one of the two tests required to practice medicine in Australia. Practicing "
                    + "medicine in Australia is one of my dreams to upgrade myself in my career. In Australia i "
                    + "can get advanced training and recent modalities of diagnosis and treatment. I am also "
                    + "searching for a better chance of education for my children who are an essential part of my "
                    + "life so I hope to pass the OET as soon as possible to start practicing medicine in Australia."),
                new WarmUpPrompt("How long have you been working as a physician?",
                    "I've been working as a physician for 10 years. 5 years were in Egypt and the other 5 years "
                    + "were in United Arab Emirates."),
                new WarmUpPrompt("What about your working hours?",
                    "I'm working for 8 hours daily from Saturday to Thursday so I'm working a bout 48 hours per "
                    + "week. Unfortunately, I've no enough time to spend with my family."),
                new WarmUpPrompt("Why did you choose medicine as a career?",
                    "Actually, it was my childhood dream and it was Also a dream of my family especially my mother "
                    + "so I studied hard to a achieve my target. My first day in the faculty of medicine was one of "
                    + "the happiest days in my life. I like medicine a lot because it gives me the chance to help "
                    + "the others."),
                new WarmUpPrompt("What about your specialty? And why?",
                    "I like all branches of medicine so I was hesitated to choose a specific specialty and Leave "
                    + "the others. Finally I found my target in family medicine because it allows me to practice "
                    + "all branches of medicine and to deal with a variety of cases which in turn help me to feel "
                    + "satisfied."),
                new WarmUpPrompt("What is your advice for fresh graduates?",
                    "Ooh! the most important advice I give to them is to choose their specialty carefully by "
                    + "choosing what they actually like because they will spend the rest of their life practicing "
                    + "it. I also advise them to outline their target at an early stage to avoid wasting their time "
                    + "so I recommend them to find out the different styles of post graduation qualifications "
                    + "before choosing a specific one and to keep updated with the recent guidelines to help people well."),
                new WarmUpPrompt("How to be a successful physician?",
                    "Ooh! What a difficult question! from my point of view I think that success in the field of "
                    + "medicine mainly depends on early and proper planning for your career pathway. You should "
                    + "fulfill two elements. The first element is the good planning which will save time and effort "
                    + "for you and the second one is hard continuous working. You should also have a lot of skills "
                    + "like being a good listener, showing sympathy to your patients, respecting the patients' time "
                    + "and confidentiality, building a trust bond between you and the patients and continuously "
                    + "updating yourself with the new guidelines"),
                new WarmUpPrompt("What was the last training you had?",
                    "I'm keen to get frequent training courses. The last one that I had was about \"advanced cardiac "
                    + "life support\" which was about one month ago and implied how to perform a cardiac and "
                    + "respiratory support in case of cardiac arrest. It also taught us how to deal with the cases "
                    + "of life threatening arrhythmias. It was really a valuable course."),
                new WarmUpPrompt("What is the most recent medical advance you heard about?",
                    "No doubt that the medical field is one of the fastest developing fields in the world. Nearly "
                    + "every month there are new researches, theories and guidelines. Diabetes mellitus treatment is "
                    + "one of the most important tasks that is developing rapidly. I heard about a new trend of the "
                    + "treatment of diabetic patients by putting a pump of insulin under their skin to release proper "
                    + "amounts of insulin according to their need which will help them to get rid of the needles "
                    + "pricks and gain a good control of their blood glucose level all over the day."))),

            new KeyValuePair<string, ImmutableArray<WarmUpPrompt>>("nursing", ToPrompts(
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

            new KeyValuePair<string, ImmutableArray<WarmUpPrompt>>("medicine", ToPrompts(
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

            new KeyValuePair<string, ImmutableArray<WarmUpPrompt>>("pharmacy", ToPrompts(
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

            new KeyValuePair<string, ImmutableArray<WarmUpPrompt>>("physiotherapy", ToPrompts(
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
