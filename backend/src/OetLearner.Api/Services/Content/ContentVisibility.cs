using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Content;

/// <summary>
/// Single owner of the fail-closed learner visibility gate: a candidate
/// surface may show a paper only when it is Published AND explicitly marked
/// <see cref="ContentPaper.CandidateVisible"/>. Every learner query routes
/// through <see cref="WhereCandidateVisible"/> (or
/// <see cref="WhereAttemptable"/> where archived attempts are policy-allowed)
/// and every in-memory check through <see cref="IsCandidateVisible"/> — the
/// predicate is never re-derived at call sites.
///
/// Navigation-shaped queries (e.g. paper reached via
/// <c>ContentPaperAsset.Paper</c>) keep their inline conjuncts for EF
/// translation and are covered by service-level hide/show vectors instead.
/// </summary>
public static class ContentVisibility
{
    /// <summary>
    /// In-memory gate: true only for Published + explicitly visible papers.
    /// Hidden, draft, and archived papers are indistinguishable from missing.
    /// </summary>
    public static bool IsCandidateVisible(ContentPaper? paper)
        => paper is not null
            && paper.Status == ContentStatus.Published
            && paper.CandidateVisible;

    /// <summary>
    /// Query gate: restricts a paper query to the learner-visible set.
    /// </summary>
    public static IQueryable<ContentPaper> WhereCandidateVisible(this IQueryable<ContentPaper> papers)
        => papers.Where(p => p.Status == ContentStatus.Published && p.CandidateVisible);

    /// <summary>
    /// Attempt gate: like <see cref="WhereCandidateVisible"/>, additionally
    /// admitting Archived papers when policy allows attempts on them.
    /// </summary>
    public static IQueryable<ContentPaper> WhereAttemptable(this IQueryable<ContentPaper> papers, bool allowArchived)
        => allowArchived
            ? papers.Where(p => p.CandidateVisible
                && (p.Status == ContentStatus.Published || p.Status == ContentStatus.Archived))
            : papers.WhereCandidateVisible();
}
