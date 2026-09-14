namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Official OET Writing descriptor engine — the candidate-scoring authority
/// (Writing Master Specification "Ultimate Final", 13 Sep 2026, §15.2).
/// Rendered into every grounded Writing grading prompt so the AI grades
/// holistically from official criterion descriptors instead of a fixed
/// one-mistake-equals-X-points deduction scheme. Implementation paraphrase;
/// not a reproduction of OET copyrighted wording.
/// </summary>
public static class WritingOetDescriptors
{
    public const string Version = "oet-writing-descriptors.v1.2026-09-13";

    /// <summary>
    /// The six-criterion descriptor anchors + grading contract. Appended to
    /// the grounded system prompt for Writing Score tasks.
    /// </summary>
    public const string DescriptorEngine = """
        OFFICIAL OET WRITING DESCRIPTOR ENGINE (v1) — CANDIDATE-SCORING AUTHORITY
        Six criteria. Purpose is scored 0-3; Content, Conciseness & Clarity, Genre & Style,
        Organisation & Layout, and Language are scored 0-7. Grade holistically from evidence.

        Purpose (0-3)
        - 3: the purpose and requested action are immediately clear AND sufficiently developed across the letter.
        - 2: discernible but under-highlighted or under-developed.
        - 1: delayed or weak, with very limited expansion.
        - 0: unclear, obscured or misunderstood.
        NOTE: a correct referral verb alone is not enough for full Purpose — it requires BOTH immediate clarity
        AND adequate development.

        Content (0-7)
        - 7: accurate, reader-appropriate, all key continuing-care/task information, no important omission.
        - 5: mostly appropriate and accurate with minor gaps.
        - 3: some key omissions or inaccuracies.
        - 1: insufficient or substantially inaccurate for reliable action.

        Conciseness & Clarity (0-7)
        - 7: length and detail fit the case and reader; effective summarising; irrelevant material excluded.
        - 5: mostly concise and clear.
        - 3: excess detail or poor summarising causes distraction.
        - 1: unnecessary, case-note-like detail seriously obscures communication.

        Genre & Style (0-7)
        - 7: factual clinical tone, register, technicality, abbreviations and politeness fit reader and purpose.
        - 5: mostly appropriate.
        - 3: intermittent mismatch causes reader effort.
        - 1: inadequate genre or reader awareness.

        Organisation & Layout (0-7)
        - 7: logical grouping, key information prominent, coherent paragraphs, easy navigation.
        - 5: generally clear and logical.
        - 3: inconsistent organisation or highlighting causes strain.
        - 1: illogical, case-note-order dependence, or poor layout.

        Language (0-7)
        - 7: grammar, vocabulary, spelling, punctuation and sentence control allow effortless meaning.
        - 5: minor slips that usually do not interfere.
        - 3: repeated inaccuracies cause some reader strain.
        - 1: frequent inaccuracies create substantial strain and may interfere with meaning.

        SCORING CONTRACT
        - Scores 6/4/2 represent performance between adjacent anchors; do NOT force every response into 7/5/3/1.
        - 0 is below the lowest functional anchor.
        - Grade holistically; NEVER use a fixed one-mistake-equals-X-points deduction scheme.
        - A defect may affect multiple criteria only if distinct impacts exist; never double-penalise one surface error.
        - The AI Estimated Practice Score /500 is derived ONLY after criterion assessment and is NOT a naive linear
          conversion of the 38 criterion points. Grade bands: A 450-500, B 350-440, C+ 300-340, C 200-290,
          D 100-190, E 0-90. The /500 score is a practice estimate, never an official OET-issued score.
        - The platform's deterministic audit may attach findings marked coaching-only (house style). Those are
          learning feedback; they must NOT lower a criterion score unless an independent OET criterion impact
          (clarity, accuracy, register, cohesion, safety, professionalism) genuinely exists in the candidate's text.
        - Never manufacture findings to fill a feedback template. If the letter is clean, it stays clean.
        """;
}
