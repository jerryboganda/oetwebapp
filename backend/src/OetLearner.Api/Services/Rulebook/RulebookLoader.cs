using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Loads OET rulebook JSON content embedded in the assembly at build time.
/// The files live in /rulebooks at the repo root and are shared with the
/// TypeScript rule engine — both runtimes MUST see identical content.
/// </summary>
public interface IRulebookLoader
{
    OetRulebook Load(RuleKind kind, ExamProfession profession);
    IEnumerable<OetRulebook> All();
    OetRule? FindRule(RuleKind kind, ExamProfession profession, string ruleId);
    JsonElement GetAssessmentCriteria(RuleKind kind);
}

public sealed class RulebookLoader : IRulebookLoader
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly Dictionary<string, OetRulebook> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<RuleKind, JsonElement> _assessmentCache = new();

    public RulebookLoader()
    {
        foreach (var kind in Enum.GetValues<RuleKind>())
        {
            foreach (var profession in Enum.GetValues<ExamProfession>())
            {
                var key = BuildKey(kind, profession);
                var stream = OpenResource($"OetRulebooks/{FolderOf(kind)}/{FolderOf(profession)}/rulebook.v1.json");
                if (stream is null) continue;
                using var _ = stream;
                var book = JsonSerializer.Deserialize<OetRulebook>(stream, JsonOpts);
                if (book is null) continue;
                _cache[key] = book;
            }
        }

        foreach (var kind in Enum.GetValues<RuleKind>())
        {
            var stream = OpenResource($"OetRulebooks/{FolderOf(kind)}/common/assessment-criteria.json");
            if (stream is null) continue;
            using var _ = stream;
            using var doc = JsonDocument.Parse(stream);
            _assessmentCache[kind] = doc.RootElement.Clone();
        }
    }

    public OetRulebook Load(RuleKind kind, ExamProfession profession)
    {
        var key = BuildKey(kind, profession);
        if (_cache.TryGetValue(key, out var book)) return book;
        throw new RulebookNotFoundException(kind, profession);
    }

    public IEnumerable<OetRulebook> All() => _cache.Values;

    public OetRule? FindRule(RuleKind kind, ExamProfession profession, string ruleId)
    {
        if (!_cache.TryGetValue(BuildKey(kind, profession), out var book)) return null;
        return book.Rules.FirstOrDefault(r => string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase));
    }

    public JsonElement GetAssessmentCriteria(RuleKind kind)
    {
        if (_assessmentCache.TryGetValue(kind, out var el)) return el;
        throw new InvalidOperationException($"Assessment criteria for {kind} not loaded.");
    }

    private static string BuildKey(RuleKind k, ExamProfession p) => $"{k}:{p}".ToLowerInvariant();

    private static string FolderOf(RuleKind kind) => kind switch
    {
        RuleKind.Writing => "writing",
        RuleKind.Speaking => "speaking",
        RuleKind.Grammar => "grammar",
        RuleKind.Pronunciation => "pronunciation",
        RuleKind.Vocabulary => "vocabulary",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string FolderOf(ExamProfession p) => ToKebabCase(p.ToString());

    private static string ToKebabCase(string value)
    {
        var builder = new StringBuilder(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (i > 0 && char.IsUpper(current))
            {
                var previous = value[i - 1];
                var nextIsLower = i + 1 < value.Length && char.IsLower(value[i + 1]);
                if (char.IsLower(previous) || nextIsLower)
                {
                    builder.Append('-');
                }
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }

    private static Stream? OpenResource(string logicalName)
    {
        var asm = typeof(RulebookLoader).Assembly;
        var allNames = asm.GetManifestResourceNames();
        // LogicalName values were set with backslashes in the .csproj; embedded
        // resources preserve whichever separator was provided. Try both shapes
        // so the loader is robust to Windows and POSIX build hosts.
        string[] candidates =
        {
            logicalName,
            logicalName.Replace('/', '\\'),
            logicalName.Replace('\\', '/'),
            logicalName.Replace('/', '.'),
            logicalName.Replace('\\', '.'),
        };
        foreach (var candidate in candidates)
        {
            var match = allNames.FirstOrDefault(n => string.Equals(n, candidate, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return asm.GetManifestResourceStream(match);
        }
        return null;
    }
}

public sealed class RulebookNotFoundException(RuleKind kind, ExamProfession profession)
    : InvalidOperationException(
        $"OET rulebook not found for kind='{kind}' profession='{profession}'. " +
        "Only rulebooks with JSON files under /rulebooks/ are available.");

// ---------------------------------------------------------------------------
// Types — mirror lib/rulebook/types.ts
// ---------------------------------------------------------------------------

public enum RuleKind { Writing, Speaking, Grammar, Pronunciation, Vocabulary }

public enum ExamProfession
{
    Medicine,
    Nursing,
    Dentistry,
    Pharmacy,
    Physiotherapy,
    Veterinary,
    Optometry,
    Radiography,
    OccupationalTherapy,
    SpeechPathology,
    Podiatry,
    Dietetics,
}

public enum RuleSeverity { Critical, Major, Minor, Info }

public sealed record OetRulebookSection(string Id, string Title, int? Order = null);

public sealed record OetRulebook
{
    public string Version { get; init; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RuleKind Kind { get; init; }
    [JsonConverter(typeof(LowercaseEnumConverter<ExamProfession>))]
    public ExamProfession Profession { get; init; }
    public string? PublishedAt { get; init; }
    public string? AuthoritySource { get; init; }
    public List<OetRulebookSection> Sections { get; init; } = new();
    public List<OetRule> Rules { get; init; } = new();
    public JsonElement? Tables { get; init; }
    public JsonElement? StateMachines { get; init; }
}

public sealed record OetRule
{
    public string Id { get; init; } = "";
    public string Section { get; init; } = "";
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";

    [JsonConverter(typeof(LowercaseEnumConverter<RuleSeverity>))]
    public RuleSeverity Severity { get; init; } = RuleSeverity.Info;

    public JsonElement? AppliesTo { get; init; } // string "all" or array of strings
    public string? TurnStage { get; init; }
    public List<string>? ExemplarPhrases { get; init; }
    public List<string>? ForbiddenPatterns { get; init; }
    public string? CheckId { get; init; }
    public JsonElement? Params { get; init; }
    public JsonElement? Examples { get; init; }
}

// Custom converter to accept lowercase enum values coming from JSON.
internal sealed class LowercaseEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString() ?? "";
        // Normalise to PascalCase to align with enum names (snake_case -> PascalCase).
        var parts = s.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var pascal = string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
        if (Enum.TryParse<T>(pascal, ignoreCase: true, out var v)) return v;
        if (Enum.TryParse<T>(s, ignoreCase: true, out v)) return v;
        throw new JsonException($"Value '{s}' is not a valid {typeof(T).Name}.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString().ToLowerInvariant());
}

// ---------------------------------------------------------------------------
// Lint findings + inputs
// ---------------------------------------------------------------------------

public sealed record LintFinding(
    string RuleId,
    RuleSeverity Severity,
    string Message,
    string? Quote = null,
    int? Start = null,
    int? End = null,
    string? FixSuggestion = null);

public sealed record WritingLintInput(
    string LetterText,
    string LetterType,
    string? RecipientSpecialty = null,
    string? RecipientName = null,
    int? PatientAge = null,
    bool PatientIsMinor = false,
    WritingCaseNotesMarkers? CaseNotesMarkers = null,
    ExamProfession Profession = ExamProfession.Medicine);

public sealed record WritingCaseNotesMarkers(
    bool SmokingMentioned = false,
    bool DrinkingMentioned = false,
    bool AllergyMentioned = false,
    bool AtopicCondition = false,
    bool PatientInitiatedReferral = false,
    bool ConsentDocumented = false,
    string? FollowUpDate = null,
    bool ResultsEnclosed = false);

public sealed record SpeakingTurn(
    string Speaker,    // "candidate" | "patient" | "interlocutor"
    string Text,
    int? StartMs = null,
    int? EndMs = null,
    string? Stage = null);

public sealed record SpeakingAuditInput(
    IReadOnlyList<SpeakingTurn> Transcript,
    string CardType,
    ExamProfession Profession = ExamProfession.Medicine,
    int? SilenceAfterDiagnosisMs = null);
