using OetLearner.Api.Domain;
using OetLearner.Api.Services.Companion;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Rulebook;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// Manifest 1.B(b) — PDF handouts and model answers — and the rulebook-kind
/// correction that goes with it.
///
/// <para>
/// The chunking cases here are about citations. The companion is required to
/// point at an exact location, and a page number that is off by one is worse
/// than none at all: the learner opens the handout, does not find what they were
/// told, and stops trusting every citation after it.
/// </para>
/// </summary>
public sealed class CompanionDocumentIngestionTests
{
    // ── page fidelity ────────────────────────────────────────────────────────

    [Fact]
    public void Each_page_becomes_a_chunk_that_knows_its_page_number()
    {
        var pages = new[] { Long("First page about openings."), Long("Second page about ordering.") };

        var chunks = CompanionDocumentIndexer.BuildChunks(pages);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(1, chunks[0].PageNumber);
        Assert.Equal(2, chunks[1].PageNumber);
    }

    [Fact]
    public void A_blank_page_does_not_shift_the_pages_after_it()
    {
        // The index of a page in the extracted list IS its page number, so a
        // dropped cover page would move every later citation by one — the exact
        // off-by-one that makes a citation unverifiable.
        var pages = new[] { string.Empty, Long("Real content starts here.") };

        var chunks = CompanionDocumentIndexer.BuildChunks(pages);

        var chunk = Assert.Single(chunks);
        Assert.Equal(2, chunk.PageNumber);
    }

    [Fact]
    public void Short_pages_are_merged_rather_than_left_as_fragments()
    {
        // A slide deck of titles would otherwise produce dozens of three-word
        // chunks that match everything weakly and crowd out real evidence.
        var pages = new[] { "Openings", "Ordering", "Tone", Long("The substantive page.") };

        var chunks = CompanionDocumentIndexer.BuildChunks(pages);

        Assert.Single(chunks);
        Assert.Contains("Openings", chunks[0].Text, StringComparison.Ordinal);
        Assert.Contains("substantive", chunks[0].Text, StringComparison.Ordinal);

        // The merged chunk is attributed to where it started, so the citation
        // points at the first page a reader would need to open.
        Assert.Equal(1, chunks[0].PageNumber);
    }

    [Fact]
    public void An_oversized_page_is_split_rather_than_silently_truncated()
    {
        // A chunk longer than the retriever's per-source verbatim cap can only
        // ever come back truncated, which would make its tail unreachable no
        // matter what the learner asked.
        var page = string.Join("\n", Enumerable.Repeat(Long("A paragraph of guidance about letters."), 12));

        var chunks = CompanionDocumentIndexer.BuildChunks([page]);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.Equal(1, c.PageNumber));
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 1200, $"chunk of {c.Text.Length} chars is too long to survive the verbatim cap"));
    }

    [Fact]
    public void A_single_unbroken_paragraph_is_cut_without_duplicating_or_losing_text()
    {
        // The case the multi-line test above cannot reach: one long run with no
        // line break to split on. An earlier version emitted the first slice as a
        // chunk AND appended it to the buffer, so the opening of a dense handout
        // page came back twice while its tail was dropped.
        var page = string.Join(" ", Enumerable.Repeat("guidance", 800));

        var chunks = CompanionDocumentIndexer.BuildChunks([page]);

        Assert.True(chunks.Count > 1);

        var rejoined = string.Concat(chunks.Select(c => c.Text));
        Assert.Equal(page.Replace(" ", string.Empty), rejoined.Replace(" ", string.Empty));
    }

    [Fact]
    public void A_document_with_no_text_produces_nothing()
    {
        Assert.Empty(CompanionDocumentIndexer.BuildChunks([]));
        Assert.Empty(CompanionDocumentIndexer.BuildChunks(["", "   "]));
    }

    // ── the package scope a material inherits ────────────────────────────────

    [Theory]
    [InlineData("crash-medicine", VideoVisibilityScopes.Crash)]
    [InlineData("full-medicine", VideoVisibilityScopes.FullMedicine)]
    [InlineData("writing-crash-2026", VideoVisibilityScopes.Crash)]
    public void A_plan_restricted_folder_inherits_that_plans_package_scope(string planCode, string expected)
    {
        // Materials and videos must agree about what a package includes, so both
        // go through PackageScopePolicy rather than each deciding for itself.
        Assert.Equal(expected, PackageScopePolicy.Resolve(null, planCode, "medicine"));
    }

    [Fact]
    public void An_unrecognised_plan_stays_shared_rather_than_guessing()
    {
        // Guessing FULL_* for an unknown plan would hide material from the
        // learners on it. Shared is the safe direction for a legacy plan, and
        // the module gate still applies.
        Assert.Equal(VideoVisibilityScopes.Shared, PackageScopePolicy.Resolve(null, "some-legacy-plan", "medicine"));
    }

    // ── the rulebook kinds that reach a learner ──────────────────────────────

    [Fact]
    public void The_candidate_facing_exam_mode_books_are_loadable()
    {
        // These sat in the repository, approved, for months and reached nothing:
        // the loader looked for reading/_exam-mode/{profession}/ and the file has
        // no profession folder, so all thirteen lookups missed and All() never
        // yielded them. 161 candidate-facing rules, invisible.
        var loader = new RulebookLoader();
        var books = loader.All().ToList();

        var reading = Assert.Single(books, b => b.Kind == RuleKind.ReadingExamMode);
        var listening = Assert.Single(books, b => b.Kind == RuleKind.ListeningExamMode);

        Assert.True(reading.Rules.Count > 50, $"expected the full Reading exam-mode set, got {reading.Rules.Count}");
        Assert.True(listening.Rules.Count > 50, $"expected the full Listening exam-mode set, got {listening.Rules.Count}");
    }

    [Fact]
    public void The_exam_mode_books_carry_the_facts_the_packs_ask_for()
    {
        var loader = new RulebookLoader();
        var reading = loader.All().Single(b => b.Kind == RuleKind.ReadingExamMode);

        var text = string.Concat(reading.Rules.Select(r => r.Title + " " + r.Body));

        Assert.Contains("42", text, StringComparison.Ordinal);
        Assert.Contains("15", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_authoring_rulebooks_are_still_distinguishable_from_them()
    {
        // Both exist and they are for different audiences. The authoring books
        // instruct content authors ("the publish gate rejects any other shape")
        // and must not reach a learner-facing companion; the exam-mode books are
        // the candidate rules. Indexing the wrong one is contamination, not a gap.
        var loader = new RulebookLoader();
        var books = loader.All().ToList();

        var authoring = books.Where(b => b.Kind is RuleKind.Reading or RuleKind.Listening).ToList();
        Assert.NotEmpty(authoring);

        var authoringText = string.Concat(authoring.SelectMany(b => b.Rules).Select(r => r.Body));
        Assert.Contains("publish gate", authoringText, StringComparison.OrdinalIgnoreCase);

        var examMode = books.Where(b => b.Kind is RuleKind.ReadingExamMode or RuleKind.ListeningExamMode).ToList();
        var examModeText = string.Concat(examMode.SelectMany(b => b.Rules).Select(r => r.Body));
        Assert.DoesNotContain("publish gate", examModeText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Padding to clear the minimum-chunk threshold without saying anything.</summary>
    private static string Long(string text) =>
        text + " " + string.Join(" ", Enumerable.Repeat("Guidance continues here for the candidate.", 6));
}
