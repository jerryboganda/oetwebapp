using Microsoft.Extensions.Caching.Memory;

namespace OetLearner.Api.Services.Companion;

/// <summary>
/// A rolling, per-learner cap on how much of any one proprietary source the
/// companion will reproduce — across turns, not within one.
///
/// <para>
/// <see cref="CompanionRetriever"/> already caps verbatim characters and chunk
/// count <i>per turn</i>. That stops a single "print the rulebook" request but
/// not the attack the packs actually describe: asking for section 1, then the
/// next section, then "keep going". Chunks are one-per-rule, so walking the rule
/// list rebuilds the book a legitimate-looking question at a time. Golden case
/// GC-006 requires the refusal to trip by the third turn, and Testing Pack 4
/// scenario 22 runs the same attack explicitly.
/// </para>
///
/// <para>
/// The window is per learner and per source. Two different rulebooks each get
/// their own allowance, because the thing being protected is a document, not the
/// learner's overall curiosity. A learner who genuinely studies one rulebook
/// hard for an hour will hit the cap — that is intended, and the companion
/// should keep teaching from its own words rather than quoting further.
/// </para>
/// </summary>
public interface ICompanionExtractionBudget
{
    /// <summary>
    /// Characters of <paramref name="sourceId"/> this learner may still be shown
    /// verbatim. Zero means the source is exhausted for now.
    /// </summary>
    int RemainingChars(string userId, Guid sourceId);

    /// <summary>Records characters actually packed into the prompt.</summary>
    void Consume(string userId, Guid sourceId, int chars);
}

/// <inheritdoc />
public sealed class CompanionExtractionBudget(IMemoryCache cache) : ICompanionExtractionBudget
{
    /// <summary>
    /// Total verbatim characters of one proprietary source a learner may see
    /// within <see cref="Window"/>.
    ///
    /// <para>
    /// Set at four times the per-turn cap: enough that a normal study
    /// conversation about one rulebook never notices it, and far short of the
    /// tens of thousands of characters a full rulebook contains.
    /// </para>
    /// </summary>
    public const int WindowChars = 4 * 1200;

    /// <summary>
    /// Sliding window. Long enough to cover a study session, short enough that
    /// the cap is not a permanent lockout for a legitimate returning learner.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(6);

    // ponytail: per-instance IMemoryCache, so the window is per API instance.
    // Behind a load balancer a determined learner gets N windows for N
    // instances — still bounded, still far below reconstruction. Move to the
    // distributed cache if the deployment scales out and this matters.
    public int RemainingChars(string userId, Guid sourceId)
    {
        var used = cache.TryGetValue<int>(Key(userId, sourceId), out var value) ? value : 0;
        return Math.Max(0, WindowChars - used);
    }

    public void Consume(string userId, Guid sourceId, int chars)
    {
        if (chars <= 0) return;

        var key = Key(userId, sourceId);
        var used = cache.TryGetValue<int>(key, out var value) ? value : 0;

        // Absolute rather than sliding expiry: a sliding window would be
        // refreshed by the very requests it is meant to limit, so a steady
        // chapter-walk would never expire and never reset either.
        cache.Set(key, used + chars, Window);
    }

    private static string Key(string userId, Guid sourceId) =>
        $"companion:extraction:{userId}:{sourceId}";
}
