using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

/// <summary>
/// Mocks V2 Wave 5 — proves the weakness → drill catalog stays internally
/// consistent. Every advertised tag must resolve to ≥1 drill, and unknown
/// tags must always return an empty list (never throw).
/// </summary>
public class RemediationCatalogTests
{
    [Fact]
    public void Resolve_KnownFineGrainedTag_ReturnsAtLeastOneDrill()
    {
        var drills = RemediationCatalog.Resolve("listening_partA_spelling");

        Assert.NotEmpty(drills);
        Assert.All(drills, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.DrillId));
            Assert.False(string.IsNullOrWhiteSpace(d.Label));
            Assert.False(string.IsNullOrWhiteSpace(d.RouteHref));
            Assert.Equal("listening", d.SkillCode);
            Assert.True(d.RecommendedDayOffset >= 1);
        });
    }

    [Fact]
    public void Resolve_UnknownTag_ReturnsEmpty()
    {
        Assert.Empty(RemediationCatalog.Resolve("__unknown__"));
    }

    [Fact]
    public void Resolve_NullOrWhitespace_ReturnsEmpty()
    {
        Assert.Empty(RemediationCatalog.Resolve(null));
        Assert.Empty(RemediationCatalog.Resolve(""));
        Assert.Empty(RemediationCatalog.Resolve("   "));
    }

    [Fact]
    public void AllWeaknessTags_EveryTagResolvesToAtLeastOneDrill()
    {
        Assert.NotEmpty(RemediationCatalog.AllWeaknessTags);

        foreach (var tag in RemediationCatalog.AllWeaknessTags)
        {
            var drills = RemediationCatalog.Resolve(tag);
            Assert.True(drills.Count >= 1, $"Tag '{tag}' resolved to 0 drills.");
        }
    }

    [Fact]
    public void AllWeaknessTags_IncludesCoarseSubtestTags()
    {
        var tags = RemediationCatalog.AllWeaknessTags;
        Assert.Contains("low_listening", tags);
        Assert.Contains("low_reading", tags);
        Assert.Contains("low_writing", tags);
        Assert.Contains("low_speaking", tags);
    }
}
