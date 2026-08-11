using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Assessment;

public sealed record AssessmentScoreTableRowInput(int RawScore, int ConvertedScore, string? Grade, bool? Passed = null);

public sealed record AssessmentScoreTableValidationResult(bool IsValid, string? ErrorCode)
{
    public static AssessmentScoreTableValidationResult Valid { get; } = new(true, null);
}

public static class AssessmentScoreTableValidator
{
    public const int RawMinimum = 0;
    public const int RawMaximum = 42;
    public const int ConvertedMinimum = 0;
    public const int ConvertedMaximum = 500;

    public static AssessmentScoreTableValidationResult Validate(
        string assessment,
        IReadOnlyCollection<AssessmentScoreTableRowInput> rows)
    {
        if (!IsSupportedAssessment(assessment))
            return new(false, "assessment_unsupported");

        if (rows.Count != RawMaximum + 1)
            return new(false, "score_table_requires_43_rows");

        if (rows.Select(row => row.RawScore).Distinct().Count() != rows.Count)
            return new(false, "score_table_duplicate_raw_score");

        var expectedRawScores = Enumerable.Range(RawMinimum, RawMaximum + 1);
        if (!expectedRawScores.SequenceEqual(rows.Select(row => row.RawScore).OrderBy(raw => raw)))
            return new(false, "score_table_raw_scores_must_cover_0_to_42");

        if (rows.Any(row => row.ConvertedScore is < ConvertedMinimum or > ConvertedMaximum))
            return new(false, "score_table_converted_score_out_of_range");

        return AssessmentScoreTableValidationResult.Valid;
    }

    public static bool IsSupportedAssessment(string? assessment) =>
        string.Equals(assessment, "listening", StringComparison.OrdinalIgnoreCase)
        || string.Equals(assessment, "reading", StringComparison.OrdinalIgnoreCase);

    public static string NormalizeAssessment(string assessment) =>
        assessment.Trim().ToLowerInvariant();

    public static string NormalizeScope(string? scope) =>
        string.IsNullOrWhiteSpace(scope) ? "default" : scope.Trim().ToLowerInvariant();
}

public sealed record AssessmentScoreConversionResult(
    string Assessment,
    string ScopeKey,
    int RawScore,
    int? ConvertedScore,
    string? TableId,
    string? TableVersionKey,
    string? ErrorCode,
    string? Grade,
    bool? Passed)
{
    public bool IsAvailable => ConvertedScore.HasValue;

    public static AssessmentScoreConversionResult Unavailable(
        string assessment,
        string scopeKey,
        int rawScore,
        string errorCode,
        string? tableId = null,
        string? tableVersionKey = null) => new(
            assessment,
            scopeKey,
            rawScore,
            null,
            tableId,
            tableVersionKey,
            errorCode,
            null,
            null);
}

public interface IAssessmentScoreConversionService
{
    Task<AssessmentScoreConversionResult> ResolveAsync(
        string assessment,
        int rawScore,
        string? scopeKey = null,
        string? tableId = null,
        CancellationToken cancellationToken = default);

    Task MarkUsedAsync(string tableId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Immutable score-conversion selection captured when a candidate attempt is
/// created. The table rows remain in the governed table, which is locked after
/// first use; this record prevents a later effective table from being selected
/// for an attempt that started before the table was available or changed.
/// </summary>
public sealed record AssessmentScoreConversionSnapshot(
    string Assessment,
    string ScopeKey,
    string? TableId,
    string? TableVersionKey,
    string? ErrorCode)
{
    public static AssessmentScoreConversionSnapshot Capture(
        AssessmentScoreConversionResult resolution) => new(
            resolution.Assessment,
            resolution.ScopeKey,
            resolution.TableId,
            resolution.TableVersionKey,
            resolution.ErrorCode ?? (resolution.TableId is null ? "score_table_not_configured" : null));

    public static AssessmentScoreConversionSnapshot Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("assessment_score_conversion_snapshot_missing");

        try
        {
            var snapshot = JsonSerializer.Deserialize<AssessmentScoreConversionSnapshot>(json)
                ?? throw new InvalidOperationException("assessment_score_conversion_snapshot_invalid");
            if (!AssessmentScoreTableValidator.IsSupportedAssessment(snapshot.Assessment)
                || string.IsNullOrWhiteSpace(snapshot.ScopeKey)
                || (string.IsNullOrWhiteSpace(snapshot.TableId)
                    && string.IsNullOrWhiteSpace(snapshot.ErrorCode)))
            {
                throw new InvalidOperationException("assessment_score_conversion_snapshot_invalid");
            }

            return snapshot with
            {
                Assessment = AssessmentScoreTableValidator.NormalizeAssessment(snapshot.Assessment),
                ScopeKey = AssessmentScoreTableValidator.NormalizeScope(snapshot.ScopeKey),
                TableId = string.IsNullOrWhiteSpace(snapshot.TableId) ? null : snapshot.TableId.Trim(),
                TableVersionKey = string.IsNullOrWhiteSpace(snapshot.TableVersionKey) ? null : snapshot.TableVersionKey.Trim(),
                ErrorCode = string.IsNullOrWhiteSpace(snapshot.ErrorCode) ? null : snapshot.ErrorCode.Trim(),
            };
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("assessment_score_conversion_snapshot_invalid_json");
        }
    }

    public string Serialize() => JsonSerializer.Serialize(this);
}

/// <summary>Shared resolver that preserves the attempt-start table choice.
/// Rows created before this snapshot field existed retain the legacy behavior
/// of resolving the current table, while every governed attempt is fail-closed
/// when no table was available at its start.</summary>
public static class AssessmentScoreConversionSnapshotResolver
{
    public static async Task<AssessmentScoreConversionResult> ResolveAsync(
        IAssessmentScoreConversionService resolver,
        string assessment,
        int rawScore,
        string? snapshotJson,
        string? legacyTableId,
        string? scopeKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return await resolver.ResolveAsync(
                assessment,
                rawScore,
                scopeKey,
                legacyTableId,
                cancellationToken);
        }

        var snapshot = AssessmentScoreConversionSnapshot.Parse(snapshotJson);
        if (!string.Equals(snapshot.Assessment,
                AssessmentScoreTableValidator.NormalizeAssessment(assessment),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("assessment_score_conversion_snapshot_assessment_mismatch");
        }

        if (snapshot.TableId is null)
        {
            return AssessmentScoreConversionResult.Unavailable(
                snapshot.Assessment,
                snapshot.ScopeKey,
                rawScore,
                snapshot.ErrorCode ?? "score_table_not_configured",
                tableVersionKey: snapshot.TableVersionKey);
        }

        return await resolver.ResolveAsync(
            snapshot.Assessment,
            rawScore,
            snapshot.ScopeKey,
            snapshot.TableId,
            cancellationToken);
    }
}

/// <summary>
/// Resolves only complete, owner-effective lookup tables. There is intentionally
/// no interpolation or formula fallback in this service.
/// </summary>
public sealed class AssessmentScoreConversionService(LearnerDbContext db)
    : IAssessmentScoreConversionService
{
    public async Task<AssessmentScoreConversionResult> ResolveAsync(
        string assessment,
        int rawScore,
        string? scopeKey = null,
        string? tableId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedAssessment = AssessmentScoreTableValidator.NormalizeAssessment(assessment);
        var normalizedScope = AssessmentScoreTableValidator.NormalizeScope(scopeKey);

        if (!AssessmentScoreTableValidator.IsSupportedAssessment(normalizedAssessment))
            return AssessmentScoreConversionResult.Unavailable(
                normalizedAssessment, normalizedScope, rawScore, "assessment_unsupported");

        if (rawScore is < AssessmentScoreTableValidator.RawMinimum
            or > AssessmentScoreTableValidator.RawMaximum)
        {
            return AssessmentScoreConversionResult.Unavailable(
                normalizedAssessment, normalizedScope, rawScore, "raw_score_out_of_range");
        }

        var now = DateTimeOffset.UtcNow;
        var query = db.AssessmentScoreConversionTables
            .AsNoTracking()
            .Include(table => table.Rows)
            .Where(table => table.Assessment == normalizedAssessment && table.ScopeKey == normalizedScope);

        AssessmentScoreConversionTable? table;
        if (!string.IsNullOrWhiteSpace(tableId))
        {
            table = await query.SingleOrDefaultAsync(x => x.Id == tableId, cancellationToken);
            if (table is null)
                return AssessmentScoreConversionResult.Unavailable(
                    normalizedAssessment, normalizedScope, rawScore, "score_table_not_found");

            if (table.Status is not (AssessmentGovernanceStatus.Effective or AssessmentGovernanceStatus.Locked))
                return AssessmentScoreConversionResult.Unavailable(
                    normalizedAssessment, normalizedScope, rawScore, "score_table_not_effective",
                    table.Id, table.VersionKey);
        }
        else
        {
            var candidates = await query
                .Where(x => x.EffectiveFrom <= now
                    && (x.Status == AssessmentGovernanceStatus.Effective
                        || x.Status == AssessmentGovernanceStatus.Locked))
                .OrderByDescending(x => x.EffectiveFrom)
                .ThenByDescending(x => x.VersionKey)
                .Take(2)
                .ToListAsync(cancellationToken);

            if (candidates.Count == 0)
                return AssessmentScoreConversionResult.Unavailable(
                    normalizedAssessment, normalizedScope, rawScore, "score_table_not_configured");

            if (candidates.Count > 1 && candidates[0].EffectiveFrom == candidates[1].EffectiveFrom)
                return AssessmentScoreConversionResult.Unavailable(
                    normalizedAssessment, normalizedScope, rawScore, "score_table_multiple_effective_versions");

            table = candidates[0];
        }

        var validation = AssessmentScoreTableValidator.Validate(
            table.Assessment,
            table.Rows
                .Select(row => new AssessmentScoreTableRowInput(row.RawScore, row.ConvertedScore, row.Grade, row.Passed))
                .ToArray());
        if (!validation.IsValid)
            return AssessmentScoreConversionResult.Unavailable(
                normalizedAssessment,
                normalizedScope,
                rawScore,
                validation.ErrorCode ?? "score_table_invalid",
                table.Id,
                table.VersionKey);

        var row = table.Rows.Single(row => row.RawScore == rawScore);
        return new AssessmentScoreConversionResult(
            normalizedAssessment,
            normalizedScope,
            rawScore,
            row.ConvertedScore,
            table.Id,
            table.VersionKey,
            null,
            string.IsNullOrWhiteSpace(row.Grade) ? null : row.Grade.Trim(),
            row.Passed);
    }

    public async Task MarkUsedAsync(string tableId, CancellationToken cancellationToken = default)
    {
        var table = await db.AssessmentScoreConversionTables
            .SingleOrDefaultAsync(x => x.Id == tableId, cancellationToken);
        if (table is null)
            throw new InvalidOperationException("score_table_not_found");

        table.HasBeenUsed = true;
        if (table.Status == AssessmentGovernanceStatus.Effective)
        {
            table.Status = AssessmentGovernanceStatus.Locked;
            table.LockedAt ??= DateTimeOffset.UtcNow;
        }

        table.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Candidate-attempt snapshot for deterministic marking and delivery controls.
/// Defaults are conservative; an owner must explicitly publish a different
/// profile through the governed admin workflow.
/// </summary>
public sealed record AssessmentMarkingPolicyResolution(
    string Assessment,
    string ScopeKey,
    string? PolicyId,
    string? PolicyVersionKey,
    AssessmentMarkingPolicyDocument Document,
    string? ErrorCode)
{
    public bool IsAvailable => PolicyId is not null;

    public static AssessmentMarkingPolicyResolution Unavailable(
        string assessment,
        string scopeKey,
        string errorCode) => new(
            assessment,
            scopeKey,
            null,
            null,
            new AssessmentMarkingPolicyDocument(),
            errorCode);
}

public interface IAssessmentMarkingPolicyService
{
    Task<AssessmentMarkingPolicyResolution> ResolveAsync(
        string assessment,
        string? scopeKey = null,
        string? policyId = null,
        CancellationToken cancellationToken = default);

    Task MarkUsedAsync(string policyId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves only owner-effective marking policies. A missing policy is
/// surfaced as unavailable; the conservative document is never presented as
/// an owner-approved version.
/// </summary>
public sealed class AssessmentMarkingPolicyService(LearnerDbContext db)
    : IAssessmentMarkingPolicyService
{
    public async Task<AssessmentMarkingPolicyResolution> ResolveAsync(
        string assessment,
        string? scopeKey = null,
        string? policyId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedAssessment = AssessmentScoreTableValidator.NormalizeAssessment(assessment);
        var normalizedScope = AssessmentScoreTableValidator.NormalizeScope(scopeKey);
        if (!AssessmentScoreTableValidator.IsSupportedAssessment(normalizedAssessment))
            return AssessmentMarkingPolicyResolution.Unavailable(
                normalizedAssessment, normalizedScope, "assessment_unsupported");

        var query = db.AssessmentMarkingPolicyVersions
            .AsNoTracking()
            .Where(policy => policy.Assessment == normalizedAssessment
                && policy.ScopeKey == normalizedScope);
        AssessmentMarkingPolicyVersion? policy;
        if (!string.IsNullOrWhiteSpace(policyId))
        {
            policy = await query.SingleOrDefaultAsync(x => x.Id == policyId, cancellationToken);
            if (policy is null)
                return AssessmentMarkingPolicyResolution.Unavailable(
                    normalizedAssessment, normalizedScope, "marking_policy_not_found");
            if (policy.Status is not (AssessmentGovernanceStatus.Effective or AssessmentGovernanceStatus.Locked))
                return new AssessmentMarkingPolicyResolution(
                    normalizedAssessment, normalizedScope, policy.Id, policy.VersionKey,
                    new AssessmentMarkingPolicyDocument(), "marking_policy_not_effective");
        }
        else
        {
            var now = DateTimeOffset.UtcNow;
            var candidates = await query
                .Where(x => x.EffectiveFrom <= now
                    && (x.Status == AssessmentGovernanceStatus.Effective
                        || x.Status == AssessmentGovernanceStatus.Locked))
                .OrderByDescending(x => x.EffectiveFrom)
                .ThenByDescending(x => x.VersionKey)
                .Take(2)
                .ToListAsync(cancellationToken);
            if (candidates.Count == 0)
                return AssessmentMarkingPolicyResolution.Unavailable(
                    normalizedAssessment, normalizedScope, "marking_policy_not_configured");
            if (candidates.Count > 1 && candidates[0].EffectiveFrom == candidates[1].EffectiveFrom)
                return AssessmentMarkingPolicyResolution.Unavailable(
                    normalizedAssessment, normalizedScope, "marking_policy_multiple_effective_versions");
            policy = candidates[0];
        }

        try
        {
            return new AssessmentMarkingPolicyResolution(
                normalizedAssessment,
                normalizedScope,
                policy.Id,
                policy.VersionKey,
                AssessmentMarkingPolicyDocument.Parse(policy.PolicyJson),
                null);
        }
        catch (InvalidOperationException)
        {
            return AssessmentMarkingPolicyResolution.Unavailable(
                normalizedAssessment, normalizedScope, "marking_policy_invalid_json");
        }
    }

    public async Task MarkUsedAsync(string policyId, CancellationToken cancellationToken = default)
    {
        var policy = await db.AssessmentMarkingPolicyVersions
            .SingleOrDefaultAsync(x => x.Id == policyId, cancellationToken)
            ?? throw new InvalidOperationException("marking_policy_not_found");
        policy.HasBeenUsed = true;
        if (policy.Status == AssessmentGovernanceStatus.Effective)
            policy.Status = AssessmentGovernanceStatus.Locked;
        policy.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
public sealed record AssessmentMarkingPolicyDocument(
    bool TrimLeadingTrailingWhitespace = true,
    bool CollapseInternalWhitespace = false,
    bool CaseSensitive = true,
    bool ReadingPartAMatchingPartialCredit = false,
    bool ListeningAudioReplayAllowed = false,
    string AudioLockMode = "exam",
    bool TechnicalRequirementsGuidanceOnly = true)
{
    public static AssessmentMarkingPolicyDocument Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new AssessmentMarkingPolicyDocument();

        try
        {
            return JsonSerializer.Deserialize<AssessmentMarkingPolicyDocument>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new AssessmentMarkingPolicyDocument();
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("assessment_marking_policy_invalid_json");
        }
    }

    public string Serialize() => JsonSerializer.Serialize(this);
}
