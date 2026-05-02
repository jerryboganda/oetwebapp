using OetLearner.Api.Services.Recalls;

namespace OetLearner.Api.Tests;

public class SpellingDiffTests
{
    [Fact]
    public void Exact_match_is_correct()
    {
        var r = SpellingDiff.Classify("anaemia", "anaemia");
        Assert.Equal(SpellingDiff.Correct, r.Code);
        Assert.Equal(0, r.Distance);
        Assert.True(r.IsCorrect);
        Assert.All(r.Segments, s => Assert.Equal("equal", s.Kind));
    }

    [Fact]
    public void Whitespace_is_trimmed_before_match()
    {
        var r = SpellingDiff.Classify("anaemia", "  anaemia  ");
        Assert.Equal(SpellingDiff.Correct, r.Code);
    }

    [Fact]
    public void Case_only_difference_is_classified()
    {
        var r = SpellingDiff.Classify("Anaemia", "anaemia");
        Assert.Equal(SpellingDiff.CaseOnly, r.Code);
        Assert.True(r.IsCorrect);
    }

    [Fact]
    public void American_spelling_is_flagged_as_british_variant()
    {
        var r = SpellingDiff.Classify("anaemia", "anemia", americanSpelling: "anemia");
        Assert.Equal(SpellingDiff.BritishVariant, r.Code);
        Assert.False(r.IsCorrect);
    }

    [Fact]
    public void Diarrhea_versus_diarrhoea_is_british_variant()
    {
        var r = SpellingDiff.Classify("diarrhoea", "diarrhea", americanSpelling: "diarrhea");
        Assert.Equal(SpellingDiff.BritishVariant, r.Code);
    }

    [Fact]
    public void Single_missing_letter_is_classified()
    {
        var r = SpellingDiff.Classify("asthma", "astma");
        Assert.Equal(SpellingDiff.MissingLetter, r.Code);
        Assert.Equal(1, r.Distance);
        Assert.Contains(r.Segments, s => s.Kind == "missing" && s.Text == "h");
    }

    [Fact]
    public void Single_extra_letter_is_classified()
    {
        var r = SpellingDiff.Classify("hospital", "hosspital");
        Assert.Equal(SpellingDiff.ExtraLetter, r.Code);
        Assert.Equal(1, r.Distance);
        Assert.Contains(r.Segments, s => s.Kind == "extra");
    }

    [Fact]
    public void Adjacent_transposition_is_classified()
    {
        var r = SpellingDiff.Classify("receive", "recieve");
        Assert.Equal(SpellingDiff.Transposition, r.Code);
        Assert.Equal(2, r.Distance);
    }

    [Fact]
    public void Double_letter_collapse_is_classified()
    {
        var r = SpellingDiff.Classify("inflammation", "inflamation");
        Assert.Equal(SpellingDiff.DoubleLetter, r.Code);
    }

    [Fact]
    public void Hyphen_difference_is_classified()
    {
        var r = SpellingDiff.Classify("follow-up", "followup");
        Assert.Equal(SpellingDiff.Hyphen, r.Code);
    }

    [Fact]
    public void Hyphen_difference_reverse_is_classified()
    {
        var r = SpellingDiff.Classify("followup", "follow-up");
        Assert.Equal(SpellingDiff.Hyphen, r.Code);
    }

    [Fact]
    public void Homophone_match_is_classified()
    {
        var r = SpellingDiff.Classify(
            "thyroid",
            "thigh road",
            similarSounding: ["thigh road"]);
        Assert.Equal(SpellingDiff.Homophone, r.Code);
    }

    [Fact]
    public void Far_off_typing_is_unknown()
    {
        var r = SpellingDiff.Classify("haemorrhage", "xylophone");
        Assert.Equal(SpellingDiff.Unknown, r.Code);
        Assert.True(r.Distance > 2);
    }

    [Theory]
    [InlineData("", "x", 1)]
    [InlineData("x", "", 1)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("flaw", "lawn", 2)]
    [InlineData("anaemia", "anaemia", 0)]
    public void Edit_distance_matches_classic_examples(string a, string b, int expected)
        => Assert.Equal(expected, SpellingDiff.EditDistance(a, b));

    [Fact]
    public void Diff_segments_coalesce_runs_of_same_kind()
    {
        var segs = SpellingDiff.DiffSegments("abcdef", "abxxef");
        // Equal "ab", missing "cd", extra "xx", equal "ef"
        Assert.Equal(4, segs.Count);
        Assert.Equal("equal", segs[0].Kind);
        Assert.Equal("ab", segs[0].Text);
        Assert.Equal("equal", segs[^1].Kind);
        Assert.Equal("ef", segs[^1].Text);
    }

    [Fact]
    public void Empty_canonical_returns_unknown()
    {
        var r = SpellingDiff.Classify("", "anything");
        Assert.Equal(SpellingDiff.Unknown, r.Code);
    }

    [Fact]
    public void Null_canonical_throws()
    {
        Assert.Throws<ArgumentNullException>(() => SpellingDiff.Classify(null!, "x"));
    }
}
