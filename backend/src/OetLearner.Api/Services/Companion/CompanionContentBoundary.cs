using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Companion;

/// <summary>
/// The content-protection boundary for Sami.
///
/// <para>
/// Dr Ahmed Hesham's Writing workshops, Speaking workshops and correction sessions are
/// <b>never knowledge sources for Sami</b> (owner directive, 9 Oct 2026). Ownership of a
/// course lets a candidate <i>open and watch</i> the protected resource on the platform; it
/// does not make the underlying workshop, video or transcript content available to Sami for
/// retrieval, summarising or reproduction.
/// </para>
///
/// <para>
/// This is enforced at the <b>retrieval</b> layer, not only in the prompt. A prompt rule is a
/// request; a source-type filter on the candidate set is a guarantee, and it holds against
/// paraphrasing, "tell me what is inside this lesson", indirect questions and jailbreaks
/// alike — because there is nothing to leak: the content never enters the context window.
/// </para>
///
/// <para>
/// <b>Why a denylist and not just "nothing ingests these today".</b> The indexers do skip
/// audio and video currently, but that is an ingestion-side accident of scope rather than an
/// enforced guarantee. If someone later adds a workshop ingest path, a transcript import, or
/// a PDF of a correction session, the retrieval gate still holds and the content still cannot
/// reach a learner. The boundary is deliberately independent of who writes what into the
/// corpus.
/// </para>
///
/// <para>
/// <b>Metadata and navigation are deliberately still allowed.</b> Sami may tell a candidate
/// that a workshop exists, name it, say which category it belongs to, say where in the
/// platform it lives, say whether their package entitles them, and deep-link them to it. That
/// is navigation, not teaching, and it is served from the platform map — never from the
/// protected body. See <see cref="IsProtected"/> for what is blocked.
/// </para>
/// </summary>
public static class CompanionContentBoundary
{
    /// <summary>
    /// Source types that may never be retrieved into Sami's context, whatever their state,
    /// however they were ingested, and whoever owns the course.
    ///
    /// <para>
    /// Compared case-insensitively. Keep entries lower-case. The set is intentionally a
    /// substring match at the edges (see <see cref="IsProtectedType"/>) so that a future
    /// variant name such as <c>workshop_transcript_v2</c> or <c>correction_session_audio</c>
    /// is caught by the same rule rather than slipping through on a naming difference.
    /// </para>
    /// </summary>
    private static readonly string[] ProtectedTypeFragments =
    [
        // Writing / Speaking workshops
        "workshop",

        // Correction sessions
        "correction_session",
        "correction-session",
        "correctionsession",

        // Protected audio/video teaching material and anything transcribed from it
        "video",
        "audio",
        "transcript",
        "webinar",
        "masterclass",
        "recording",
    ];

    /// <summary>
    /// True when a source type names protected teaching content and must never enter
    /// retrieval.
    /// </summary>
    /// <remarks>
    /// Matched on fragments rather than an exact list on purpose: the failure mode we care
    /// about is a <i>new</i> source type quietly carrying protected content, and an exact-match
    /// list would let it through. The cost of the looser match is that a future legitimate
    /// source type containing one of these words is also blocked — which is the safe direction
    /// to fail, and is surfaced by the unit-level assertions in the admin diagnostics rather
    /// than silently.
    /// </remarks>
    public static bool IsProtectedType(string? sourceType)
    {
        if (string.IsNullOrWhiteSpace(sourceType)) return false;

        foreach (var fragment in ProtectedTypeFragments)
        {
            if (sourceType.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when this source must be excluded from Sami's candidate set.
    /// </summary>
    public static bool IsProtected(CompanionSource source) => IsProtectedType(source.SourceType);

    /// <summary>
    /// The protected fragments, for admin diagnostics and for the audit assertion that no
    /// approved source currently in the corpus matches one of them.
    /// </summary>
    public static IReadOnlyList<string> ProtectedFragments => ProtectedTypeFragments;

    /// <summary>
    /// The candidate-facing explanation to use when a learner asks for the content of a
    /// protected workshop, video or correction session.
    ///
    /// <para>
    /// Deliberately specific about what Sami <i>can</i> do, so the refusal is a redirection to
    /// the real resource rather than a dead end — the point is that the candidate watches the
    /// teaching, and Sami helps them prepare for and follow up on it.
    /// </para>
    /// </summary>
    public const string ProtectedContentRefusal =
        "I can't reproduce, summarise or teach from the contents of that workshop, video or " +
        "correction session — that material is Dr Ahmed Hesham's protected teaching content, and " +
        "watching it is part of the course. What I can do is tell you where it is and whether your " +
        "package includes it, help you prepare before you watch it, and work through your own " +
        "questions and practice afterwards.";
}
