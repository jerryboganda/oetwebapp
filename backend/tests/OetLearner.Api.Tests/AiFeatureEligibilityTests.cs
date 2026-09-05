using System.Reflection;
using System.Text.RegularExpressions;
using OetLearner.Api.Domain;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// Locks the coupling that <c>AiEntities.cs</c> has always claimed:
/// "Adding a new code here without updating that matrix is a bug caught by
/// <c>AiFeatureEligibilityTests</c>." Until the AI Learning Companion program
/// (docs/ai-learning-companion/) this test did not actually exist, so the
/// matrix in <c>docs/AI-USAGE-POLICY.md</c> §5 had silently drifted.
///
/// This is a <b>ratchet</b>, not a big-bang cleanup: the 32 feature codes that
/// were already undocumented when the test was written are listed explicitly in
/// <see cref="KnownUndocumentedCodes"/>. They are pre-existing debt, visible and
/// countable. Any <b>new</b> code must be documented or the build fails, and the
/// allowlist may only ever shrink.
/// </summary>
public sealed class AiFeatureEligibilityTests
{
    /// <summary>
    /// Feature codes that predate this test and are not yet in the policy matrix.
    /// Do not add to this list. Removing entries (by documenting them) is the goal.
    /// </summary>
    private static readonly HashSet<string> KnownUndocumentedCodes = new(StringComparer.Ordinal)
    {
        "writing.model_answer_pregenerate",
        "writing.coach.v1",
        "writing.rewrite.v1",
        "writing.scenario.generate.v1",
        "writing.exemplar.embed.v1",
        "writing.appeal.v1",
        "writing.canon.detect.v1",
        "writing.drill.grade.v1",
        "writing.outline.v1",
        "writing.paraphrase.v1",
        "writing.ask.v1",
        "recalls.mistake_explain",
        "recalls.revision_plan",
        "mock.remediation_draft",
        "admin.writing_draft",
        "admin.listening.skill_tag",
        "admin.listening.transcript_segment",
        "ai_assistant.admin",
        "ai_assistant.expert",
        "ai_assistant.learner",
        "reading.vocabulary.card",
        "reading.passage_qna.v1",
        "class.recording.transcribe.v1",
        "class.recording.summarize.v1",
        "class.recording.translate.v1",
        "class.assistant.qna.v1",
        "tutor.recommendation.v1",
        "listening.parta.score",
        "ocr.listening.partbc",
        "listening.partbc.extract",
        "embeddings.generate",
        // Catch-all sentinel for pre-classification calls; intentionally not a matrix row.
        "unclassified",
    };

    /// <summary>
    /// The AI Learning Companion codes. These are non-scoring but platform-only:
    /// the prompt carries learner performance history, entitlement scope and credit
    /// state, so BYOK must never be able to see it. DR-001/DR-002 in
    /// docs/ai-learning-companion/REPO_GAP_ANALYSIS.md.
    /// </summary>
    private static readonly string[] CompanionFeatureCodes =
    [
        AiFeatureCodes.CompanionChat,
        AiFeatureCodes.CompanionRetrieval,
        AiFeatureCodes.CompanionAction,
    ];

    [Fact]
    public void EveryFeatureCode_IsDocumented_OrIsKnownPreExistingDebt()
    {
        var documented = DocumentedCodes();
        var undocumented = AllFeatureCodes()
            .Where(code => !documented.Contains(code) && !KnownUndocumentedCodes.Contains(code))
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            undocumented.Count == 0,
            "These AiFeatureCodes constants are missing from the feature-eligibility matrix in " +
            "docs/AI-USAGE-POLICY.md §5. Add a row (scoring-critical / BYOK / platform / notes) " +
            "for each before merging:\n  " + string.Join("\n  ", undocumented));
    }

    [Fact]
    public void KnownUndocumentedAllowlist_OnlyShrinks()
    {
        var codes = AllFeatureCodes().ToHashSet(StringComparer.Ordinal);
        var stale = KnownUndocumentedCodes
            .Where(code => !codes.Contains(code))
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            stale.Count == 0,
            "KnownUndocumentedCodes references feature codes that no longer exist. Remove them:\n  " +
            string.Join("\n  ", stale));

        var documented = DocumentedCodes();
        var nowDocumented = KnownUndocumentedCodes
            .Where(documented.Contains)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            nowDocumented.Count == 0,
            "These codes are now documented in docs/AI-USAGE-POLICY.md §5 — delete them from " +
            "KnownUndocumentedCodes so the ratchet tightens:\n  " + string.Join("\n  ", nowDocumented));
    }

    [Fact]
    public void CompanionFeatureCodes_AreDocumented()
    {
        var documented = DocumentedCodes();
        foreach (var code in CompanionFeatureCodes)
        {
            Assert.True(
                documented.Contains(code),
                $"Companion feature code '{code}' must have a row in docs/AI-USAGE-POLICY.md §5.");
            Assert.DoesNotContain(code, KnownUndocumentedCodes);
        }
    }

    [Fact]
    public void CompanionAndAdminFeatureCodes_ArePlatformOnly()
    {
        var platformOnly = PlatformOnlyFeatureCodes();

        foreach (var code in CompanionFeatureCodes)
        {
            Assert.True(
                platformOnly.Contains(code),
                $"Companion feature code '{code}' must be in AiCredentialResolver.PlatformOnlyFeatures. " +
                "The companion prompt carries learner performance history, entitlement scope and credit " +
                "state; a learner-supplied BYOK key must never receive it.");
        }

        var adminCodesOutsidePlatformOnly = AllFeatureCodes()
            .Where(code => code.StartsWith("admin.", StringComparison.Ordinal))
            .Where(code => !platformOnly.Contains(code))
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            adminCodesOutsidePlatformOnly.Count == 0,
            "Every admin.* feature code must be platform-only, otherwise BYOK could route an admin " +
            "extraction call to a learner-supplied key:\n  " +
            string.Join("\n  ", adminCodesOutsidePlatformOnly));
    }

    // ---------------------------------------------------------------- helpers

    private static IEnumerable<string> AllFeatureCodes() =>
        typeof(AiFeatureCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

    /// <summary>
    /// Reads <c>AiCredentialResolver.PlatformOnlyFeatures</c> via reflection so the
    /// test asserts against the real enforcement set rather than a copy that could
    /// drift away from it.
    /// </summary>
    private static HashSet<string> PlatformOnlyFeatureCodes()
    {
        var resolverType = typeof(AiFeatureCodes).Assembly
            .GetType("OetLearner.Api.Services.AiManagement.AiCredentialResolver");
        Assert.NotNull(resolverType);

        var field = resolverType!.GetField(
            "PlatformOnlyFeatures",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);

        var value = field!.GetValue(null);
        Assert.NotNull(value);

        return new HashSet<string>((HashSet<string>)value!, StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> DocumentedCodes()
    {
        var path = Path.Combine(FindRepoRoot(), "docs", "AI-USAGE-POLICY.md");
        Assert.True(File.Exists(path), $"Expected the AI usage policy at {path}.");

        var text = File.ReadAllText(path);
        return Regex
            .Matches(text, @"\|\s*`([a-z0-9_.]+)`\s*\|")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(current.FullName, "backend")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find repository root for the AI feature eligibility test.");
    }
}
