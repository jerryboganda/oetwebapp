using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// W2 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// deterministic canonical-action key for <see cref="AiExecutionCoordinator"/>.
///
/// <para>
/// SHA-256 over exactly nine pipe-separated dimensions, in this fixed order:
/// <c>feature|module|userId|resourceId|resourceVersion|requestHash|promptVersion|rulebookVersion|modelRoute</c>.
/// Every dimension is normalised (trimmed, lower-invariant) before hashing so
/// two logically-identical requests that differ only in casing or
/// leading/trailing whitespace collapse to the same key; a null/blank
/// dimension normalises to the empty string, not to a literal <c>"null"</c>,
/// so "field omitted" and "field explicitly empty" are indistinguishable by
/// design — the caller decides which dimensions matter for a given feature.
/// </para>
///
/// <para>
/// This key becomes <see cref="OetLearner.Api.Domain.AiOperation.IdempotencyKey"/>,
/// which is unique-indexed at the database
/// (<c>UX_AiOperations_IdempotencyKey</c>). Retrying the exact same logical
/// action (same nine dimensions) always resolves to the same operation row;
/// changing any one dimension — including <c>requestHash</c>, so a payload
/// edit under the same resource/version is never silently conflated with the
/// original — produces a different key and therefore a different operation.
/// </para>
/// </summary>
public static class AiIdempotencyKeyBuilder
{
    public static string Build(
        string feature,
        string module,
        string? userId,
        string? resourceId,
        int? resourceVersion,
        string? requestHash,
        string? promptVersion,
        string? rulebookVersion,
        string? modelRoute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        ArgumentException.ThrowIfNullOrWhiteSpace(module);

        var canonical = string.Join(
            '|',
            Normalize(feature),
            Normalize(module),
            Normalize(userId),
            Normalize(resourceId),
            resourceVersion?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Normalize(requestHash),
            Normalize(promptVersion),
            Normalize(rulebookVersion),
            Normalize(modelRoute));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Exposed for tests that need to assert on the exact canonical
    /// string a given dimension set hashes to, independent of the digest
    /// algorithm. Not used by any runtime call site.</summary>
    internal static string BuildCanonicalString(
        string feature,
        string module,
        string? userId,
        string? resourceId,
        int? resourceVersion,
        string? requestHash,
        string? promptVersion,
        string? rulebookVersion,
        string? modelRoute)
        => string.Join(
            '|',
            Normalize(feature),
            Normalize(module),
            Normalize(userId),
            Normalize(resourceId),
            resourceVersion?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Normalize(requestHash),
            Normalize(promptVersion),
            Normalize(rulebookVersion),
            Normalize(modelRoute));

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}
