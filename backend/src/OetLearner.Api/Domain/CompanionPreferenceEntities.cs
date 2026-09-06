using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// How the companion should teach. Serves F-011 (teaching-style preference),
/// F-050 (Socratic tutor) and F-052 (coach mode) — the source specification
/// lists them separately, but they are one choice from the learner's side:
/// "how do you want to be taught?"
/// </summary>
public enum CompanionTeachingStyle
{
    /// <summary>Answer directly, then explain. The default: most candidates are
    /// short on time and a question answered with a question is a cost.</summary>
    Direct = 0,

    /// <summary>F-050. Lead with a question that makes the learner reason it out,
    /// give the answer only after they try or ask for it.</summary>
    Socratic = 1,

    /// <summary>F-052. Motivational framing, progress-aware, short next steps.</summary>
    Coaching = 2,
}

/// <summary>How much detail the learner wants in a normal answer.</summary>
public enum CompanionExplanationDepth
{
    Brief = 0,
    Standard = 1,
    Deep = 2,
}

/// <summary>
/// One row per learner. Absent means every default, so the companion works
/// before a learner has expressed any preference — the row is created on first
/// save, never required.
/// </summary>
public sealed class CompanionPreference
{
    [Key]
    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public CompanionTeachingStyle TeachingStyle { get; set; } = CompanionTeachingStyle.Direct;

    public CompanionExplanationDepth Depth { get; set; } = CompanionExplanationDepth.Standard;

    /// <summary>
    /// F-055 — English-only mode. When true the companion replies in English even
    /// to an Arabic message. Opt-in immersion, never imposed: defaulting this on
    /// would silently take away the Arabic support many candidates rely on.
    /// </summary>
    public bool EnglishOnly { get; set; }

    /// <summary>Prefer a worked example from the learner's profession over prose.</summary>
    public bool PreferWorkedExamples { get; set; } = true;

    public DateTimeOffset UpdatedAt { get; set; }
}
