using System.Text;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant.Indexing;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Companion;

public interface ICompanionRulebookIndexer
{
    /// <summary>
    /// Indexes the approved rulebooks into the companion knowledge store.
    /// Idempotent: a rule whose text is unchanged keeps its existing embedding.
    /// </summary>
    Task<CompanionIndexResult> IndexAsync(
        IReadOnlyCollection<ExamProfession>? professions,
        bool embed,
        CancellationToken ct);
}

public sealed record CompanionIndexResult(
    int SourcesWritten,
    int ChunksWritten,
    int ChunksUnchanged,
    int ChunksEmbedded,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Builds the companion corpus from the versioned rulebooks.
///
/// <para>
/// It reads through <see cref="IRulebookLoader"/> — which DI resolves to the
/// DB-backed loader — so admin edits and rulebook versions flow into the index
/// instead of the index drifting against a snapshot of the JSON files.
/// </para>
///
/// <para><b>Chunking.</b> One chunk per <see cref="OetRule"/>. The source
/// specification requires chunking "by pedagogical meaning … not arbitrary fixed
/// size", and a rule is exactly that unit: it has a stable id, a section, a
/// body, and its own exemplars. It also makes citations meaningful — the
/// companion can say <i>which rule</i> it is teaching from.</para>
///
/// <para><b>Authority.</b> Profession rulebooks are
/// <see cref="CompanionAuthorityClass.ProfessionApprovedMethod"/>, which gives
/// them precedence over generic teaching advice while still ranking below a
/// verified official exam fact. The <c>_exam-mode</c> books are the exception and
/// are classed as official fact — see <see cref="IndexableKinds"/>.</para>
///
/// <para><b>Proprietary.</b> Rulebooks are Dr Hesham's paid teaching method, so
/// those sources are marked <see cref="CompanionSource.IsProprietary"/>. They stay
/// retrievable (the companion must be able to teach the method) but the
/// retriever enforces verbatim-span and volume caps so the assistant cannot be
/// used to reconstruct a rulebook.</para>
/// </summary>
public sealed class CompanionRulebookIndexer(
    LearnerDbContext db,
    IRulebookLoader rulebooks,
    IEmbeddingService embeddings,
    ILogger<CompanionRulebookIndexer> logger) : ICompanionRulebookIndexer
{
    /// <summary>
    /// Rulebook kinds that carry <b>learner-facing</b> teaching method.
    ///
    /// <para>
    /// <c>Reading</c> and <c>Listening</c> are deliberately absent and their
    /// <c>*ExamMode</c> siblings deliberately present. That looks like a mistake
    /// and is the opposite of one: <c>rulebooks/reading/{profession}/</c> is the
    /// <i>OET Reading Authoring Rulebook</i>, instructions to the people who write
    /// papers — "The publish gate rejects any other shape", "Authors never
    /// hardcode pass/fail thresholds in extracted structures" — while
    /// <c>rulebooks/reading/_exam-mode/</c> holds the 81 candidate-facing rules
    /// about how the exam actually runs. Indexing the first into a learner-facing
    /// companion is contamination rather than coverage; indexing the second is
    /// the Reading and Listening methodology the Manifest asks for, and it was
    /// sitting unused in the repository the whole time.
    /// </para>
    ///
    /// <para>
    /// <c>Remediation</c> is absent for a different reason: those books declare
    /// themselves a "V0 stub for the AI-personalisation feature flag".
    /// </para>
    /// </summary>
    private static readonly RuleKind[] IndexableKinds =
    [
        RuleKind.Writing,
        RuleKind.Speaking,
        RuleKind.ReadingExamMode,
        RuleKind.ListeningExamMode,
        RuleKind.Grammar,
        RuleKind.Vocabulary,
        RuleKind.Pronunciation,
        RuleKind.Conversation,
    ];

    /// <summary>
    /// Author-facing specifications. Reported in the warnings so an operator can
    /// see they were held back deliberately rather than lost.
    /// </summary>
    private static readonly RuleKind[] AuthoringOnlyKinds = [RuleKind.Reading, RuleKind.Listening];

    public async Task<CompanionIndexResult> IndexAsync(
        IReadOnlyCollection<ExamProfession>? professions,
        bool embed,
        CancellationToken ct)
    {
        var warnings = new List<string>();
        var sourcesWritten = 0;
        var chunksWritten = 0;
        var chunksUnchanged = 0;
        var chunksEmbedded = 0;

        var all = rulebooks.All()
            .Where(b => professions is null || professions.Count == 0 || professions.Contains(b.Profession))
            .ToList();

        var heldBack = all.Count(b => AuthoringOnlyKinds.Contains(b.Kind));
        if (heldBack > 0)
        {
            warnings.Add(
                $"{heldBack} authoring-specification rulebook(s) held out of the learner corpus on purpose: " +
                "they instruct content authors, not candidates. Candidate-facing Reading and Listening rules " +
                "come from the _exam-mode books instead.");
        }

        var books = all.Where(b => IndexableKinds.Contains(b.Kind)).ToList();

        if (books.Count == 0)
        {
            warnings.Add("No rulebooks matched the requested scope; nothing indexed.");
            return new CompanionIndexResult(0, 0, 0, 0, warnings);
        }

        foreach (var book in books)
        {
            ct.ThrowIfCancellationRequested();

            if (book.Rules.Count == 0)
            {
                warnings.Add($"Rulebook {book.Kind}/{book.Profession} has no rules; skipped.");
                continue;
            }

            var chunks = book.Rules
                .Select(rule => new CompanionChunkDraft(
                    string.IsNullOrWhiteSpace(rule.Title) ? rule.Id : $"{rule.Id} — {rule.Title}",
                    BuildChunkText(book, rule)))
                .Where(c => !string.IsNullOrWhiteSpace(c.Text))
                .ToList();

            var isExamMode = book.Kind is RuleKind.ReadingExamMode or RuleKind.ListeningExamMode;
            var subtest = MapSubtest(book.Kind);
            var version = string.IsNullOrWhiteSpace(book.Version) ? "v1" : book.Version;

            var result = await CompanionIndexWriter.WriteAsync(
                db, embeddings, logger, BuildSourceKey(book), version,
                source =>
                {
                    source.SourceType = isExamMode ? "exam_format" : "rulebook";
                    source.Title = isExamMode
                        ? $"OET {subtest} exam format and rules"
                        : $"{book.Kind} rulebook — {book.Profession}";

                    // The _exam-mode books state how the exam itself runs: part
                    // counts, timings, marks, what the screen does. That is an
                    // official exam fact rather than Dr Hesham's opinion about
                    // one, and classing it as method would hollow out the
                    // official-vs-method distinction the packs repeatedly test.
                    // They are already approved, shipped product content, so
                    // unlike the newly authored Phase 4 facts they are not staged
                    // for re-approval.
                    source.AuthorityClass = isExamMode
                        ? CompanionAuthorityClass.OfficialCurrentFact
                        : CompanionAuthorityClass.ProfessionApprovedMethod;

                    // Rulebooks here are already the approved teaching artefact:
                    // DbBackedRulebookLoader serves admin-approved DB rows in preference
                    // to the embedded JSON, so reaching here means it is publishable.
                    source.State = CompanionSourceState.Approved;
                    source.ExamTypeCode = "OET";

                    // Exam-mode rules are profession-agnostic by their own R01.6 /
                    // R01.10; the "medicine" folder is a sentinel, and honouring it
                    // would hide the Reading format from every nurse who asked.
                    source.ProfessionId = isExamMode
                        ? null
                        : book.Profession.ToString().ToLowerInvariant();
                    source.SubtestCode = subtest;

                    // How the exam works is public fact. The method for passing it
                    // is the paid product.
                    source.IsProprietary = !isExamMode;
                    source.RequiredEntitlementScope = null;

                    // Package isolation is applied to course MATERIAL (Phase 3),
                    // not to the method. A Crash learner bought a condensed route
                    // through the same rules; scoping the rulebooks to FULL_* would
                    // deny them the thing they paid for.
                    source.PackageScope = null;

                    source.SourceUrl = isExamMode ? book.AuthoritySource : null;
                    source.StorageLocator = isExamMode
                        ? $"rulebooks/{subtest}/_exam-mode/rulebook.{version}.json"
                        : $"rulebooks/{book.Kind.ToString().ToLowerInvariant()}/{book.Profession.ToString().ToLowerInvariant()}/rulebook.{version}.json";
                    source.ApprovedAt ??= DateTimeOffset.UtcNow;
                },
                chunks, embed, warnings, ct);

            sourcesWritten += result.SourcesWritten;
            chunksWritten += result.ChunksWritten;
            chunksUnchanged += result.ChunksUnchanged;
            chunksEmbedded += result.ChunksEmbedded;
        }

        logger.LogInformation(
            "Companion rulebook index: {Sources} sources, {Written} chunks written, {Unchanged} unchanged, {Embedded} embedded.",
            sourcesWritten, chunksWritten, chunksUnchanged, chunksEmbedded);

        return new CompanionIndexResult(sourcesWritten, chunksWritten, chunksUnchanged, chunksEmbedded, warnings);
    }

    /// <summary>
    /// Flattens a rule into retrievable text. Exemplars and forbidden patterns are
    /// included because they are the part learners most often ask about.
    /// </summary>
    private static string BuildChunkText(OetRulebook book, OetRule rule)
    {
        var sb = new StringBuilder();

        var section = book.Sections.FirstOrDefault(s =>
            string.Equals(s.Id, rule.Section, StringComparison.OrdinalIgnoreCase));

        sb.Append(book.Kind).Append(" · ").Append(book.Profession);
        if (section is not null) sb.Append(" · ").Append(section.Title);
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(rule.Title)) sb.AppendLine(rule.Title);
        if (!string.IsNullOrWhiteSpace(rule.Body)) sb.AppendLine(rule.Body);

        sb.Append("Severity: ").Append(rule.Severity).AppendLine();

        if (rule.ExemplarPhrases is { Count: > 0 })
        {
            sb.AppendLine("Good examples:");
            foreach (var phrase in rule.ExemplarPhrases) sb.Append("- ").AppendLine(phrase);
        }

        if (rule.ForbiddenPatterns is { Count: > 0 })
        {
            sb.AppendLine("Avoid:");
            foreach (var pattern in rule.ForbiddenPatterns) sb.Append("- ").AppendLine(pattern);
        }

        return sb.ToString().Trim();
    }

    private static string BuildSourceKey(OetRulebook book) =>
        $"rulebook:{book.Kind.ToString().ToLowerInvariant()}:{book.Profession.ToString().ToLowerInvariant()}";

    private static string? MapSubtest(RuleKind kind) => kind switch
    {
        RuleKind.Writing => "writing",
        RuleKind.Speaking or RuleKind.Conversation or RuleKind.Pronunciation => "speaking",
        RuleKind.Reading or RuleKind.ReadingExamMode => "reading",
        RuleKind.Listening or RuleKind.ListeningExamMode => "listening",
        _ => null, // grammar and vocabulary apply across every subtest
    };
}
