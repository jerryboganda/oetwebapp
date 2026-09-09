using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiAssistant.Indexing;

namespace OetLearner.Api.Services.Companion;

public interface ICompanionSpeakingCriteriaIndexer
{
    Task<CompanionIndexResult> IndexAsync(bool embed, CancellationToken ct);
}

/// <summary>
/// Publishes the approved Speaking Assessment Criteria and Intro Questions into
/// the companion corpus.
///
/// <para>
/// Testing Pack 1 scenario 24 asks for exactly this — "Show me the approved
/// Speaking Intro Questions and explain the Speaking Assessment Criteria used in
/// our course. If you cannot retrieve the approved set, do not invent one." Both
/// existed only as data behind two Next.js pages, so the companion had no way to
/// retrieve them and the honest answer was a refusal.
/// </para>
///
/// <para>
/// <b>One file, two readers.</b> The source is
/// <c>data/speaking-candidate-resources.json</c>, linked into this project by
/// the csproj and imported directly by
/// <c>lib/speaking-candidate-resources.ts</c>. Had this been a hand-written C#
/// copy, the failure mode would be the worst kind for an exam product: the
/// /speaking pages showing one set of descriptors while Sami confidently quotes
/// another, with nothing to say which is right.
/// </para>
/// </summary>
public sealed class CompanionSpeakingCriteriaIndexer(
    LearnerDbContext db,
    IEmbeddingService embeddings,
    ILogger<CompanionSpeakingCriteriaIndexer> logger) : ICompanionSpeakingCriteriaIndexer
{
    internal const string SourceKey = "speaking:assessment-criteria-and-intro";

    /// <summary>
    /// Bump when the shape or wording of the generated chunks changes, so the
    /// previous version is superseded rather than left answering alongside.
    /// The underlying data carries no version of its own.
    /// </summary>
    internal const string Version = "2026-09-09.1";

    internal const string RelativePath = "Data/Seeds/speaking-candidate-resources.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<CompanionIndexResult> IndexAsync(bool embed, CancellationToken ct)
    {
        var warnings = new List<string>();

        var resources = await LoadAsync(warnings, ct);
        if (resources is null)
        {
            // Deliberately not fatal to the wider reindex: the rulebooks and the
            // taxonomy are still worth publishing. But the warning has to say
            // what the learner-visible consequence is, because an operator
            // reading "0 chunks" would not otherwise connect it to scenario 24.
            warnings.Add(
                "Speaking criteria/intro questions were NOT indexed. Sami will correctly refuse to " +
                "show the approved set rather than invent one, so Testing Pack 1 scenario 24 cannot pass.");
            return new CompanionIndexResult(0, 0, 0, 0, warnings);
        }

        var chunks = BuildChunks(resources)
            .Select(c => new CompanionChunkDraft(c.Heading, c.Text))
            .ToList();

        return await CompanionIndexWriter.WriteAsync(
            db, embeddings, logger, SourceKey, Version,
            source =>
            {
                source.SourceType = "speaking_criteria";
                source.Title = "Speaking assessment criteria and intro questions";
                source.AuthorityClass = CompanionAuthorityClass.DrHeshamApprovedMethod;
                source.State = CompanionSourceState.Approved;
                source.ExamTypeCode = "OET";
                source.ProfessionId = null;
                source.SubtestCode = "speaking";
                // Both pages render without authentication, so this is public reference
                // material rather than gated course content.
                source.IsProprietary = false;
                source.RequiredEntitlementScope = null;
                source.PackageScope = null;
                source.StorageLocator = "data/speaking-candidate-resources.json";
                source.ApprovedAt ??= DateTimeOffset.UtcNow;
            },
            chunks, embed, warnings, ct);
    }

    /// <summary>
    /// One chunk per criterion, plus an overview and the intro-question set.
    ///
    /// <para>
    /// Chunked per criterion because that is how a learner asks — "what is
    /// Appropriateness of Language?" — and because the per-turn evidence cap
    /// would otherwise let one enormous block crowd out everything else.
    /// </para>
    /// </summary>
    internal static IEnumerable<(string Heading, string Text)> BuildChunks(SpeakingCandidateResources resources)
    {
        yield return (
            "Speaking assessment criteria — overview",
            "OET Speaking is marked on nine criteria in two groups.\n" +
            "Four linguistic criteria, each scored 0–6: " +
            string.Join(", ", resources.LinguisticCriteria.Select(c => c.Name)) + ".\n" +
            "Five clinical communication criteria, each scored 0–3: " +
            string.Join(", ", resources.ClinicalCriteria.Select(c => $"{c.Letter}. {c.Name}")) + ".\n" +
            "The linguistic criteria assess the language itself; the clinical communication criteria assess how effectively you " +
            "conduct the consultation. A candidate can speak accurate English and still lose marks on the clinical criteria, " +
            "and this is the most common reason a strong speaker underperforms.");

        foreach (var criterion in resources.LinguisticCriteria)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{criterion.Name} is a linguistic criterion scored from 0 to {criterion.MaxBand}.");
            foreach (var band in criterion.Bands.OrderByDescending(b => b.Band))
            {
                sb.AppendLine($"Band {band.Band}: {string.Join(" ", band.Descriptors)}");
            }

            yield return ($"Speaking criterion — {criterion.Name} (0–{criterion.MaxBand})", sb.ToString().Trim());
        }

        foreach (var criterion in resources.ClinicalCriteria)
        {
            var sb = new StringBuilder();
            sb.AppendLine(
                $"{criterion.Letter}. {criterion.Name} is a clinical communication criterion scored from 0 to {criterion.MaxScore}.");

            if (criterion.Scale.Count > 0)
            {
                sb.AppendLine("Scale: " + string.Join(" | ", criterion.Scale));
            }

            if (criterion.Indicators.Count > 0)
            {
                sb.AppendLine("What the assessor looks for:");
                foreach (var indicator in criterion.Indicators)
                {
                    sb.AppendLine($"- {indicator.Code}: {indicator.Text}");
                }
            }

            yield return (
                $"Speaking criterion — {criterion.Letter}. {criterion.Name} (0–{criterion.MaxScore})",
                sb.ToString().Trim());
        }

        var intro = new StringBuilder();
        intro.AppendLine(
            $"The OET Speaking test opens with a short unassessed warm-up. These are the {resources.IntroQuestions.Count} " +
            "approved introductory questions, with sample answers to PERSONALISE — they are not to be memorised word for word. " +
            "Replace the bracketed details with the candidate's own profession, experience, country and career plan.");
        intro.AppendLine();

        foreach (var question in resources.IntroQuestions.OrderBy(q => q.No))
        {
            intro.AppendLine($"{question.No}. {question.Question}");
            intro.AppendLine($"   Sample answer to adapt: {question.SampleAnswer}");
            if (!string.IsNullOrWhiteSpace(question.Note))
            {
                intro.AppendLine($"   Note: {question.Note}");
            }
        }

        yield return ("Speaking intro questions and sample answers", intro.ToString().Trim());
    }

    private async Task<SpeakingCandidateResources?> LoadAsync(List<string> warnings, CancellationToken ct)
    {
        // Next to the assembly, which is where the csproj <None Include> link
        // copies it — in the API's publish output and in the test output alike.
        // Deliberately not ContentRootPath: that differs between hosting the API
        // and running a unit test, and the whole point is that both read the
        // same shipped bytes.
        var path = Path.Combine(AppContext.BaseDirectory, RelativePath);

        if (!File.Exists(path))
        {
            logger.LogWarning("Speaking candidate resources not found at {Path}.", path);
            warnings.Add($"Speaking candidate resources file missing: {RelativePath}.");
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var parsed = await JsonSerializer.DeserializeAsync<SpeakingCandidateResources>(stream, JsonOptions, ct);

            if (parsed is null ||
                parsed.LinguisticCriteria.Count == 0 ||
                parsed.ClinicalCriteria.Count == 0 ||
                parsed.IntroQuestions.Count == 0)
            {
                warnings.Add($"Speaking candidate resources at {RelativePath} parsed but were empty or incomplete.");
                return null;
            }

            return parsed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to read speaking candidate resources from {Path}.", path);
            warnings.Add($"Speaking candidate resources at {RelativePath} could not be parsed: {ex.GetType().Name}.");
            return null;
        }
    }

}

// ── The shape of data/speaking-candidate-resources.json ──────────────────────
// Mirrors the TypeScript interfaces in lib/speaking-candidate-resources.ts.

public sealed class SpeakingCandidateResources
{
    [JsonPropertyName("linguisticCriteria")]
    public List<SpeakingLinguisticCriterion> LinguisticCriteria { get; set; } = [];

    [JsonPropertyName("clinicalCriteria")]
    public List<SpeakingClinicalCriterion> ClinicalCriteria { get; set; } = [];

    [JsonPropertyName("introQuestions")]
    public List<SpeakingIntroQuestion> IntroQuestions { get; set; } = [];
}

public sealed class SpeakingLinguisticCriterion
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int MaxBand { get; set; }
    public List<SpeakingLinguisticBand> Bands { get; set; } = [];
}

public sealed class SpeakingLinguisticBand
{
    public int Band { get; set; }
    public List<string> Descriptors { get; set; } = [];
}

public sealed class SpeakingClinicalCriterion
{
    public string Id { get; set; } = string.Empty;
    public string Letter { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int MaxScore { get; set; }
    public List<string> Scale { get; set; } = [];
    public List<SpeakingClinicalIndicator> Indicators { get; set; } = [];
}

public sealed class SpeakingClinicalIndicator
{
    public string Code { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public sealed class SpeakingIntroQuestion
{
    public int No { get; set; }
    public string Question { get; set; } = string.Empty;
    public string SampleAnswer { get; set; } = string.Empty;
    public string? Note { get; set; }
}
