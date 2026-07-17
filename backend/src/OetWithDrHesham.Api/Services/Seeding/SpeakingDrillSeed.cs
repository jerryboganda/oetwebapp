using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Seeding;

// Phase 5 (G.5) of the OET Speaking module roadmap.
//
// Seeds 24 speaking drills — 2 per drill kind across the 12 kinds
// defined in `SpeakingDrillKind`. Each drill is wired to its own
// `ContentItem` (ContentType="speaking_drill", SubtestCode="speaking",
// Status=Published) with the prompt living in `DetailJson.instructionText`
// and the target criteria mirrored on both rows.
//
// Idempotent on Id prefix `sdi-seed-`: the seeder probes for any
// existing seeded drill and bails out cleanly. This lets `Program.cs`
// call the seeder on every startup without risk of duplicate inserts.
public static class SpeakingDrillSeed
{
    public const string SeedIdPrefix = "sdi-seed-";
    private const string SeederUserId = "system-speaking-drill-seed";

    public static async Task SeedAsync(LearnerDbContext db, CancellationToken ct = default)
    {
        var alreadySeeded = await db.SpeakingDrillItems
            .AsNoTracking()
            .AnyAsync(d => d.Id.StartsWith(SeedIdPrefix), ct);
        if (alreadySeeded)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var seed in DrillSeeds())
        {
            var contentItemId = $"ci-seed-drill-{seed.Slug}";
            var drillId = $"sdi-seed-{seed.Slug}";

            db.ContentItems.Add(new ContentItem
            {
                Id = contentItemId,
                ContentType = "speaking_drill",
                SubtestCode = "speaking",
                ProfessionId = null,
                Title = seed.Title,
                Difficulty = "core",
                EstimatedDurationMinutes = 1,
                CriteriaFocusJson = JsonSupport.Serialize(seed.TargetCriteria),
                ScenarioType = seed.Kind.ToString().ToLowerInvariant(),
                ModeSupportJson = JsonSupport.Serialize(new[] { "learning" }),
                PublishedRevisionId = $"rev-{drillId}",
                Status = ContentStatus.Published,
                DetailJson = JsonSupport.Serialize(new
                {
                    instructionText = seed.InstructionText,
                    drillKind = seed.Kind.ToString(),
                    targetCriteria = seed.TargetCriteria,
                }),
                ModelAnswerJson = "{}",
                ExamFamilyCode = "oet",
                ExamTypeCode = "oet",
                DifficultyRating = 1500,
                SourceType = "manual",
                SourceProvenance = "original",
                RightsStatus = "owned",
                QaStatus = "approved",
                FreshnessConfidence = "current",
                InstructionLanguage = "en",
                ContentLanguage = "en",
                CreatedBy = SeederUserId,
                CreatedAt = now,
                UpdatedAt = now,
                PublishedAt = now,
            });

            db.SpeakingDrillItems.Add(new SpeakingDrillItem
            {
                Id = drillId,
                ContentItemId = contentItemId,
                DrillKind = seed.Kind,
                TargetCriteriaJson = JsonSupport.Serialize(seed.TargetCriteria),
                RecommendedAfterSessionScoreBelow = seed.RecommendedBelow,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    // ─── Drill catalogue — 2 per kind × 12 kinds = 24 drills ─────────────

    private static IEnumerable<DrillSeedData> DrillSeeds() => new[]
    {
        // ── Opening (2) ───────────────────────────────────────────────────
        new DrillSeedData(
            Slug: "opening-01",
            Kind: SpeakingDrillKind.Opening,
            Title: "Open with a wound-check follow-up",
            InstructionText: "You're meeting Mrs Lee for a wound check. Open the conversation: greet her, "
                + "confirm identity, state your purpose, and invite questions — all in ≤30 seconds.",
            TargetCriteria: new[] { "appropriateness", "structure", "relationshipBuilding" },
            RecommendedBelow: 4),
        new DrillSeedData(
            Slug: "opening-02",
            Kind: SpeakingDrillKind.Opening,
            Title: "Open a paediatric clinic appointment",
            InstructionText: "You're seeing a parent and their 4-year-old in clinic for the first time. "
                + "Open warmly: greet both, confirm names, explain who you are, and set the agenda for the visit.",
            TargetCriteria: new[] { "appropriateness", "structure", "patientPerspective" },
            RecommendedBelow: 4),

        // ── Empathy (2) ───────────────────────────────────────────────────
        new DrillSeedData(
            Slug: "empathy-01",
            Kind: SpeakingDrillKind.Empathy,
            Title: "Respond to fear of surgery",
            InstructionText: "The patient says: 'I'm scared this operation might go wrong.' Respond in "
                + "≤30 seconds showing empathy, gentle normalisation, and a concrete next step.",
            TargetCriteria: new[] { "patientPerspective", "relationshipBuilding" },
            RecommendedBelow: 3),
        new DrillSeedData(
            Slug: "empathy-02",
            Kind: SpeakingDrillKind.Empathy,
            Title: "Acknowledge a recent bereavement",
            InstructionText: "Your patient mentions her husband died last month while reporting low "
                + "mood. Acknowledge the loss, pause, then gently move into a relevant clinical question.",
            TargetCriteria: new[] { "patientPerspective", "relationshipBuilding", "appropriateness" },
            RecommendedBelow: 3),

        // ── Ice (Ideas / Concerns / Expectations) (2) ─────────────────────
        new DrillSeedData(
            Slug: "ice-01",
            Kind: SpeakingDrillKind.Ice,
            Title: "Elicit ICE about starting insulin",
            InstructionText: "Find out what the patient thinks insulin is (ideas), what worries them "
                + "about it (concerns), and what they expect from this change in treatment (expectations).",
            TargetCriteria: new[] { "informationGathering", "patientPerspective" },
            RecommendedBelow: 3),
        new DrillSeedData(
            Slug: "ice-02",
            Kind: SpeakingDrillKind.Ice,
            Title: "ICE before a new cancer screening",
            InstructionText: "Before a discussion about bowel-cancer screening, draw out the patient's "
                + "ideas, concerns, and expectations in three open prompts — one for each.",
            TargetCriteria: new[] { "informationGathering", "patientPerspective" },
            RecommendedBelow: 3),

        // ── OpenQuestion (2) ──────────────────────────────────────────────
        new DrillSeedData(
            Slug: "open-question-01",
            Kind: SpeakingDrillKind.OpenQuestion,
            Title: "Open-ended chest pain history",
            InstructionText: "Take the first 30 seconds of a chest-pain history using only open questions. "
                + "Resist the temptation to drop into closed yes/no questions.",
            TargetCriteria: new[] { "informationGathering", "structure" },
            RecommendedBelow: 4),
        new DrillSeedData(
            Slug: "open-question-02",
            Kind: SpeakingDrillKind.OpenQuestion,
            Title: "Open-ended antenatal review",
            InstructionText: "You're starting a routine antenatal review at 28 weeks. Use three open "
                + "questions back-to-back to explore how the pregnancy is going so far.",
            TargetCriteria: new[] { "informationGathering", "patientPerspective" },
            RecommendedBelow: 4),

        // ── LayLanguage (2) ───────────────────────────────────────────────
        new DrillSeedData(
            Slug: "lay-language-01",
            Kind: SpeakingDrillKind.LayLanguage,
            Title: "Explain 'hypertension' to a teenager",
            InstructionText: "Explain what hypertension is and why it matters to a 15-year-old patient "
                + "in ≤30 seconds. No medical jargon — every term must be reformulated.",
            TargetCriteria: new[] { "informationGiving", "appropriateness" },
            RecommendedBelow: 4),
        new DrillSeedData(
            Slug: "lay-language-02",
            Kind: SpeakingDrillKind.LayLanguage,
            Title: "Explain 'MRI scan' to an anxious older adult",
            InstructionText: "Describe what an MRI scan is, what the patient will experience, and how "
                + "long it takes — in plain language a 75-year-old can repeat back.",
            TargetCriteria: new[] { "informationGiving", "patientPerspective" },
            RecommendedBelow: 4),

        // ── Signposting (2) ───────────────────────────────────────────────
        new DrillSeedData(
            Slug: "signposting-01",
            Kind: SpeakingDrillKind.Signposting,
            Title: "Structure a 4-step explanation of post-op care",
            InstructionText: "Walk the patient through post-operative care in four clearly signposted "
                + "steps: 'First, … Next, … Then, … Finally, …'.",
            TargetCriteria: new[] { "structure", "informationGiving" },
            RecommendedBelow: 4),
        new DrillSeedData(
            Slug: "signposting-02",
            Kind: SpeakingDrillKind.Signposting,
            Title: "Signpost a transition from history to examination",
            InstructionText: "You've finished taking the history and need to move on to the physical "
                + "examination. Signpost the transition clearly, set expectations, and check consent.",
            TargetCriteria: new[] { "structure", "appropriateness" },
            RecommendedBelow: 4),

        // ── CheckingUnderstanding (2) ─────────────────────────────────────
        new DrillSeedData(
            Slug: "checking-understanding-01",
            Kind: SpeakingDrillKind.CheckingUnderstanding,
            Title: "Check medication understanding",
            InstructionText: "You've just explained a new asthma inhaler regimen. Use teach-back to "
                + "check the patient's understanding without sounding like a quiz.",
            TargetCriteria: new[] { "informationGiving", "patientPerspective" },
            RecommendedBelow: 4),
        new DrillSeedData(
            Slug: "checking-understanding-02",
            Kind: SpeakingDrillKind.CheckingUnderstanding,
            Title: "Confirm parental understanding of a fever plan",
            InstructionText: "After explaining how to manage a child's fever at home, ask the parent "
                + "to repeat the three red-flag symptoms you described — politely and warmly.",
            TargetCriteria: new[] { "patientPerspective", "informationGiving" },
            RecommendedBelow: 4),

        // ── Reassurance (2) ───────────────────────────────────────────────
        new DrillSeedData(
            Slug: "reassurance-01",
            Kind: SpeakingDrillKind.Reassurance,
            Title: "Reassure parent about toddler fever without false certainty",
            InstructionText: "A worried parent asks: 'Is my child going to be OK?' Reassure without "
                + "overpromising — acknowledge the fear, share what's reassuring, name what to watch for.",
            TargetCriteria: new[] { "relationshipBuilding", "patientPerspective" },
            RecommendedBelow: 3),
        new DrillSeedData(
            Slug: "reassurance-02",
            Kind: SpeakingDrillKind.Reassurance,
            Title: "Reassure a pre-surgery patient about pain control",
            InstructionText: "Your patient is anxious about post-operative pain. Reassure them about "
                + "the multimodal pain plan without dismissing their worry, and invite further questions.",
            TargetCriteria: new[] { "relationshipBuilding", "informationGiving" },
            RecommendedBelow: 3),

        // ── Closing (2) ───────────────────────────────────────────────────
        new DrillSeedData(
            Slug: "closing-01",
            Kind: SpeakingDrillKind.Closing,
            Title: "Close with summary + next visit",
            InstructionText: "Close a 5-minute consultation: summarise the plan in two sentences, "
                + "confirm the next visit, and invite any final questions.",
            TargetCriteria: new[] { "structure", "appropriateness" },
            RecommendedBelow: 4),
        new DrillSeedData(
            Slug: "closing-02",
            Kind: SpeakingDrillKind.Closing,
            Title: "Close after a difficult conversation",
            InstructionText: "You've just delivered difficult news. Close the conversation safely — "
                + "name the next contact, signpost support, and offer a way to reach you.",
            TargetCriteria: new[] { "appropriateness", "relationshipBuilding" },
            RecommendedBelow: 3),

        // ── Pronunciation (2) ─────────────────────────────────────────────
        new DrillSeedData(
            Slug: "pronunciation-01",
            Kind: SpeakingDrillKind.Pronunciation,
            Title: "Medical word stress drill — 5 terms",
            InstructionText: "Say each term twice, placing the primary stress correctly: paracetamol, "
                + "hypertension, ibuprofen, antibiotic, anaesthetic.",
            TargetCriteria: new[] { "intelligibility" },
            RecommendedBelow: 4),
        new DrillSeedData(
            Slug: "pronunciation-02",
            Kind: SpeakingDrillKind.Pronunciation,
            Title: "Vowel contrast in clinical pairs",
            InstructionText: "Read each minimal pair clearly: ship/sheep, pen/pain, hit/heat, "
                + "fit/feet, bed/bad. Slow down on the vowel.",
            TargetCriteria: new[] { "intelligibility" },
            RecommendedBelow: 4),

        // ── Fluency (2) ───────────────────────────────────────────────────
        new DrillSeedData(
            Slug: "fluency-01",
            Kind: SpeakingDrillKind.Fluency,
            Title: "60-second monologue: a typical morning shift",
            InstructionText: "Speak for 60 seconds without long pauses about what you typically do "
                + "on a morning shift. Aim for natural sentences, not perfect grammar.",
            TargetCriteria: new[] { "fluency" },
            RecommendedBelow: 4),
        new DrillSeedData(
            Slug: "fluency-02",
            Kind: SpeakingDrillKind.Fluency,
            Title: "60-second monologue: a memorable patient",
            InstructionText: "Tell a 60-second story about a patient you'll never forget. Use linking "
                + "words ('then', 'after that', 'in the end') to keep the flow going.",
            TargetCriteria: new[] { "fluency", "structure" },
            RecommendedBelow: 4),

        // ── Grammar (2) ───────────────────────────────────────────────────
        new DrillSeedData(
            Slug: "grammar-01",
            Kind: SpeakingDrillKind.Grammar,
            Title: "Conditional advice: rephrase 5 imperatives",
            InstructionText: "Rephrase each imperative as a polite conditional / modal form. "
                + "Example: 'Take this twice a day.' → 'You might want to take this twice a day.'",
            TargetCriteria: new[] { "grammarExpression", "appropriateness" },
            RecommendedBelow: 4),
        new DrillSeedData(
            Slug: "grammar-02",
            Kind: SpeakingDrillKind.Grammar,
            Title: "Reported speech: relaying a colleague's plan",
            InstructionText: "Relay these three direct quotes from your colleague as reported "
                + "speech: 'I'll review the wound on Friday.' / 'She needs the bloods repeated.' / "
                + "'I've asked physiotherapy to see him today.'",
            TargetCriteria: new[] { "grammarExpression" },
            RecommendedBelow: 4),
    };
}

/// <summary>Internal DTO describing one seed drill. Lives in this file
/// because nothing else in the codebase consumes it.</summary>
internal sealed record DrillSeedData(
    string Slug,
    SpeakingDrillKind Kind,
    string Title,
    string InstructionText,
    string[] TargetCriteria,
    int? RecommendedBelow);
