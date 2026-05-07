using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public class RandomisationHelperTests
{
    [Fact]
    public void SeededShuffle_SameSeedAndSalt_IsDeterministic()
    {
        var items = new[] { "A", "B", "C", "D", "E", "F" };
        var first = RandomisationHelper.SeededShuffle(items, seed: 12345u, saltKey: 99);
        var second = RandomisationHelper.SeededShuffle(items, seed: 12345u, saltKey: 99);

        Assert.Equal(first, second);
        // Should also be a permutation (no loss / no duplication).
        Assert.Equal(items.OrderBy(x => x), first.OrderBy(x => x));
    }

    [Fact]
    public void SeededShuffle_DifferentSaltsWithSameSeed_ProduceDifferentOrders()
    {
        // 6! = 720 permutations, so the chance of two different salts colliding
        // by accident across a handful of trials is negligible.
        var items = new[] { "A", "B", "C", "D", "E", "F" };
        var seed = 7777u;

        var distinct = new HashSet<string>();
        foreach (var salt in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            var permutation = RandomisationHelper.SeededShuffle(items, seed, salt);
            distinct.Add(string.Join(",", permutation));
        }

        // We expect the vast majority of 8 trials to yield unique permutations.
        Assert.True(distinct.Count >= 6,
            $"Expected ≥6 distinct permutations across 8 salts, got {distinct.Count}.");
    }

    [Fact]
    public void SeededShuffle_DifferentSeeds_ProduceDifferentOrders()
    {
        var items = new[] { "A", "B", "C", "D", "E", "F" };
        var distinct = new HashSet<string>();
        foreach (var seed in new uint[] { 1u, 2u, 3u, 100u, 9999u, 4242u, 31415u, 27182u })
        {
            var permutation = RandomisationHelper.SeededShuffle(items, seed, saltKey: 0);
            distinct.Add(string.Join(",", permutation));
        }

        Assert.True(distinct.Count >= 6,
            $"Expected ≥6 distinct permutations across 8 seeds, got {distinct.Count}.");
    }

    [Fact]
    public void SeededShuffle_EmptyList_ReturnedUnchanged()
    {
        var items = Array.Empty<string>();
        var result = RandomisationHelper.SeededShuffle(items, seed: 42u, saltKey: 0);
        Assert.Same(items, result);
    }

    [Fact]
    public void SeededShuffle_SingleElement_ReturnedUnchanged()
    {
        var items = new[] { "only" };
        var result = RandomisationHelper.SeededShuffle(items, seed: 42u, saltKey: 0);
        Assert.Same(items, result);
    }

    [Fact]
    public void SeededShuffle_DoesNotMutateSource()
    {
        var items = new[] { "A", "B", "C", "D", "E" };
        var snapshot = items.ToArray();
        _ = RandomisationHelper.SeededShuffle(items, seed: 12345u, saltKey: 7);
        Assert.Equal(snapshot, items);
    }

    [Fact]
    public void SaltKeyFromString_IsStableForSameInput()
    {
        Assert.Equal(
            RandomisationHelper.SaltKeyFromString("question-abc"),
            RandomisationHelper.SaltKeyFromString("question-abc"));
    }

    [Fact]
    public void SaltKeyFromString_DifferentInputsProduceDifferentSalts()
    {
        Assert.NotEqual(
            RandomisationHelper.SaltKeyFromString("question-abc"),
            RandomisationHelper.SaltKeyFromString("question-xyz"));
    }

    [Fact]
    public void SaltKeyFromString_NullOrEmpty_ReturnsZero()
    {
        Assert.Equal(0, RandomisationHelper.SaltKeyFromString(null));
        Assert.Equal(0, RandomisationHelper.SaltKeyFromString(string.Empty));
    }
}
