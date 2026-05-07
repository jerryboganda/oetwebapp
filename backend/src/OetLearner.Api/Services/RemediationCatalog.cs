namespace OetLearner.Api.Services;

/// <summary>
/// Mocks V2 Wave 5 — static catalog mapping a learner weakness tag to one or
/// more drill recommendations. Extracted from <see cref="RemediationPlanService"/>
/// so the deterministic plan generator can be unit-tested in isolation and so
/// future call sites (admin overrides, sponsor playbooks, AI personalisation)
/// share one source of truth.
///
/// <para>
/// Tag conventions:
/// </para>
/// <list type="bullet">
///   <item><c>low_{subtest}</c> — coarse weakness derived from a sub-test scaled
///   score below the Grade B threshold. Subtest is one of
///   <c>listening | reading | writing | speaking</c>.</item>
///   <item><c>{subtest}_{partOrSkill}_{focus}</c> — finer-grained weakness
///   surfaced by sub-test analytics (e.g. <c>listening_partA_spelling</c>).</item>
/// </list>
///
/// <para>
/// Resolve never throws; unknown tags return an empty list. Add new tags here
/// when sub-test analytics begin emitting them.
/// </para>
/// </summary>
public static class RemediationCatalog
{
    private static readonly Dictionary<string, RemediationDrillRef[]> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── Coarse, sub-test level ──────────────────────────────────────────
        ["low_listening"] = new[]
        {
            new RemediationDrillRef("listening.partB.dialogue", "Listening Part B drill", "Two short workplace dialogues with focused note-taking.", "listening", "/listening", 1),
            new RemediationDrillRef("listening.partA.consultation", "Listening Part A consultation drill", "Practice picking out details from a 5-minute consultation.", "listening", "/listening", 2),
            new RemediationDrillRef("recalls.audio.focus", "Recalls audio focus session", "10 minutes hearing and typing high-risk clinical terms.", "listening", "/recalls/words", 3),
        },
        ["low_reading"] = new[]
        {
            new RemediationDrillRef("reading.partC.scan", "Reading Part C scan-and-locate", "One Part C text with explanations after each item.", "reading", "/reading", 1),
            new RemediationDrillRef("reading.vocabulary.review", "Reading vocabulary review", "Top distractors from your last mock, in context.", "reading", "/reading", 2),
            new RemediationDrillRef("reading.timed.run", "Reading timed run", "20-minute Part B + Part C set — exam pace.", "reading", "/reading", 3),
        },
        ["low_writing"] = new[]
        {
            new RemediationDrillRef("writing.planning", "Writing planning skill", "Sketch a referral letter outline in 5 minutes — no full draft.", "writing", "/writing", 1),
            new RemediationDrillRef("writing.linkers", "Linker practice", "Rewrite three sentences using exam-grade linkers.", "writing", "/writing", 2),
            new RemediationDrillRef("writing.structure.audit", "Letter structure check", "Audit a past letter against the rubric headings.", "writing", "/writing", 3),
        },
        ["low_speaking"] = new[]
        {
            new RemediationDrillRef("speaking.warmup.roleplay", "Speaking warm-up roleplay", "5-minute scenario focused on your weakest indicator.", "speaking", "/speaking", 1),
            new RemediationDrillRef("recalls.audio.drill", "Recalls audio drill", "Hear British clinical pronunciation for common patient phrases and high-risk terms.", "speaking", "/recalls/words", 2),
            new RemediationDrillRef("speaking.fluency.loop", "Fluency loop", "60-second 'patient explanation' loops, 3 reps.", "speaking", "/speaking", 3),
        },

        // ── Fine-grained, part/skill level ──────────────────────────────────
        ["listening_partA_spelling"] = new[]
        {
            new RemediationDrillRef("listening.partA.spelling", "Listening Part A spelling drill", "Type the high-risk clinical terms you misspell most often, dictated at exam pace.", "listening", "/recalls/words", 1),
            new RemediationDrillRef("recalls.audio.focus", "Recalls audio focus session", "10 minutes hearing and typing high-risk clinical terms.", "listening", "/recalls/words", 2),
        },
        ["listening_partB_inference"] = new[]
        {
            new RemediationDrillRef("listening.partB.inference", "Listening Part B inference drill", "Short workplace exchanges where the answer is implied, not stated.", "listening", "/listening", 1),
        },
        ["reading_partC_inference"] = new[]
        {
            new RemediationDrillRef("reading.partC.inference", "Reading Part C inference drill", "Author-stance and implication questions on a single Part C text.", "reading", "/reading", 1),
        },
        ["writing_letter_structure"] = new[]
        {
            new RemediationDrillRef("writing.structure.audit", "Letter structure check", "Audit a past letter against the rubric headings.", "writing", "/writing", 1),
        },
        ["speaking_clinical_communication"] = new[]
        {
            new RemediationDrillRef("speaking.warmup.roleplay", "Speaking warm-up roleplay", "5-minute scenario focused on clinical communication indicators.", "speaking", "/speaking", 1),
        },
    };

    /// <summary>
    /// Returns the drill recommendations for a weakness tag, or an empty list
    /// when the tag is unknown. Never throws.
    /// </summary>
    public static IReadOnlyList<RemediationDrillRef> Resolve(string? weaknessTag)
    {
        if (string.IsNullOrWhiteSpace(weaknessTag)) return Array.Empty<RemediationDrillRef>();
        return Map.TryGetValue(weaknessTag, out var drills) ? drills : Array.Empty<RemediationDrillRef>();
    }

    /// <summary>
    /// Every weakness tag the catalog currently knows about. Used by
    /// administrators to validate weakness-emitting analytics jobs and by
    /// regression tests to prove every advertised tag resolves to ≥1 drill.
    /// </summary>
    public static IReadOnlyList<string> AllWeaknessTags { get; } = Map.Keys.ToArray();
}

/// <summary>
/// One drill recommendation surfaced to the learner inside a remediation plan.
/// Day offset is a hint (1 = today, 2 = tomorrow…); the plan generator may
/// re-distribute days when multiple weaknesses compete for slots.
/// </summary>
public sealed record RemediationDrillRef(
    string DrillId,
    string Label,
    string Description,
    string SkillCode,
    string RouteHref,
    int RecommendedDayOffset);
