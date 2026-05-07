namespace OetLearner.Api.Services;

/// <summary>
/// Deterministic per-attempt shuffle helper used by mock graders / loaders
/// that wish to honour <c>MockAttempt.RandomisationSeed</c>.
///
/// MISSION CRITICAL invariants (see Mocks Module Plan §16, Reading §8):
/// <list type="bullet">
///   <item>Canonical question order (Part A → B → C, case-notes order, audio
///         cue order) MUST NOT be shuffled. This helper is intended for
///         option-order shuffling within a single multi-choice item only,
///         and only when the rulebook permits.</item>
///   <item>Same <paramref name="seed"/> + same <paramref name="saltKey"/>
///         MUST always produce the same permutation, so re-renders during
///         an attempt show a stable order.</item>
///   <item>Different <paramref name="saltKey"/> values (e.g. derived from
///         the question id) SHOULD produce different permutations even
///         when the attempt seed is the same, so two MCQs in one attempt
///         do not share a shuffle pattern.</item>
/// </list>
/// </summary>
public static class RandomisationHelper
{
    /// <summary>
    /// Returns a deterministic Fisher–Yates shuffle of <paramref name="items"/>.
    /// The original list is not mutated. Empty or single-element inputs are
    /// returned unchanged (already a no-op permutation).
    /// </summary>
    /// <param name="items">Source list — never mutated.</param>
    /// <param name="seed">Per-attempt seed (typically <c>MockAttempt.RandomisationSeed</c>).</param>
    /// <param name="saltKey">
    /// Per-item salt (typically a stable hash of the question id) so distinct
    /// items in the same attempt get distinct shuffles.
    /// </param>
    public static IReadOnlyList<T> SeededShuffle<T>(IReadOnlyList<T> items, uint seed, int saltKey)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count <= 1) return items;

        // XOR seed with salt then squash to int. unchecked cast preserves the
        // bit pattern so callers always observe the same Random sequence for
        // the same (seed, salt) pair regardless of platform.
        var rngSeed = unchecked((int)(seed ^ (uint)saltKey));
        var rng = new Random(rngSeed);

        var copy = new T[items.Count];
        for (var i = 0; i < items.Count; i++) copy[i] = items[i];

        // In-place Fisher–Yates on the copy.
        for (var i = copy.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }

        return copy;
    }

    /// <summary>
    /// Convenience helper for callers that have a string id (e.g. question id)
    /// rather than a precomputed integer salt. Uses an ordinal hash so the
    /// salt is stable across runs and machines (unlike <see cref="string.GetHashCode()"/>
    /// which is randomised per process in .NET).
    /// </summary>
    public static int SaltKeyFromString(string? id)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        // FNV-1a 32-bit — small, deterministic, no allocations.
        const uint offset = 2166136261u;
        const uint prime = 16777619u;
        var hash = offset;
        foreach (var c in id)
        {
            hash ^= c;
            hash *= prime;
        }
        return unchecked((int)hash);
    }
}
