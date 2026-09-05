using OetLearner.Api.Domain;

namespace OetLearner.Api.Tests.Speaking;

// Pure-unit cover for `RolePlayCardTasks`, the helper that backs the
// unbounded task lists on `RolePlayCard` / `InterlocutorScript`.
//
// The truncation test below is not hypothetical: the owner's corpus of real
// printed OET cards contains a candidate bullet of 602 characters. Before the
// clamp, mirroring it into the legacy `varchar(500)` Task1..Task5 columns made
// Postgres reject the whole INSERT, so a valid card could not be saved at all.
public sealed class RolePlayCardTasksTests
{
    [Fact]
    public void MirrorToLegacyColumns_ClampsToTheLegacyColumnWidth()
    {
        var longBullet = new string('x', 602);
        string? task1 = null;

        RolePlayCardTasks.MirrorToLegacyColumns([longBullet], s => task1 = s);

        Assert.Equal(RolePlayCardTasks.LegacyColumnLength, task1!.Length);
        Assert.StartsWith(task1, longBullet, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_KeepsTheFullTextTheLegacyMirrorHadToClip()
    {
        var longBullet = new string('x', 602);

        var roundTripped = RolePlayCardTasks.Effective(
            RolePlayCardTasks.Serialize([longBullet]));

        Assert.Equal(longBullet, Assert.Single(roundTripped));
    }

    [Fact]
    public void MirrorToLegacyColumns_NullsOutSettersBeyondTheListLength()
    {
        string? task1 = "stale", task2 = "stale", task3 = "stale";

        RolePlayCardTasks.MirrorToLegacyColumns(
            ["only one"], s => task1 = s, s => task2 = s, s => task3 = s);

        Assert.Equal("only one", task1);
        Assert.Null(task2);
        Assert.Null(task3);
    }

    [Fact]
    public void Effective_PrefersTheJsonListOverTheLegacyColumns()
    {
        // A card authored after the migration carries more than five bullets;
        // the legacy columns only ever mirror the first five, so reading them
        // in preference would silently drop real content.
        var nine = Enumerable.Range(1, 9).Select(i => $"bullet {i}").ToArray();

        var effective = RolePlayCardTasks.Effective(
            RolePlayCardTasks.Serialize(nine),
            "bullet 1", "bullet 2", "bullet 3", "bullet 4", "bullet 5");

        Assert.Equal(nine, effective);
    }

    [Fact]
    public void Effective_FallsBackToLegacyColumnsForPreMigrationRows()
    {
        var effective = RolePlayCardTasks.Effective("[]", "one", null, "  three  ", null, null);

        Assert.Equal(["one", "three"], effective);
    }
}
