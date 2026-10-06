using OetLearner.Api.Services.Companion;

namespace OetLearner.Api.Tests.Companion;

/// <summary>
/// The pure halves extracted from <c>CompanionDocumentIndexer</c> so a helper-side prep can run the SAME code
/// (OET-RWP/1 section 6.2), and the corrected file selection that finally walks past the first 200 files.
/// </summary>
public sealed class CompanionChunkerAndSelectionTests
{
    private static string Long(string seed) => seed + " " + new string('x', 260);

    // ── chunker ──────────────────────────────────────────────────────────────

    [Fact]
    public void ChecksumVersion_IsTheFirstSixteenLowercaseHexCharsOfTheSha256OfThePagesJoinedByNewline()
    {
        var pages = new[] { "Alpha page", "Beta page", string.Empty, "Gamma page" };

        var legacy = CompanionIndexWriter.Sha256(string.Join("\n", pages))[..16].ToLowerInvariant();

        Assert.Equal(legacy, CompanionChunker.ChecksumVersion(pages));
        Assert.Matches("^[0-9a-f]{16}$", CompanionChunker.ChecksumVersion(pages));
    }

    [Fact]
    public void ChecksumVersion_ChangesWhenAnyPageChanges()
    {
        var baseline = CompanionChunker.ChecksumVersion(new[] { "one", "two" });

        Assert.NotEqual(baseline, CompanionChunker.ChecksumVersion(new[] { "one", "twO" }));
        Assert.NotEqual(baseline, CompanionChunker.ChecksumVersion(new[] { "one", "two", string.Empty }));
        Assert.Equal(baseline, CompanionChunker.ChecksumVersion(new[] { "one", "two" }));
    }

    [Fact]
    public void Build_IsDeterministic_AndTheIndexerDelegatesToIt()
    {
        var pages = new[] { Long("First page."), "short", "tiny", Long("Fourth page."), new string('y', 2600) };

        var first = CompanionChunker.Build(pages);
        var second = CompanionChunker.Build(pages);
        var viaIndexer = CompanionDocumentIndexer.BuildChunks(pages);

        Assert.Equal(first.Select(c => (c.Heading, c.Text, c.PageNumber)), second.Select(c => (c.Heading, c.Text, c.PageNumber)));
        Assert.Equal(first.Select(c => (c.Heading, c.Text, c.PageNumber)), viaIndexer.Select(c => (c.Heading, c.Text, c.PageNumber)));
    }

    [Fact]
    public void Build_KeepsPageNumbersAndHeadings_AndRespectsTheBounds()
    {
        var pages = new[] { Long("First."), "x", Long("Third."), new string('z', 3000) };

        var chunks = CompanionChunker.Build(pages);

        Assert.All(chunks, chunk =>
        {
            Assert.True(chunk.Text.Length <= CompanionChunker.MaxChunkChars);
            Assert.True(chunk.PageNumber is >= 1 and <= 4);
            Assert.Matches(@"^Page [0-9]+( \([0-9]+\))?$", chunk.Heading);
        });
        Assert.Equal(1, chunks[0].PageNumber);
        // the very long fourth page is split into several parts that all cite page 4
        Assert.True(chunks.Count(chunk => chunk.PageNumber == 4) >= 3);
    }

    [Fact]
    public void Build_OfNothingOrBlankPages_IsEmpty()
    {
        Assert.Empty(CompanionChunker.Build(Array.Empty<string>()));
        Assert.Empty(CompanionChunker.Build(new[] { string.Empty, "   ", "\n" }));
    }

    // ── file selection ───────────────────────────────────────────────────────

    private sealed record Doc(int Id, DateTimeOffset Updated);

    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Take_PrefersNeverIndexedThenEditedSinceThenTheLeastRecentlyIndexed()
    {
        var indexed = new Dictionary<int, DateTimeOffset?>
        {
            [1] = null,                            // A: never indexed, oldest edit
            [2] = null,                            // B: never indexed, newer edit
            [3] = Epoch.AddDays(-5),               // C: edited AFTER its last index
            [4] = Epoch.AddDays(-1),               // D: up to date, indexed recently
            [5] = Epoch.AddDays(-3),               // E: up to date, indexed longer ago
        };
        var files = new[]
        {
            new Doc(1, Epoch.AddDays(-10)),
            new Doc(2, Epoch.AddDays(-1)),
            new Doc(3, Epoch.AddDays(-2)),
            new Doc(4, Epoch.AddDays(-9)),
            new Doc(5, Epoch.AddDays(-8)),
        };

        var order = CompanionIndexSelection.Take(files, f => f.Updated, f => indexed[f.Id], 5).Select(f => f.Id).ToArray();

        Assert.Equal(new[] { 1, 2, 3, 5, 4 }, order);
    }

    [Fact]
    public void Take_AFileThatRecentlyYieldedNothing_GoesToTheBack()
    {
        var files = new[] { new Doc(1, Epoch), new Doc(2, Epoch.AddDays(1)) };

        var order = CompanionIndexSelection
            .Take(files, f => f.Updated, _ => null, 2, recentlyFailed: f => f.Id == 1)
            .Select(f => f.Id)
            .ToArray();

        Assert.Equal(new[] { 2, 1 }, order);
    }

    [Fact]
    public void Take_ALimitOfZeroOrLess_SelectsNothing()
    {
        var files = new[] { new Doc(1, Epoch) };

        Assert.Empty(CompanionIndexSelection.Take(files, f => f.Updated, _ => null, 0));
        Assert.Empty(CompanionIndexSelection.Take(files, f => f.Updated, _ => null, -3));
    }

    [Fact]
    public void Take_SuccessiveRunsWalkPastTheFirstTwoHundredFiles()
    {
        // The old selection (OrderBy(UpdatedAt).Take(200)) re-picked the SAME oldest 200 forever, because nothing
        // updated the files themselves. Here the indexer stamps what it processed, so the 201st file IS reached.
        const int total = 250;
        const int cap = 200;
        var files = Enumerable.Range(0, total).Select(i => new Doc(i, Epoch.AddDays(-1000 + i))).ToList();
        var indexed = new Dictionary<int, DateTimeOffset?>();
        foreach (var file in files) indexed[file.Id] = null;

        var firstRun = CompanionIndexSelection.Take(files, f => f.Updated, f => indexed[f.Id], cap).Select(f => f.Id).ToList();
        Assert.Equal(cap, firstRun.Count);
        Assert.Equal(Enumerable.Range(0, cap), firstRun.OrderBy(id => id));
        foreach (var id in firstRun) indexed[id] = Epoch;

        var secondRun = CompanionIndexSelection.Take(files, f => f.Updated, f => indexed[f.Id], cap).Select(f => f.Id).ToList();

        // files 200..249 were never indexed, so they lead the second run
        Assert.Equal(Enumerable.Range(cap, total - cap), secondRun.Take(total - cap));
        Assert.Contains(200, secondRun);
        Assert.Equal(total, firstRun.Concat(secondRun).Distinct().Count());
    }

    [Fact]
    public void Take_TheTwentyFirstFileIsReachedOnTheSecondRunWithAnyCap()
    {
        var files = Enumerable.Range(0, 25).Select(i => new Doc(i, Epoch.AddDays(i))).ToList();
        var indexed = files.ToDictionary(f => f.Id, _ => (DateTimeOffset?)null);

        var first = CompanionIndexSelection.Take(files, f => f.Updated, f => indexed[f.Id], 20).Select(f => f.Id).ToList();
        foreach (var id in first) indexed[id] = Epoch.AddDays(100);
        var second = CompanionIndexSelection.Take(files, f => f.Updated, f => indexed[f.Id], 20).Select(f => f.Id).ToList();

        Assert.DoesNotContain(20, first);
        Assert.Equal(new[] { 20, 21, 22, 23, 24 }, second.Take(5).ToArray());
    }

    [Fact]
    public void NeedsIndexing_IsTrueForNeverIndexedAndForFilesEditedAfterTheirLastIndex()
    {
        Assert.True(CompanionIndexSelection.NeedsIndexing(Epoch, null));
        Assert.True(CompanionIndexSelection.NeedsIndexing(Epoch.AddDays(1), Epoch));
        Assert.False(CompanionIndexSelection.NeedsIndexing(Epoch, Epoch));
        Assert.False(CompanionIndexSelection.NeedsIndexing(Epoch.AddDays(-1), Epoch));
    }
}
