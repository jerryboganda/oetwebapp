using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// ListeningContentSeeder — Phase 2 of OET_LISTENING_MODULE_PATHWAY.md §7+§16.
//
// Idempotently inserts the 8 foundation lessons (one per L1..L8 sub-skill) and
// 12 strategy articles (2 per category × 6 categories) the learner-facing
// lesson player and strategy library render against. Re-runs are no-ops: the
// seeder matches existing rows by Slug and only upserts the body when the
// row is missing.
//
// Enable via:   Seed:ListeningContent:Enabled = true
//
// The body markdown is deliberately rich (~600 words for lessons, ~400 words
// for strategies) so the learner sees real OET-flavoured content even before
// the content team ships their hand-authored bodies. Each lesson body covers:
//
//   • Why the sub-skill matters in OET Listening
//   • The exam contexts where it shows up (Part A consultation gap-fills,
//     Part B workplace MCQ, Part C presentation MCQ)
//   • A short healthcare-themed worked example with a doctor / nurse /
//     pharmacist / physio dialogue
//   • Drill instructions covering the three graduated drills
//   • A mini-quiz reminder (pass = 4/5)
//
// Strategy bodies follow a tighter "what / when / how / common mistakes" shape
// so they read as field guides rather than full lessons.
// ═════════════════════════════════════════════════════════════════════════════

public sealed class ListeningContentSeederOptions
{
    public const string SectionName = "Seed:ListeningContent";

    /// <summary>If false, the seeder no-ops. Default false so CI / production
    /// containers without operator opt-in remain unaffected.</summary>
    public bool Enabled { get; set; } = false;
}

public sealed class ListeningContentSeeder(
    LearnerDbContext db,
    IOptions<ListeningContentSeederOptions> opts,
    ILogger<ListeningContentSeeder> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
    };

    public async Task<int> SeedAsync(CancellationToken ct)
    {
        var options = opts.Value;
        if (!options.Enabled)
        {
            logger.LogInformation(
                "ListeningContentSeeder disabled (Seed:ListeningContent:Enabled=false); skipping.");
            return 0;
        }

        var lessonsInserted = await SeedLessonsAsync(ct);
        var strategiesInserted = await SeedStrategiesAsync(ct);

        var total = lessonsInserted + strategiesInserted;
        logger.LogInformation(
            "ListeningContentSeeder upserted {LessonCount} lessons + {StrategyCount} strategies (total {Total}).",
            lessonsInserted, strategiesInserted, total);
        return total;
    }

    // ── Lessons (8 sub-skill bootcamps) ─────────────────────────────────────

    private async Task<int> SeedLessonsAsync(CancellationToken ct)
    {
        var lessons = BuildLessons();

        // Pull existing slugs once so we know which ones to skip without
        // round-tripping per lesson.
        var existingSlugs = await db.ListeningLessons
            .AsNoTracking()
            .Where(l => lessons.Select(x => x.Slug).Contains(l.Slug))
            .Select(l => l.Slug)
            .ToListAsync(ct);
        var existingSet = new HashSet<string>(existingSlugs, StringComparer.Ordinal);

        var added = 0;
        foreach (var lesson in lessons)
        {
            if (existingSet.Contains(lesson.Slug))
            {
                continue;
            }
            db.ListeningLessons.Add(lesson);
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        return added;
    }

    private static List<ListeningLesson> BuildLessons()
    {
        // Sub-skill metadata mirrors the labels in
        // ListeningPathwayEndpoints.SkillLabels so the lesson UI stays aligned
        // with the skill-radar legend.
        return new List<ListeningLesson>
        {
            BuildLesson(
                "L1",
                1,
                "Detail capture — hearing the exact words",
                "L1 trains your ability to lock onto the literal information a speaker says: dates, dosages, names, addresses, lab values. In OET Part A you transcribe what the patient and consultant say into 24 short-answer gap-fills, and a single missed number can cost you a band. Detail capture is the foundation skill the diagnostic weights most heavily because every other sub-skill builds on top of it.\n\n## Where it shows up in OET\n\nPart A is the obvious home of detail capture: a consultation between a healthcare professional and a patient, with you transcribing answers like *temperature 38.5°C*, *medication: ibuprofen 200mg*, *next appointment: 14 March*. But L1 also matters in Part B and C — distractor MCQ stems often hinge on a single contradicting detail (\"the patient reports the pain started **two** weeks ago, not four\") and you can't recognise the distractor unless you heard the number.\n\n## Healthcare-flavoured worked example\n\n> *Doctor (British male):* \"Right, Mrs Khan, you've been taking the **metformin 500 milligrams** twice a day since the second of January — is that right?\"\n> *Patient:* \"Yes, with food, just like you said.\"\n> *Doctor:* \"Good. And your blood sugars in the morning have been around **7.2 millimoles**, you said.\"\n\nA Part A gap-fill on this extract might ask you to complete: *Medication: ____________ twice daily.* The correct answer is **metformin 500mg** (the drug + dose unit). A learner who misses the dose, or writes \"metformin only\", loses the mark even though they heard the gist.\n\n## How the three drills build the skill\n\n- **Drill 1 — Numbers & quantities (easy):** five short clips of doctor / nurse turns with a single number to capture. Spelling tolerance is on; you just have to get the digits right.\n- **Drill 2 — Mixed details (medium):** seven clips combining names, addresses, dosages and times. You'll start to notice the rhythm of an OET clinician dictating a discharge summary.\n- **Drill 3 — Long-form details (hard):** three 40-second consultation snippets where you transcribe four answers each. Pace matches Phase 1 diagnostic difficulty.\n\n## Common detail-capture mistakes\n\n1. **Paraphrasing instead of transcribing.** If the speaker says \"three times a day\" don't write *t.d.s.* — OET marks the exact spoken form.\n2. **Hearing the stressed syllable but missing the prefix.** *Hypertension* and *hypotension* sound nearly identical at speed; relying on context is safer than relying on the consonant.\n3. **Filling in plausible details from medical knowledge.** A learner who knows metformin is usually 500mg may auto-write that even when the speaker said 850mg.\n\n## The mini-quiz\n\nFive short clips, one detail each. You need 4 out of 5 to complete the lesson. There is no replay — the diagnostic doesn't allow replays either, so this is good prep. If you score 3 or below, the lesson will stay marked as in-progress and a remediation drill will appear on tomorrow's daily plan.",
                drillIds: new[] { "drill-l1-numbers", "drill-l1-mixed", "drill-l1-long-form" },
                quizIds: new[] { "quiz-l1-q1", "quiz-l1-q2", "quiz-l1-q3", "quiz-l1-q4", "quiz-l1-q5" }),

            BuildLesson(
                "L2",
                2,
                "Note-taking speed — keeping up at clinic pace",
                "L2 is the cousin of L1: where detail capture asks *did you hear it?*, note-taking speed asks *can you write it fast enough to keep up?*. OET Part A runs at a natural consultation pace (around 160 words per minute) and the gap-fills appear in the order the speakers raise them. If your pen / keyboard speed lags, you will lose the second half of an extract while still scribbling the first.\n\n## Where it shows up in OET\n\n- **Part A** demands continuous note-taking for 8 minutes per extract.\n- **Part B** doesn't need notes per se, but a 15-second prep window between extracts vanishes if you're still finishing answers from the previous one.\n- **Part C** is monologue-heavy; you can't note everything, so triage is essential.\n\n## Healthcare-flavoured worked example\n\n> *Pharmacist (Australian female):* \"So the GP's started Mrs Lopez on **ramipril** for her blood pressure, and she's also picking up the usual **atorvastatin** — I noticed her last refill was four months ago, so I want to confirm she's still taking it. Could you do a brief medication review?\"\n\nIn 12 seconds the pharmacist has delivered: two drug names, two clinical conditions, a refill gap, and a delegated task. A learner without trained note-taking will write *ramipril*, miss *atorvastatin*, and remember *medication review* only as a vague impression.\n\n## How the three drills build the skill\n\n- **Drill 1 — Telegraphic style:** practise dropping articles, copulas, and adverbs (*pt c/o chest pain → onset 2hr*). You'll halve your stroke count without losing meaning.\n- **Drill 2 — Numeric-heavy turns:** five clips where the bulk of information is numbers. Train your fingers to type digits as fast as letters.\n- **Drill 3 — Dual-speaker pace:** consultations where the doctor and patient overlap. You learn which speaker to attend to first.\n\n## Common note-taking mistakes\n\n1. **Writing in full sentences.** Cuts your pace in half. *The patient reports nausea since yesterday* should be *N+ since yest.*\n2. **Trying to make notes legible mid-flow.** Triage now, tidy later — the next gap-fill is already arriving.\n3. **Switching between Arabic and English.** Bilingual notes are fine, but switching languages mid-clause costs ~600ms.\n\n## The mini-quiz\n\nFive short clips with a target note (provided) and a 7-second window to type it. You need 4 out of 5 to pass.",
                drillIds: new[] { "drill-l2-telegraphic", "drill-l2-numeric", "drill-l2-dual" },
                quizIds: new[] { "quiz-l2-q1", "quiz-l2-q2", "quiz-l2-q3", "quiz-l2-q4", "quiz-l2-q5" }),

            BuildLesson(
                "L3",
                3,
                "Spelling accuracy — getting the marks you earned",
                "L3 protects the answers you already heard correctly. OET Part A is marked on spelling: *hypertention*, *paracetimol*, *Tylonol* — all heard right, all wrong on the marking key. L3 is the cheapest band a learner can buy: 20 minutes of targeted drilling can lift a Part A score by half a band.\n\n## Where it shows up in OET\n\n- **Part A short-answer items.** Spelling-sensitive, no tolerance for transposed letters.\n- **Wherever a learner writes a free-text term in the writing module.** Same vocabulary stock.\n\n## Healthcare-flavoured worked example\n\n> *Doctor (US male):* \"The patient is on **acetaminophen** 500mg, four times daily, and we're starting **azithromycin** for the suspected pneumonia.\"\n\nA learner from the UK who hears *acetaminophen* may write *paracetamol* (correct meaning, wrong word). A learner who recognises *azithromycin* may still write *azythromycin* or *azithromicin*. Both errors cost a mark each.\n\n## How the three drills build the skill\n\n- **Drill 1 — Healthcare vocabulary spell-check:** 20 high-yield clinical terms (medications, conditions, procedures) typed from audio.\n- **Drill 2 — Look-alike sound-alike pairs:** *amlodipine vs. amitriptyline*, *cefuroxime vs. cefalexin*. You learn to distinguish acoustically similar drug names.\n- **Drill 3 — Real Part A extracts:** four-question gap-fills where spelling is scored exactly as OET does.\n\n## Common spelling mistakes\n\n1. **Doubling consonants from instinct.** *Withhold* not *withold*; *paracetamol* not *paracettemol*.\n2. **Latin / Greek roots.** *Haemoglobin* (UK) vs *hemoglobin* (US) — both accepted, but pick a register and stay there.\n3. **Number-letter abbreviations.** *Vit. B12*, not *vitb12*.\n\n## The mini-quiz\n\nFive transcribe-the-word items. Pass = 4/5. The quiz uses the same exact-match marking the OET grader does, so you can trust the result.",
                drillIds: new[] { "drill-l3-vocab", "drill-l3-pairs", "drill-l3-partA" },
                quizIds: new[] { "quiz-l3-q1", "quiz-l3-q2", "quiz-l3-q3", "quiz-l3-q4", "quiz-l3-q5" }),

            BuildLesson(
                "L4",
                4,
                "Gist comprehension — hearing the speaker's main point",
                "L4 is the L1 of Part B. Where Part A asks for the literal words, Part B and the gist questions in Part C ask *what is the speaker really getting at?*. Workplace MCQs in Part B always test gist: *what is the purpose of the meeting?*, *what concern does the speaker raise?*, *what action will the listener take next?*.\n\n## Where it shows up in OET\n\n- **Part B** — every question is a gist or main-purpose item.\n- **Part C** — the first question on each extract is usually a gist anchor.\n- **Real clinical life** — handover meetings, multi-disciplinary team huddles.\n\n## Healthcare-flavoured worked example\n\n> *Nurse (Australian female):* \"Look, I know the new pressure-injury protocol means more documentation, but honestly the bigger problem is the patients we're not turning often enough — that's where the breakdowns are starting. The audit forms are fine; we need more pairs of hands on the ward.\"\n\nA Part B stem might read: *The nurse mainly suggests that:* (a) the protocol forms are too complex, (b) staffing levels are causing the harm, (c) audits should be discontinued. A learner who fixates on the literal mention of \"audit forms are fine\" picks (a) or (c); the gist learner picks (b).\n\n## How the three drills build the skill\n\n- **Drill 1 — Single-purpose extracts:** 30-second clips with one clear main point.\n- **Drill 2 — Hedge-heavy speakers:** real OET style — speakers qualify and contrast before they land on their actual view. You learn to wait for the *but*.\n- **Drill 3 — Workplace MCQ practice:** four full Part B items at exam pace.\n\n## Common gist mistakes\n\n1. **Picking the option that matches a phrase you heard.** OET designs distractors from literal lifts.\n2. **Trusting your first impression.** Speakers often state the counter-view first.\n3. **Missing the *however / but / actually*.** Hedge-words flip the gist.\n\n## The mini-quiz\n\nFive Part B MCQ items. Pass = 4/5.",
                drillIds: new[] { "drill-l4-purpose", "drill-l4-hedge", "drill-l4-partB" },
                quizIds: new[] { "quiz-l4-q1", "quiz-l4-q2", "quiz-l4-q3", "quiz-l4-q4", "quiz-l4-q5" }),

            BuildLesson(
                "L5",
                5,
                "Distractor recognition — spotting the trap answers",
                "L5 is the OET-specific skill that separates 350+ from 400+ candidates. Every MCQ in Parts B and C ships with two wrong options engineered to *sound* right — they lift a phrase from the extract, or describe a plausible inference the speaker didn't actually make. L5 trains your ear and eye to recognise the four trap patterns OET uses.\n\n## Where it shows up in OET\n\n- **Part B** — 6 MCQs.\n- **Part C** — 12 MCQs.\n\n## The four trap patterns\n\n1. **Word lift.** The wrong option re-uses a memorable phrase from the audio in a different context.\n2. **Partial truth.** Half the option matches; the other half contradicts.\n3. **Wrong speaker.** The option states something a *different* person in the extract said.\n4. **Plausible inference.** The option states something a clinician *might* say, but didn't in this extract.\n\n## Healthcare-flavoured worked example\n\n> *Doctor (British male):* \"I'd normally recommend a short course of antibiotics here, but Mrs Patel's penicillin allergy makes that complicated, so I'll go with a macrolide instead.\"\n\nMCQ stem: *What treatment will the doctor prescribe?* (a) A penicillin antibiotic *— word lift*. (b) A macrolide antibiotic *— correct*. (c) No antibiotics because of the allergy *— plausible inference*. (d) The shortest available antibiotic course *— partial truth*. Each wrong option exploits a specific trap pattern.\n\n## How the three drills build the skill\n\n- **Drill 1 — Word-lift drills:** identify which option re-uses an audio phrase.\n- **Drill 2 — Speaker-tracking drills:** match each opinion to the right speaker.\n- **Drill 3 — Inference vs. evidence:** the hardest pattern — distinguishing what was said from what the speaker probably believes.\n\n## Common L5 mistakes\n\n1. **Picking the option containing the loudest / most-repeated word.**\n2. **Choosing the option that sounds most clinically sensible.**\n3. **Not eliminating wrong options before evaluating right ones.**\n\n## The mini-quiz\n\nFive MCQs, each engineered with one of the four trap patterns. Pass = 4/5.",
                drillIds: new[] { "drill-l5-word-lift", "drill-l5-speaker", "drill-l5-inference" },
                quizIds: new[] { "quiz-l5-q1", "quiz-l5-q2", "quiz-l5-q3", "quiz-l5-q4", "quiz-l5-q5" }),

            BuildLesson(
                "L6",
                6,
                "Inference — what the speaker means but doesn't say",
                "L6 is the *between-the-lines* skill. A consultant who says *\"I see... and how is the family coping?\"* is signalling concern about safeguarding without using the word. Part C inference questions test exactly this: detecting attitude, implication, and unstated conclusion.\n\n## Where it shows up in OET\n\n- **Part C — inference items** make up 4–6 of the 18 MCQs.\n- **Speaking sub-tests** — picking up the patient's implied concern.\n\n## Healthcare-flavoured worked example\n\n> *Physiotherapist (Australian male):* \"Look, the rehab protocol is structured around three sessions per week. We *can* be flexible, but missing more than one session per fortnight does tend to undo the progress.\"\n\nMCQ stem: *The speaker's attitude towards missed sessions is best described as:* (a) strict prohibition, (b) qualified discouragement, (c) neutral observation, (d) outright endorsement of flexibility. The correct answer is (b) — the *can be flexible / but* construction signals discouragement without forbidding.\n\n## How the three drills build the skill\n\n- **Drill 1 — Hedge-word spotting:** *however*, *I'd say*, *tends to*, *I suppose*. You learn which English phrases signal stance.\n- **Drill 2 — Tone matching:** match a 20-second clip to an attitude label (concerned / sceptical / reassuring / etc.).\n- **Drill 3 — Inference MCQs:** OET-style items where the right answer is never literally stated.\n\n## Common L6 mistakes\n\n1. **Picking the most literal option** — inference items always have a literal trap.\n2. **Ignoring intonation.** OET speakers genuinely act; rising pitch + *I suppose* signals doubt.\n3. **Over-inferring** — choosing a strong claim when a hedged one fits.\n\n## The mini-quiz\n\nFive Part C inference items. Pass = 4/5.",
                drillIds: new[] { "drill-l6-hedge", "drill-l6-tone", "drill-l6-inference" },
                quizIds: new[] { "quiz-l6-q1", "quiz-l6-q2", "quiz-l6-q3", "quiz-l6-q4", "quiz-l6-q5" }),

            BuildLesson(
                "L7",
                7,
                "Speaker stance — recognising opinion vs. fact",
                "L7 sits next to L6. Inference is *what the speaker means*; stance is *what the speaker thinks of what they're saying*. Part C presenters often shift between reporting evidence, evaluating it, and recommending action — and the MCQs probe whether you can tell which mode they're in.\n\n## Where it shows up in OET\n\n- **Part C** — multiple items test \"the speaker's view\" or \"what the speaker emphasises\".\n- **All-skills crossover** — Speaking sub-test, Writing letters where you summarise a clinician's recommendation.\n\n## Healthcare-flavoured worked example\n\n> *Clinical researcher (US female):* \"The trial showed a 12 percent reduction in re-admission rates — that's statistically significant, but I'd be cautious about translating it directly into practice without replication.\"\n\nMCQ stem: *The speaker's stance on the trial findings is best described as:* (a) enthusiastic endorsement, (b) cautious optimism with reservations, (c) outright scepticism, (d) neutral reporting only. The correct answer is (b) — *statistically significant* (positive) + *cautious about translating* (reservation).\n\n## How the three drills build the skill\n\n- **Drill 1 — Fact vs. opinion sorting:** 20 short clips labelled as report / evaluation / recommendation.\n- **Drill 2 — Stance phrases:** *I'd argue*, *the evidence suggests*, *we now know*, *I'm not convinced*. You build a phrase bank.\n- **Drill 3 — Part C stance MCQs:** OET-style.\n\n## Common L7 mistakes\n\n1. **Conflating *stated fact* with *speaker's stance*.** A speaker can quote a fact while disagreeing with it.\n2. **Missing the *but* / *however* pivot.** Stance often flips mid-sentence.\n3. **Over-weighting recent words.** OET speakers sometimes close with the counter-view as a rhetorical flourish.\n\n## The mini-quiz\n\nFive stance items. Pass = 4/5.",
                drillIds: new[] { "drill-l7-fact-opinion", "drill-l7-phrases", "drill-l7-mcq" },
                quizIds: new[] { "quiz-l7-q1", "quiz-l7-q2", "quiz-l7-q3", "quiz-l7-q4", "quiz-l7-q5" }),

            BuildLesson(
                "L8",
                8,
                "Accent adaptation — British, Australian, North American, non-native",
                "L8 is the meta-skill. OET deliberately mixes accents: a Part A consultation with a British doctor and an Australian patient, a Part B clinic-meeting in a North American voice, a Part C presentation by a Filipino-accented presenter. L8 trains your ear to stay accurate across all four registers.\n\n## Where it shows up in OET\n\n- **Every part.** OET will not warn you which accent is coming.\n- **Real clinical life.** UK NHS wards, Australian hospitals, US clinics, multilingual teams.\n\n## Healthcare-flavoured worked example\n\nThe phrase *appointment* is pronounced four very different ways depending on the speaker. A British RP doctor says *ə-POYNT-mənt*, an Australian says *uh-POYNT-mun*, a Californian says *uh-POYNT-mənt* with a stronger second-t, a Filipino-accented English speaker may flatten the second vowel. All four are correct OET English. L8 trains you to map all four to the same lexical item without hesitation.\n\n## How the three drills build the skill\n\n- **Drill 1 — Accent ID:** label each clip with one of British / Australian / North American / non-native. Trains your high-level perceptual map.\n- **Drill 2 — Mixed-accent gap-fills:** Part A style detail capture across all four accents.\n- **Drill 3 — Confidence stretch:** the accent the diagnostic flagged as your weakest, drilled five times.\n\n## Common L8 mistakes\n\n1. **Tuning out the speaker for half a second when an unfamiliar accent starts.** That half second is a lost gap-fill.\n2. **Mis-mapping vowels.** *Eight / ate*, *can / Cain*, *port / pot* depending on accent.\n3. **Assuming the speaker will repeat or slow down.** They won't.\n\n## The mini-quiz\n\nFive mixed-accent items. Pass = 4/5.",
                drillIds: new[] { "drill-l8-accent-id", "drill-l8-gap-fill", "drill-l8-stretch" },
                quizIds: new[] { "quiz-l8-q1", "quiz-l8-q2", "quiz-l8-q3", "quiz-l8-q4", "quiz-l8-q5" }),
        };
    }

    private static ListeningLesson BuildLesson(
        string skillCode,
        int orderIndex,
        string title,
        string bodyMarkdownEn,
        IReadOnlyList<string> drillIds,
        IReadOnlyList<string> quizIds)
    {
        var slug = $"listening-lesson-{skillCode.ToLowerInvariant()}-{SkillCodeSlug(skillCode)}";
        return new ListeningLesson
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            Title = title,
            TitleAr = string.Empty,
            SkillCode = skillCode,
            OrderIndex = orderIndex,
            EstimatedMinutes = 30,
            VideoUrl = null,
            BodyMarkdownEn = bodyMarkdownEn,
            BodyMarkdownAr = string.Empty,
            DrillQuestionIdsJson = JsonSerializer.Serialize(drillIds, JsonOpts),
            QuizQuestionIdsJson = JsonSerializer.Serialize(quizIds, JsonOpts),
            PrerequisiteLessonId = null,
            IsPublished = true,
        };
    }

    /// <summary>Map L1..L8 to the hyphenated slug suffix used in the canonical
    /// lesson URL (e.g. L1 → "detail-capture").</summary>
    private static string SkillCodeSlug(string skillCode) => skillCode.ToUpperInvariant() switch
    {
        "L1" => "detail-capture",
        "L2" => "note-taking-speed",
        "L3" => "spelling-accuracy",
        "L4" => "gist-comprehension",
        "L5" => "distractor-recognition",
        "L6" => "inference",
        "L7" => "speaker-stance",
        "L8" => "accent-adaptation",
        _ => skillCode.ToLowerInvariant(),
    };

    // ── Strategies (12 — 2 per category × 6) ────────────────────────────────

    private async Task<int> SeedStrategiesAsync(CancellationToken ct)
    {
        var strategies = BuildStrategies();

        var existingSlugs = await db.ListeningStrategies
            .AsNoTracking()
            .Where(s => strategies.Select(x => x.Slug).Contains(s.Slug))
            .Select(s => s.Slug)
            .ToListAsync(ct);
        var existingSet = new HashSet<string>(existingSlugs, StringComparer.Ordinal);

        var added = 0;
        foreach (var strategy in strategies)
        {
            if (existingSet.Contains(strategy.Slug))
            {
                continue;
            }
            db.ListeningStrategies.Add(strategy);
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        return added;
    }

    private static List<ListeningStrategy> BuildStrategies()
    {
        // Categories chosen to mirror the pathway design doc §16:
        //   note-taking, gist, inference, time-management, accent, exam-day.
        // Two strategies per category gives a launch-day library of 12 that
        // covers every learner stage.
        return new List<ListeningStrategy>
        {
            // ── note_taking ──────────────────────────────────────────────
            BuildStrategy(
                slug: "the-skeleton-method",
                title: "The Skeleton Method — pre-print the structure",
                category: "note_taking",
                applicableParts: new[] { "A" },
                difficulty: 1,
                body: "## What it is\n\nThe Skeleton Method is a Part-A specific habit: before the audio starts, you scan the gap-fills and **pre-write** the labels you will need to fill in. Names, dates, medication slots, dose units, addresses — most appear in a recognisable order. You arrive at the audio with half your sentences already scaffolded; the consultation just fills the blanks.\n\n## When to use it\n\n- **Part A** every time. The pre-read window is 8 seconds.\n- **Practice Part A drills** to lock in the muscle memory.\n\n## How to do it\n\n1. Read the first 5 gap-fills as soon as the screen loads.\n2. On your scratch pad, write a vertical column: *Name | DOB | Address | Med | Dose | Allergy*.\n3. Cross each label out as the patient and clinician land on it.\n4. Triage: if a gap is missed, draw a horizontal line and keep listening — never stop to recover a lost item.\n\n## Healthcare-themed worked example\n\nIn a typical paediatric consultation, the doctor will introduce the patient (name, DOB), elicit the presenting complaint, ask about meds and allergies, then move to plan. Your skeleton — *Name / DOB / Complaint / Meds / Allergy / Plan* — mirrors that exact flow. When the consultant says *\"and is Aiden allergic to anything we should know about?\"* you simply tick *Allergy* and type the answer.\n\n## Common mistakes\n\n1. **Pre-writing in full sentences.** Defeats the speed gain. Use single words.\n2. **Skipping the scan.** A scrambled-order Part A still uses the same vocabulary; familiarity is half the win.\n3. **Sticking to a skeleton that doesn't match the audio.** If the consultation opens with allergies (rare), drop the skeleton — don't fight it.\n\n## Linked drill\n\nThe L2 Telegraphic Drill teaches the abbreviation style you'll use on the skeleton. Pair the two for compounding gains."),
            BuildStrategy(
                slug: "the-cornell-shorthand",
                title: "The Cornell Shorthand — a 30-symbol bank for OET",
                category: "note_taking",
                applicableParts: new[] { "A", "B", "C" },
                difficulty: 2,
                body: "## What it is\n\nThe Cornell Shorthand is an adapted version of the classic Cornell note system, tuned for clinical language. You build a fixed bank of ~30 symbols — *pt* (patient), *Δ* (change), *↑/↓* (increase/decrease), *N+* (nausea), *SOB* (shortness of breath) — and use them as your default writing unit. The benefit compounds: a typical OET Part A extract has 25–30 high-frequency clinical terms, and shaving 4 keystrokes off each saves you over a minute of writing time.\n\n## When to use it\n\n- **Part A** — strongly.\n- **Part C** — when triaging a long monologue.\n- **Real ward handover** — bonus, not OET-required.\n\n## How to do it\n\n1. Print the Cornell Bank (download from the Resources tab) and tape it to your study monitor for 7 days.\n2. Practice 10 minutes per day with the Telegraphic Drill, forcing every clinical concept into a symbol.\n3. By day 7 the bank is automatic; you no longer reach for the printout.\n\n## Healthcare-themed worked example\n\nFull-text note: *Patient reports nausea since yesterday morning and shortness of breath on exertion.* Cornell version: *pt N+ since yest am, SOB on exert.* Same meaning, 31 keystrokes vs. 67.\n\n## Common mistakes\n\n1. **Inventing symbols on the fly.** You won't remember them under exam pressure. Stick to the bank.\n2. **Mixing Cornell with English shorthand systems** (e.g. Pitman). They conflict.\n3. **Using symbols you can't read back.** If a symbol takes longer to *decipher* than to *write*, drop it.\n\n## Linked drill\n\nThe L2 Telegraphic Drill is the canonical training surface for Cornell. Three runs per day for a week unlocks measurable Part A gains."),

            // ── gist ────────────────────────────────────────────────────
            BuildStrategy(
                slug: "wait-for-the-but",
                title: "Wait for the But — find the speaker's pivot",
                category: "gist",
                applicableParts: new[] { "B", "C" },
                difficulty: 1,
                body: "## What it is\n\nOET speakers rarely lead with their main point. They open with a concession — *\"I know the new protocol has been welcomed by the team…\"* — then pivot with a single hinge word: *but*, *however*, *that said*, *the bigger issue is*. The gist of a Part B extract usually sits **after** the pivot, not before.\n\n## When to use it\n\n- **Part B** — almost every extract.\n- **Part C** — the first 30 seconds of a monologue.\n\n## How to do it\n\n1. As the speaker begins, listen for the opening *concession marker* (*I know*, *of course*, *granted*).\n2. Mark a tiny dash on your scratch pad — this is the *pre-pivot* zone, low-yield.\n3. When you hear the pivot word, switch to active note-taking. Everything after the pivot is the answer.\n\n## Healthcare-themed worked example\n\n> *Pharmacist:* \"The new dispensing software has cut script processing time, which everyone appreciates. **However**, the real saving has come from the consolidation of stock at the central depot — that's where we found the budget headroom for the locum hires.\"\n\nA Part B stem asking *what does the speaker identify as the main saving?* expects *consolidation of stock*, not *script processing*. Learners who skipped the pivot answer the pre-pivot phrase.\n\n## Common mistakes\n\n1. **Latching onto the loudest word.** *Software* is salient but pre-pivot.\n2. **Picking the literal lift.** The wrong option will re-quote *script processing time*.\n3. **Stopping at the first pivot.** Some speakers double-pivot (*however… but actually*). Wait for the last one.\n\n## Linked drill\n\nThe L4 Hedge-Heavy Drill is built around exactly this skill."),
            BuildStrategy(
                slug: "one-sentence-summary",
                title: "One-Sentence Summary — force a gist commitment",
                category: "gist",
                applicableParts: new[] { "B" },
                difficulty: 1,
                body: "## What it is\n\nAfter every Part B extract you have ~5 seconds before the next one starts. The One-Sentence Summary drill forces you to use that window: *in one sentence, what was the speaker's main point?*. The act of compressing the extract into a single clause crystallises your understanding and gives you a anchor to compare the MCQ options against.\n\n## When to use it\n\n- **Part B** — between every extract.\n- **Part C** — between sub-topics within a monologue.\n\n## How to do it\n\n1. As the extract ends, mentally complete: *\"The speaker is saying that ___\"*.\n2. Constrain the summary to one clause — no *and*, no *because*.\n3. Compare each MCQ option to that clause; the closest match is the answer.\n\n## Healthcare-themed worked example\n\nAfter a 40-second extract about staff rota changes: *\"The speaker is saying that the rota change has reduced overtime but increased weekend stress.\"* The MCQ option that says *\"the rota change has had mixed effects on staff workload\"* is the clean match.\n\n## Common mistakes\n\n1. **Skipping the summary** because you're too busy. Two seconds buys two marks.\n2. **Padding the summary** with extracted details. The point is *gist*, not transcript.\n3. **Defaulting to the first option that matches a phrase.** Always run the summary first.\n\n## Linked drill\n\nThe L4 Workplace MCQ Drill practises this exact rhythm."),

            // ── inference ──────────────────────────────────────────────
            BuildStrategy(
                slug: "the-stance-bank",
                title: "The Stance Bank — 12 phrases that signal opinion",
                category: "inference",
                applicableParts: new[] { "C" },
                difficulty: 2,
                body: "## What it is\n\nThe Stance Bank is a memorised list of 12 high-yield phrases OET presenters use to signal opinion: *I'd argue*, *the evidence suggests*, *in my experience*, *we now know that*, *I'm not convinced that*, *what's been overlooked is*, *it's perhaps worth noting that*, *on balance*, *the data, however, tells a different story*, *I'd be cautious about*, *I see this differently*, *let me put it this way*. Recognising these phrases live tells you whether the next sentence is fact, opinion, or recommendation.\n\n## When to use it\n\n- **Part C** stance questions.\n- **L7 Speaker Stance** drills.\n\n## How to do it\n\n1. Memorise the 12-phrase list in two sittings (5 minutes each).\n2. While practising, annotate every phrase you hear in the audio with *F* (fact), *O* (opinion), or *R* (recommendation).\n3. In the exam, your ear pre-filters speaker turns into these three categories.\n\n## Healthcare-themed worked example\n\n> *Researcher:* \"The trial demonstrated a 12% reduction in readmissions. **I'd be cautious about** generalising the finding without replication.\"\n\nThe first clause is *F*; the second, signalled by the stance phrase, is *O*. The MCQ asking *what is the speaker's view of the trial?* is asking about the *O* clause, not the *F* clause.\n\n## Common mistakes\n\n1. **Treating *the evidence shows* as a fact phrase.** It signals opinion-from-evidence, not pure fact.\n2. **Ignoring the *however*.** It's the most common stance pivot in OET.\n3. **Missing recommendations.** *Let me put it this way* often introduces a recommendation OET will quiz."),
            BuildStrategy(
                slug: "fact-vs-opinion-sort",
                title: "Fact vs. Opinion — the live-sort habit",
                category: "inference",
                applicableParts: new[] { "B", "C" },
                difficulty: 1,
                body: "## What it is\n\nFact-vs-Opinion is a simple, continuous classification: as the speaker talks, you silently label each clause F or O. Over a 90-second Part C monologue, this habit produces a mental map (e.g. *F F F O F O R*) that you can scan when the MCQ asks *what does the speaker emphasise?* — the answer is almost always one of the O or R clauses, never the Fs.\n\n## When to use it\n\n- **Part C** — every monologue.\n- **Part B** — quicker, two or three labels.\n\n## How to do it\n\n1. Pre-print *F* and *O* on your scratch pad.\n2. As each clause finishes, tap an imaginary key — F or O.\n3. When an MCQ asks for the speaker's *view*, *belief*, or *concern*, you scan for the O clauses.\n\n## Healthcare-themed worked example\n\nA presenter says: *\"Rehabilitation outcomes improved 8% last year [F]. That's still well below the regional benchmark of 15% [F]. We need to invest in earlier intervention [O / R].\"* The MCQ stem *what does the speaker advocate?* maps to the O / R clause.\n\n## Common mistakes\n\n1. **Trying to write the labels** — too slow. The labels live in your head.\n2. **Mis-classifying *statistically significant* findings as opinion.** They're facts the speaker is *reporting*; the speaker's stance on them is a separate clause.\n3. **Skipping the recommendation tag.** OET sometimes asks specifically about the action the speaker wants taken."),

            // ── time_mgt ────────────────────────────────────────────────
            BuildStrategy(
                slug: "the-eight-minute-rule",
                title: "The Eight-Minute Rule — pacing across Parts",
                category: "time_mgt",
                applicableParts: new[] { "A", "B", "C" },
                difficulty: 1,
                body: "## What it is\n\nOET Listening runs for ~45 minutes. The Eight-Minute Rule is a simple budget: Part A consumes one 8-minute extract; each Part B extract is 90 seconds; each Part C extract is around 6 minutes. If you're spending more than 8 seconds on any single MCQ in Parts B or C, you are out of budget and need to commit and move on.\n\n## When to use it\n\n- **Always.** This is the meta-strategy.\n\n## How to do it\n\n1. Pre-mark your scratch pad with three timing checkpoints: *A done by 9 min, B done by 22 min, C done by 45 min*.\n2. Wear (or imagine) a stopwatch you can glance at between extracts.\n3. If you're behind at any checkpoint, sacrifice the slowest item — never the next one.\n\n## Healthcare-themed worked example\n\nYou're in Part B, second extract. You catch the gist but the MCQ is a 3-way tie. The Eight-Minute Rule says: **commit your best guess in 8 seconds**, mark it for review, and free your ear for extract 3. Coming back later costs you nothing; failing to hear extract 3 costs you a whole item.\n\n## Common mistakes\n\n1. **Replaying the audio in your head while extract 3 is playing.** Catastrophic.\n2. **Going back to fix an item before the test ends.** OET doesn't allow it.\n3. **Skipping checkpoints in practice.** Pacing is muscle memory; practise it from day 1."),
            BuildStrategy(
                slug: "the-commit-and-mark-pattern",
                title: "Commit-and-Mark — never leave a question blank",
                category: "time_mgt",
                applicableParts: new[] { "A", "B", "C" },
                difficulty: 1,
                body: "## What it is\n\nOET Listening doesn't penalise wrong answers, but a blank is always wrong. The Commit-and-Mark pattern says: *every question gets a guess, even if you didn't hear the answer*. A 25% guess on a 4-option MCQ is worth +0.25 marks on average; over the 42 items in a full mock, the discipline is worth 3+ marks.\n\n## When to use it\n\n- **Every time** the audio ends and an item is incomplete.\n\n## How to do it\n\n1. When the audio ends, scan unanswered items.\n2. For each blank, pick the longest option (statistically slightly more likely to be correct in OET style).\n3. Move on. Do not return.\n\n## Healthcare-themed worked example\n\nYou missed a date in Part A — *15 March*. Commit-and-Mark says: write *15 March* if you heard *March*, or *15 / unknown* if you heard the day but not the month. Partial credit on Part A is rare but possible.\n\n## Common mistakes\n\n1. **Leaving a blank thinking you'll come back.** You won't.\n2. **Erasing a partial answer.** Partial is better than blank.\n3. **Spending 30 seconds choosing between two guesses.** Pick the longer option and move on."),

            // ── accent ──────────────────────────────────────────────────
            BuildStrategy(
                slug: "fifteen-minute-accent-warm-up",
                title: "The 15-Minute Accent Warm-Up — exam-day prep",
                category: "accent",
                applicableParts: new[] { "A", "B", "C" },
                difficulty: 1,
                body: "## What it is\n\nA 15-minute pre-exam warm-up routine: 4 minutes of British audio, 4 minutes of Australian, 4 minutes of North American, 3 minutes of non-native — at full OET pace. The point is not to learn anything new; it's to *prime* your perceptual system so the first 30 seconds of Part A doesn't feel cold.\n\n## When to use it\n\n- **The morning of the exam.**\n- **Before every full mock.**\n\n## How to do it\n\n1. Save four 4-minute extracts (one per accent) from the practice library.\n2. Play them at 1.0x speed in the order above, with 30 seconds between each.\n3. Don't take notes; just listen.\n\n## Healthcare-themed worked example\n\nA Filipino nurse handover, a Glaswegian consultant, an Australian physio, a Californian researcher — your 16 minutes covers the four canonical OET registers. By the time the real Part A starts, none of the accents are novel.\n\n## Common mistakes\n\n1. **Skipping the warm-up because the exam feels stressful.** It's the highest-ROI 15 minutes you'll spend.\n2. **Mixing in study material.** Warm-up is *priming*, not *learning*.\n3. **Playing at 0.75x.** Defeats the purpose."),
            BuildStrategy(
                slug: "accent-vowel-shifts",
                title: "Accent Vowel Shifts — the 6 trap pairs",
                category: "accent",
                applicableParts: new[] { "A" },
                difficulty: 2,
                body: "## What it is\n\nSix vowel pairs that flip between OET accents and cost learners the most Part A marks:\n\n1. *Eight* (UK long *ay*) vs *eight* (US/AU near-monophthong).\n2. *Can* (UK long *a*) vs *can* (US short *a*) — sounds like *Cain* to non-native ears.\n3. *Pot* (UK rounded *o*) vs *pot* (US unrounded *a*) — sounds like *pat*.\n4. *Hospital* — UK *hoss-PI-t'l*, US *HOSS-pi-tal*, AU drops the *t*.\n5. *Schedule* — UK *SHED-yool*, US *SKED-yool*.\n6. *Privacy* — UK *PRIV-a-see*, US *PRY-va-see*.\n\n## When to use it\n\n- **Always**, but especially in Part A where these are the gap-fills most likely to fool you.\n\n## How to do it\n\n1. Drill each pair using the L8 Stretch Drill until you can identify which accent at first vowel.\n2. Annotate your Cornell Bank with the alternative pronunciations beside high-frequency words.\n3. When in doubt during a real Part A, *write what you heard literally* and trust the spelling tolerance to forgive minor mismatches.\n\n## Healthcare-themed worked example\n\nA US doctor saying *\"the patient was seen by the schedule team\"* (US: *SKED-yool*) trips UK-trained learners who wrote *shed*. The audio said *scheduling team*; the gap-fill takes *scheduling*."),

            // ── exam_day ────────────────────────────────────────────────
            BuildStrategy(
                slug: "the-30-second-pre-flight-check",
                title: "The 30-Second Pre-Flight Check",
                category: "exam_day",
                applicableParts: new[] { "A", "B", "C" },
                difficulty: 1,
                body: "## What it is\n\nThirty seconds before each part starts, you run a four-item pre-flight check: *(1) headphones seated, (2) volume audible, (3) pen + scratch pad ready, (4) skeleton drawn (Part A) or stance bank visible (Part C)*. The point is to commit no avoidable equipment / process errors during the first 30 seconds of the audio.\n\n## When to use it\n\n- **Before each of the three parts.**\n\n## How to do it\n\n1. As the part transition screen loads, do the check in this exact order.\n2. If anything fails (headphones not seated, mic ID popup blocking the screen), raise your hand immediately.\n3. Use the remaining seconds to look at the first gap-fill / first MCQ.\n\n## Healthcare-themed worked example\n\nThe NHS has built a pre-flight checklist for theatre teams to use before incision. Same principle: cheap, fast, prevents the disaster.\n\n## Common mistakes\n\n1. **Adjusting volume mid-audio.** The first 30 seconds are gone.\n2. **Skipping the check because the part is short.** Part B's first extract starts in 5 seconds — that's exactly when you most need the check."),
            BuildStrategy(
                slug: "post-test-recovery-routine",
                title: "Post-Test Recovery — preserving the score",
                category: "exam_day",
                applicableParts: new[] { "A", "B", "C" },
                difficulty: 1,
                body: "## What it is\n\nOET Listening is followed immediately by Reading. A 5-minute recovery routine — water, a single stretch, one slow breath cycle, a 30-second mental reset — preserves your alertness for Reading without erasing your Listening flow.\n\n## When to use it\n\n- **Between Listening and Reading.**\n- **Between each part of the mock when practising at home.**\n\n## How to do it\n\n1. Don't review what you just did. The score is now fixed.\n2. Drink ~150ml water.\n3. Roll your shoulders back, breathe out for 8 seconds.\n4. Re-read the Reading instructions on the candidate page.\n\n## Healthcare-themed worked example\n\nThis is the *clinical reset* surgeons use between cases. Same biology, same payoff.\n\n## Common mistakes\n\n1. **Replaying answers in your head.** Active interference with the next test.\n2. **Asking neighbouring candidates about answers.** Disqualification risk.\n3. **Skipping water.** Cognitive performance drops measurably after 90 minutes of dehydrated focus."),
        };
    }

    private static ListeningStrategy BuildStrategy(
        string slug,
        string title,
        string category,
        IReadOnlyList<string> applicableParts,
        int difficulty,
        string body)
    {
        return new ListeningStrategy
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            Title = title,
            TitleAr = string.Empty,
            Category = category,
            ApplicablePartsJson = JsonSerializer.Serialize(applicableParts, JsonOpts),
            EstimatedReadMinutes = 5,
            BodyMarkdownEn = body,
            BodyMarkdownAr = string.Empty,
            VideoUrl = null,
            AudioUrl = null,
            LinkedDrillId = null,
            UnlockStage = "foundation",
            Difficulty = difficulty,
            IsPublished = true,
        };
    }
}
