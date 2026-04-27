using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// IRulebookLoader implementation that prefers the admin-managed DB rows
/// (RulebookVersions / RulebookSectionRows / RulebookRuleRows) and falls
/// back to the embedded-JSON loader when no Published row exists for a
/// given (kind, profession).
///
/// Caching:
///   - Per (kind, profession) result is cached in IMemoryCache for 60s to
///     keep grading hot-paths cheap. Admin mutations invalidate the cache
///     synchronously via <see cref="InvalidateCacheKey"/>.
///   - Single-process prod today; if we scale horizontally, swap to a
///     pub/sub invalidation channel.
///
/// Assessment criteria + StateMachines / Tables remain JSON-only — they
/// were not modelled in DB in this slice.
/// </summary>
public sealed class DbBackedRulebookLoader : IRulebookLoader
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly RulebookLoader _jsonInner;
    private readonly LearnerDbContext _db;
    private readonly IMemoryCache _cache;

    public DbBackedRulebookLoader(RulebookLoader jsonInner, LearnerDbContext db, IMemoryCache cache)
    {
        _jsonInner = jsonInner;
        _db = db;
        _cache = cache;
    }

    public static string CacheKey(RuleKind kind, ExamProfession profession)
        => $"rulebook:db:{kind.ToString().ToLowerInvariant()}:{profession.ToString().ToLowerInvariant()}";

    public static string CacheKey(string kindLower, string professionLower)
        => $"rulebook:db:{kindLower}:{professionLower}";

    /// <summary>Drops a cached rulebook so the next read rebuilds from DB.</summary>
    public static void InvalidateCacheKey(IMemoryCache cache, string kindLower, string professionLower)
        => cache.Remove(CacheKey(kindLower, professionLower));

    public OetRulebook Load(RuleKind kind, ExamProfession profession)
    {
        var key = CacheKey(kind, profession);
        if (_cache.TryGetValue(key, out OetRulebook? cached) && cached is not null)
            return cached;

        var fromDb = TryBuildFromDb(kind, profession);
        if (fromDb is not null)
        {
            _cache.Set(key, fromDb, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl });
            return fromDb;
        }

        // No published DB row — JSON fallback. Cache the JSON copy too so we
        // don't re-query DB every grading call for kinds without DB rows.
        var json = _jsonInner.Load(kind, profession);
        _cache.Set(key, json, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl });
        return json;
    }

    public IEnumerable<OetRulebook> All()
    {
        // Combine: DB-backed published rulebooks override JSON for any
        // (kind, profession) pair where DB has a row. JSON fills the rest.
        var dbBooks = new Dictionary<string, OetRulebook>();
        var dbRows = _db.RulebookVersions
            .AsNoTracking()
            .Where(v => v.Status == RulebookStatus.Published)
            .Select(v => new { v.Kind, v.Profession })
            .ToList();

        foreach (var r in dbRows)
        {
            if (!Enum.TryParse<RuleKind>(r.Kind, ignoreCase: true, out var kind)) continue;
            if (!Enum.TryParse<ExamProfession>(r.Profession, ignoreCase: true, out var prof)) continue;
            var book = TryBuildFromDb(kind, prof);
            if (book is null) continue;
            dbBooks[$"{kind}:{prof}".ToLowerInvariant()] = book;
        }

        foreach (var json in _jsonInner.All())
        {
            var key = $"{json.Kind}:{json.Profession}".ToLowerInvariant();
            if (!dbBooks.ContainsKey(key)) yield return json;
        }
        foreach (var book in dbBooks.Values) yield return book;
    }

    public OetRule? FindRule(RuleKind kind, ExamProfession profession, string ruleId)
    {
        var book = Load(kind, profession);
        return book.Rules.FirstOrDefault(r => string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase));
    }

    public JsonElement GetAssessmentCriteria(RuleKind kind)
        => _jsonInner.GetAssessmentCriteria(kind);

    // ── DB → OetRulebook projection ──────────────────────────────────

    private OetRulebook? TryBuildFromDb(RuleKind kind, ExamProfession profession)
    {
        var kindStr = kind.ToString().ToLowerInvariant();
        var profStr = profession.ToString().ToLowerInvariant();

        var version = _db.RulebookVersions.AsNoTracking()
            .FirstOrDefault(v => v.Kind == kindStr && v.Profession == profStr && v.Status == RulebookStatus.Published);
        if (version is null) return null;

        var sectionRows = _db.RulebookSectionRows.AsNoTracking()
            .Where(s => s.RulebookVersionId == version.Id)
            .OrderBy(s => s.OrderIndex).ThenBy(s => s.Code)
            .ToList();

        var ruleRows = _db.RulebookRuleRows.AsNoTracking()
            .Where(r => r.RulebookVersionId == version.Id)
            .OrderBy(r => r.SectionCode).ThenBy(r => r.OrderIndex).ThenBy(r => r.Code)
            .ToList();

        // If the DB row has zero rules (e.g. truly empty admin draft was
        // promoted), prefer the JSON copy rather than serving an empty book
        // to grading. Defensive: should not happen because publish gate
        // would normally enforce content.
        if (ruleRows.Count == 0) return null;

        var sections = sectionRows
            .Select(s => new OetRulebookSection(s.Code, s.Title, s.OrderIndex))
            .ToList();

        var rules = ruleRows.Select(MapRule).Where(r => r is not null).Cast<OetRule>().ToList();

        return new OetRulebook
        {
            Version = version.Version,
            Kind = kind,
            Profession = profession,
            PublishedAt = version.PublishedAt?.ToString("o"),
            AuthoritySource = version.AuthoritySource,
            Sections = sections,
            Rules = rules,
            // Tables / StateMachines: not modelled in DB. Pull from JSON if available.
            Tables = TryGetJsonExtension(kind, profession, b => b.Tables),
            StateMachines = TryGetJsonExtension(kind, profession, b => b.StateMachines),
        };
    }

    private OetRule? MapRule(Domain.RulebookRuleRow r)
    {
        if (!Enum.TryParse<RuleSeverity>(r.Severity, ignoreCase: true, out var severity))
            severity = RuleSeverity.Info;

        return new OetRule
        {
            Id = r.Code,
            Section = r.SectionCode,
            Title = r.Title,
            Body = r.Body,
            Severity = severity,
            AppliesTo = ParseJsonElement(r.AppliesToJson),
            TurnStage = r.TurnStage,
            ExemplarPhrases = ParseStringList(r.ExemplarPhrasesJson),
            ForbiddenPatterns = ParseStringList(r.ForbiddenPatternsJson),
            CheckId = r.CheckId,
            Params = ParseJsonElement(r.ParamsJson),
            Examples = ParseJsonElement(r.ExamplesJson),
        };
    }

    private JsonElement? TryGetJsonExtension(RuleKind kind, ExamProfession profession, Func<OetRulebook, JsonElement?> picker)
    {
        try
        {
            var json = _jsonInner.Load(kind, profession);
            return picker(json);
        }
        catch
        {
            return null;
        }
    }

    private static JsonElement? ParseJsonElement(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.Clone();
        }
        catch
        {
            // Treat malformed JSON as a string literal — safer than crashing grading.
            return JsonSerializer.SerializeToElement(raw);
        }
    }

    private static List<string>? ParseStringList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<string>>(raw, JsonOpts);
        }
        catch
        {
            return null;
        }
    }
}
