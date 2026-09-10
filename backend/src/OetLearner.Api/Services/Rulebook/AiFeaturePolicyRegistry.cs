using System.Reflection;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// W2 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// versioned feature policy registry. Every AI call must resolve to exactly
/// one active policy before a provider is ever selected (see
/// <c>AiGatewayService.CompleteAsync</c>'s feature-policy gate and
/// <c>Services/Ai/AiExecutionCoordinator.cs</c>).
///
/// <para>
/// Resolution order mirrors the existing <see cref="IAiFeatureRouteResolver"/>
/// pattern: a DB-backed <see cref="AiFeaturePolicy"/> row (highest active,
/// currently-effective <see cref="AiFeaturePolicy.PolicyVersion"/>) wins;
/// absent that, a static, code-defined default from
/// <see cref="AiFeaturePolicyDefaults"/> — built by reflecting over every
/// constant in <see cref="AiFeatureCodes"/> plus
/// <see cref="SpeakingAiFeatureCodes.All"/>, so newly added feature codes are
/// covered automatically without a second hand-maintained list drifting out
/// of sync. <c>AiFeatureCodes.Unclassified</c> is deliberately never
/// registered — it is the "no feature code supplied" marker, not a feature.
/// </para>
/// </summary>
public interface IAiFeaturePolicyRegistry
{
    /// <summary>
    /// Looks up the governing policy for <paramref name="featureCode"/> and
    /// reports WHY it is (or is not) usable. Never returns null: an
    /// unresolvable code comes back as <see cref="AiFeaturePolicyStatus.Unknown"/>
    /// so callers can distinguish "nobody ever registered this" from
    /// "an admin explicitly switched it off", which a nullable resolution
    /// could not express.
    /// </summary>
    Task<AiFeaturePolicyLookup> LookupAsync(string? featureCode, CancellationToken ct);
}

/// <summary>
/// Why a feature-policy lookup resolved the way it did. The distinction
/// matters because an EXPLICIT row that is inactive/not-yet-effective/expired
/// must suppress the static code default (an admin turning a feature off must
/// not be silently overridden by a compiled-in fallback), whereas a feature
/// with no row at all may legitimately fall back to the exhaustive static
/// default set.
/// </summary>
public enum AiFeaturePolicyStatus
{
    /// <summary>No DB row exists for this feature; the exhaustive static
    /// default from <see cref="AiFeaturePolicyDefaults"/> governs it.</summary>
    StaticDefault = 0,

    /// <summary>A DB row is active AND currently effective.</summary>
    DbActive = 1,

    /// <summary>DB row(s) exist for this feature but the governing one has
    /// <see cref="AiFeaturePolicy.IsActive"/> = false. Fails closed.</summary>
    DbDisabled = 2,

    /// <summary>DB row(s) exist but none is effective yet
    /// (<see cref="AiFeaturePolicy.EffectiveFrom"/> is in the future).</summary>
    DbNotYetEffective = 3,

    /// <summary>DB row(s) exist but all have expired
    /// (<see cref="AiFeaturePolicy.EffectiveTo"/> has passed).</summary>
    DbExpired = 4,

    /// <summary>Blank/whitespace feature code, <see cref="AiFeatureCodes.Unclassified"/>,
    /// or a code with neither a DB row nor a static default.</summary>
    Unknown = 5,
}

/// <summary>Outcome of <see cref="IAiFeaturePolicyRegistry.LookupAsync"/>.</summary>
public sealed record AiFeaturePolicyLookup(
    string FeatureCode,
    AiFeaturePolicyStatus Status,
    AiFeaturePolicyResolution? Policy)
{
    /// <summary>True only when a provider call may proceed on policy grounds.</summary>
    public bool IsUsable => Status is AiFeaturePolicyStatus.DbActive or AiFeaturePolicyStatus.StaticDefault;

    /// <summary>Short, sanitized machine reason for a refusal record. Never
    /// carries provider or prompt content.</summary>
    public string Reason => Status switch
    {
        AiFeaturePolicyStatus.DbActive => "db_active",
        AiFeaturePolicyStatus.StaticDefault => "static_default",
        AiFeaturePolicyStatus.DbDisabled => "policy_disabled",
        AiFeaturePolicyStatus.DbNotYetEffective => "policy_not_yet_effective",
        AiFeaturePolicyStatus.DbExpired => "policy_expired",
        _ => "policy_unknown",
    };

    public static AiFeaturePolicyLookup Unknown(string? featureCode)
        => new(string.IsNullOrWhiteSpace(featureCode) ? AiFeatureCodes.Unclassified : featureCode!,
            AiFeaturePolicyStatus.Unknown, null);
}

/// <summary>Resolved policy for one feature code. Immutable snapshot — never
/// mutated by callers.</summary>
public sealed record AiFeaturePolicyResolution(
    string FeatureCode,
    string Module,
    AiOperationClass OperationClass,
    bool RequiresGrounding,
    int PolicyVersion,
    IReadOnlyList<string> CacheDimensions);

/// <summary>Thrown by the gateway/coordinator when a Production call cannot
/// resolve an active feature policy. Development/Test keep the pre-W2
/// Unclassified fallback — see the call sites' <c>IHostEnvironment</c> guard.</summary>
public sealed class AiFeaturePolicyRefusedException : InvalidOperationException
{
    public AiFeaturePolicyRefusedException(string featureCode, string reason)
        : base($"No active AI feature policy is registered for '{featureCode}' ({reason}).")
    {
        FeatureCode = featureCode;
        Reason = reason;
    }

    public string FeatureCode { get; }
    public string Reason { get; }
}

/// <summary>Immutable code-defined fallback used when no DB
/// <see cref="AiFeaturePolicy"/> row exists for a feature code.</summary>
public sealed record AiFeaturePolicyStaticDefault(
    string FeatureCode,
    string Module,
    AiOperationClass OperationClass,
    bool RequiresGrounding);

/// <summary>
/// Builds the complete, reflection-derived static default policy set. See
/// the class doc comment on <see cref="IAiFeaturePolicyRegistry"/> for why
/// this must stay exhaustive rather than a hand-picked subset.
/// </summary>
public static class AiFeaturePolicyDefaults
{
    public static readonly string[] DefaultCacheDimensions =
    {
        "feature", "module", "userId", "resourceId", "resourceVersion",
        "requestHash", "promptVersion", "rulebookVersion", "modelRoute",
    };

    /// <summary>Feature codes that are scoring-critical but do not contain
    /// "grade"/"score" in their code — see <c>docs/AI-USAGE-POLICY.md</c> §5.</summary>
    private static readonly HashSet<string> ScoringOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        AiFeatureCodes.ConversationEvaluation,
        // Owner directive 2026-08-28 AI/Cloud API plan, point 7: appeals are
        // "quality-sensitive assessment" and must be treated with the same
        // rigour as official grading (platform-only credential, premium
        // model) even though "appeal" doesn't match the grade/score heuristic
        // below. See 20261104090000_AddAiModelPricingAndFeaturePolicy.cs,
        // which mirrors this override in its seed data.
        AiFeatureCodes.WritingAppealV1,
    };

    /// <summary>Feature codes whose operation class does not follow the
    /// generic prefix/keyword heuristic below.</summary>
    private static readonly Dictionary<string, AiOperationClass> ClassOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        // Admin AI-draft tool for role-play cards — an authoring tool despite
        // not carrying the "admin." prefix.
        [SpeakingAiFeatureCodes.CardDraftV1] = AiOperationClass.AdminBatch,
        // One-time-per-task Writing Model Answer pregeneration (admin-only,
        // never per-candidate — see WritingTaskModelAnswerService). Carries
        // the "writing." prefix so it fell through to the InteractiveLearning
        // default (meant for cheap learner-facing chat/coach calls) and hit
        // that class's $1/day cap after a single generation. This is content
        // authoring, not learner interaction.
        [AiFeatureCodes.WritingModelAnswerPregenerate] = AiOperationClass.AdminBatch,
        // Same failure shape as the override above, found diagnosing the
        // admin AI Assistant returning "temporarily unavailable": the
        // "ai_assistant." prefix isn't admin./class./tutor., so this fell
        // through to the InteractiveLearning default and shared the $1/day
        // pool with every learner AI Companion chat — exhausted by learner
        // traffic long before the admin sent a single message. AiBudgetClasses
        // .IsAdminBatchFeature already treats this feature as admin-batch;
        // this override brings the policy default in line with that intent.
        // See also 20261228090000_FixAiAssistantAdminBudgetClass.cs, which
        // corrects the already-seeded DB policy row the same way.
        [AiFeatureCodes.AiAssistantAdmin] = AiOperationClass.AdminBatch,
    };

    private static readonly Dictionary<string, string> ModuleOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        [SpeakingAiFeatureCodes.SpeakingScoreV2] = "speaking",
        [SpeakingAiFeatureCodes.SpeakingPatientTurnV1] = "speaking",
        [SpeakingAiFeatureCodes.CardDraftV1] = "speaking",
    };

    /// <summary>Direct (non-gateway) call prefixes that never carry a
    /// <c>RulebookPromptBuilder</c> grounded system prompt — see
    /// <c>docs/AI-USAGE-POLICY.md</c> §5 "Direct (non-gateway) AI calls".</summary>
    private static readonly string[] UngroundedPrefixes = { "ocr.", "stt.", "listening.parta.", "listening.partbc." };

    public static IReadOnlyDictionary<string, AiFeaturePolicyStaticDefault> All { get; } = Build();

    private static Dictionary<string, AiFeaturePolicyStaticDefault> Build()
    {
        var codes = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var field in typeof(AiFeatureCodes).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
        {
            if (field.FieldType != typeof(string)) continue;
            var value = (string?)field.GetValue(null);
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (string.Equals(value, AiFeatureCodes.Unclassified, StringComparison.Ordinal)) continue;
            codes.Add(value);
        }

        foreach (var code in SpeakingAiFeatureCodes.All)
        {
            codes.Add(code);
        }

        var result = new Dictionary<string, AiFeaturePolicyStaticDefault>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in codes)
        {
            result[code] = new AiFeaturePolicyStaticDefault(
                FeatureCode: code,
                Module: ResolveModule(code),
                OperationClass: ResolveOperationClass(code),
                RequiresGrounding: !UngroundedPrefixes.Any(prefix => code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
        }

        return result;
    }

    private static string ResolveModule(string code)
        => ModuleOverrides.TryGetValue(code, out var module) ? module : code.Split('.', 2)[0];

    private static AiOperationClass ResolveOperationClass(string code)
    {
        if (ClassOverrides.TryGetValue(code, out var overriddenClass)) return overriddenClass;
        if (ScoringOverrides.Contains(code)) return AiOperationClass.ScoringCritical;
        if (code.StartsWith("admin.", StringComparison.OrdinalIgnoreCase)
            || code.StartsWith("class.", StringComparison.OrdinalIgnoreCase)
            || code.StartsWith("tutor.", StringComparison.OrdinalIgnoreCase))
        {
            return AiOperationClass.AdminBatch;
        }

        if (code.Contains("grade", StringComparison.OrdinalIgnoreCase)
            || code.Contains("score", StringComparison.OrdinalIgnoreCase))
        {
            return AiOperationClass.ScoringCritical;
        }

        return AiOperationClass.InteractiveLearning;
    }
}

public sealed class AiFeaturePolicyRegistry(LearnerDbContext db) : IAiFeaturePolicyRegistry
{
    public async Task<AiFeaturePolicyLookup> LookupAsync(string? featureCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(featureCode)
            || string.Equals(featureCode, AiFeatureCodes.Unclassified, StringComparison.OrdinalIgnoreCase))
        {
            return AiFeaturePolicyLookup.Unknown(featureCode);
        }

        var code = featureCode!;
        var now = DateTimeOffset.UtcNow;

        // Every version row for this feature, newest first. A feature has at
        // most a handful of versions, so this is materially the same cost as
        // the previous single-row query but lets us tell "explicitly disabled"
        // apart from "never registered" — the whole point of the lookup.
        var rows = await db.AiFeaturePolicies
            .AsNoTracking()
            .Where(p => p.FeatureCode == code)
            .OrderByDescending(p => p.PolicyVersion)
            .ToListAsync(ct);

        var active = rows.FirstOrDefault(p => p.IsActive
            && p.EffectiveFrom <= now
            && (p.EffectiveTo == null || p.EffectiveTo > now));

        if (active is not null)
        {
            return new AiFeaturePolicyLookup(code, AiFeaturePolicyStatus.DbActive, new AiFeaturePolicyResolution(
                active.FeatureCode,
                active.Module,
                active.OperationClass,
                active.RequiresGrounding,
                active.PolicyVersion,
                ParseCacheDimensions(active.CacheDimensions)));
        }

        if (rows.Count > 0)
        {
            // An explicit row exists but does not govern right now. This
            // deliberately suppresses the static default: an admin who
            // deactivated or date-bounded a feature must not be silently
            // overridden by a compiled-in fallback.
            var governing = rows[0];
            var status = !governing.IsActive
                ? AiFeaturePolicyStatus.DbDisabled
                : governing.EffectiveFrom > now
                    ? AiFeaturePolicyStatus.DbNotYetEffective
                    : AiFeaturePolicyStatus.DbExpired;
            return new AiFeaturePolicyLookup(code, status, null);
        }

        if (AiFeaturePolicyDefaults.All.TryGetValue(code, out var fallback))
        {
            return new AiFeaturePolicyLookup(code, AiFeaturePolicyStatus.StaticDefault, new AiFeaturePolicyResolution(
                fallback.FeatureCode,
                fallback.Module,
                fallback.OperationClass,
                fallback.RequiresGrounding,
                PolicyVersion: 1,
                AiFeaturePolicyDefaults.DefaultCacheDimensions));
        }

        return AiFeaturePolicyLookup.Unknown(code);
    }

    private static IReadOnlyList<string> ParseCacheDimensions(string? csv)
        => string.IsNullOrWhiteSpace(csv)
            ? AiFeaturePolicyDefaults.DefaultCacheDimensions
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
