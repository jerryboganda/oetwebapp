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

    // V2 Medium #5 (May 2026 audit closure) — canonical drill ID + route gates.

    [Fact]
    public void AllDrillIds_AreNonEmpty_AndUniquePerCatalog()
    {
        var drillIds = RemediationCatalog.AllDrillIds;
        Assert.NotEmpty(drillIds);
        Assert.All(drillIds, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(drillIds.Count, drillIds.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AllDrillIds_FollowDottedNamespaceShape()
    {
        // Every drill ID is dotted (`<area>.<sub>...`) so future migration to a
        // canonical drill table can map by namespace prefix.
        foreach (var drillId in RemediationCatalog.AllDrillIds)
        {
            Assert.True(
                drillId.Contains('.'),
                $"Drill id '{drillId}' is missing a dotted namespace separator. Use 'area.sub' shape.");
            Assert.False(
                drillId.StartsWith('.') || drillId.EndsWith('.'),
                $"Drill id '{drillId}' has a leading/trailing dot.");
        }
    }

    [Fact]
    public void AllRouteHrefs_StartWithACanonicalAppPrefix()
    {
        var prefixes = RemediationCatalog.CanonicalRoutePrefixes;
        Assert.NotEmpty(prefixes);

        foreach (var href in RemediationCatalog.AllRouteHrefs)
        {
            Assert.True(
                prefixes.Any(p => href == p || href.StartsWith(p + "/", StringComparison.Ordinal)),
                $"Route '{href}' does not start with any canonical prefix ({string.Join(", ", prefixes)}).");
        }
    }

    [Fact]
    public void Generic_FallbackDrill_PointsAtTheDashboard()
    {
        // The generic fallback in RemediationPlanService.ResolveDrillsFor
        // uses /dashboard. Locking this here so a future refactor doesn't
        // silently drop the canonical fallback target.
        Assert.Contains("/dashboard", RemediationCatalog.CanonicalRoutePrefixes);
    }
}
