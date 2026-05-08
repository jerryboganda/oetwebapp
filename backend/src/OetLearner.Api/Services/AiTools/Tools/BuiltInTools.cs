using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.AiTools.Tools;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 5 v1 tool catalog. Seven implementations:
//   4 read    : LookupRulebookRule, LookupVocabularyTerm, GetUserRecentAttempts, SearchRecallSet
//   2 self-write: SaveUserNote, BookmarkRecallTerm
//   1 ext-net : FetchDictionaryDefinition  (api.dictionaryapi.dev)
// Catalog rows seeded by AiToolRegistry on first read; grants are deny-by-default.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class LookupRulebookRuleTool : IAiToolExecutor
{
    public string Code => "lookup_rulebook_rule";
    public AiToolCategory Category => AiToolCategory.Read;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "kind":{"type":"string","enum":["writing","speaking","listening","grammar","pronunciation","conversation","vocabulary"]},
        "rule_id":{"type":"string","minLength":1,"maxLength":120},
        "profession":{"type":"string","maxLength":32}
      },
      "required":["kind","rule_id"],
      "additionalProperties":false
    }
    """;

    private readonly IRulebookLoader _loader;
    public LookupRulebookRuleTool(IRulebookLoader loader) { _loader = loader; }

    public Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        var kindStr = args.GetProperty("kind").GetString()!;
        var ruleId = args.GetProperty("rule_id").GetString()!;
        var prof = args.TryGetProperty("profession", out var p) ? p.GetString() : null;
        if (!Enum.TryParse<RuleKind>(kindStr, ignoreCase: true, out var kind))
        {
            return Task.FromResult(new AiToolExecutionResult(AiToolOutcome.ArgsInvalid, null, "kind_unknown", "kind not recognised"));
        }
        var profession = !string.IsNullOrWhiteSpace(prof) && Enum.TryParse<ExamProfession>(prof, ignoreCase: true, out var ep)
            ? ep
            : ExamProfession.Medicine;

        var rule = _loader.FindRule(kind, profession, ruleId);
        if (rule is null)
        {
            return Task.FromResult(new AiToolExecutionResult(
                AiToolOutcome.Success,
                ToJson(new { found = false, rule_id = ruleId, kind = kindStr, profession = profession.ToString().ToLowerInvariant() })));
        }
        return Task.FromResult(new AiToolExecutionResult(
            AiToolOutcome.Success,
            ToJson(new
            {
                found = true,
                rule_id = rule.Id,
                section = rule.Section,
                title = rule.Title,
                body = rule.Body,
                severity = rule.Severity.ToString().ToLowerInvariant(),
                profession = profession.ToString().ToLowerInvariant(),
            })));
    }

    internal static JsonElement ToJson(object payload) =>
        JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement.Clone();
}

public sealed class LookupVocabularyTermTool : IAiToolExecutor
{
    public string Code => "lookup_vocabulary_term";
    public AiToolCategory Category => AiToolCategory.Read;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "lemma":{"type":"string","minLength":1,"maxLength":120},
        "profession":{"type":"string","maxLength":32}
      },
      "required":["lemma"],
      "additionalProperties":false
    }
    """;

    private readonly LearnerDbContext _db;
    public LookupVocabularyTermTool(LearnerDbContext db) { _db = db; }

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        var lemma = args.GetProperty("lemma").GetString()!;
        var prof = args.TryGetProperty("profession", out var p) ? p.GetString() : null;

        var q = _db.VocabularyTerms.AsNoTracking().Where(t => t.Term == lemma);
        if (!string.IsNullOrEmpty(prof)) q = q.Where(t => t.ProfessionId == prof || t.ProfessionId == null);
        var term = await q.OrderBy(t => t.ProfessionId == null ? 1 : 0).FirstOrDefaultAsync(ct);

        if (term is null)
        {
            return new AiToolExecutionResult(AiToolOutcome.Success,
                LookupRulebookRuleTool.ToJson(new { found = false, lemma }));
        }
        return new AiToolExecutionResult(AiToolOutcome.Success,
            LookupRulebookRuleTool.ToJson(new
            {
                found = true,
                lemma = term.Term,
                definition = term.Definition,
                example = term.ExampleSentence,
                ipa = term.IpaPronunciation,
                profession = term.ProfessionId,
            }));
    }
}

public sealed class GetUserRecentAttemptsTool : IAiToolExecutor
{
    public string Code => "get_user_recent_attempts";
    public AiToolCategory Category => AiToolCategory.Read;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "skill":{"type":"string","enum":["writing","reading","listening","speaking","pronunciation"]},
        "limit":{"type":"integer","minimum":1,"maximum":10}
      },
      "required":["skill"],
      "additionalProperties":false
    }
    """;

    private readonly LearnerDbContext _db;
    public GetUserRecentAttemptsTool(LearnerDbContext db) { _db = db; }

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId))
        {
            return new AiToolExecutionResult(AiToolOutcome.Success,
                LookupRulebookRuleTool.ToJson(new { attempts = Array.Empty<object>() }));
        }
        var skill = args.GetProperty("skill").GetString()!;
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 5;
        if (limit < 1) limit = 1;
        if (limit > 10) limit = 10;

        var rows = await _db.Attempts.AsNoTracking()
            .Where(a => a.UserId == ctx.UserId && a.SubtestCode == skill)
            .OrderByDescending(a => a.SubmittedAt ?? a.StartedAt)
            .Take(limit)
            .Select(a => new
            {
                id = a.Id,
                state = a.State.ToString().ToLowerInvariant(),
                started_at = a.StartedAt,
                submitted_at = a.SubmittedAt,
                elapsed_seconds = a.ElapsedSeconds,
            })
            .ToListAsync(ct);

        return new AiToolExecutionResult(AiToolOutcome.Success,
            LookupRulebookRuleTool.ToJson(new { attempts = rows }));
    }
}

public sealed class SearchRecallSetTool : IAiToolExecutor
{
    public string Code => "search_recall_set";
    public AiToolCategory Category => AiToolCategory.Read;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "q":{"type":"string","minLength":1,"maxLength":120},
        "profession":{"type":"string","maxLength":32},
        "limit":{"type":"integer","minimum":1,"maximum":20}
      },
      "required":["q"],
      "additionalProperties":false
    }
    """;

    private readonly LearnerDbContext _db;
    public SearchRecallSetTool(LearnerDbContext db) { _db = db; }

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        var q = args.GetProperty("q").GetString()!;
        var prof = args.TryGetProperty("profession", out var pp) ? pp.GetString() : null;
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 8;
        if (limit < 1) limit = 1;
        if (limit > 20) limit = 20;

        var pattern = $"%{q}%";
        var qry = _db.VocabularyTerms.AsNoTracking()
            .Where(t => EF.Functions.ILike(t.Term, pattern) || EF.Functions.ILike(t.Definition, pattern));
        if (!string.IsNullOrEmpty(prof)) qry = qry.Where(t => t.ProfessionId == prof || t.ProfessionId == null);

        List<object> items;
        try
        {
            items = (await qry.Take(limit).Select(t => new { id = t.Id, lemma = t.Term, definition = t.Definition }).ToListAsync(ct))
                .Cast<object>().ToList();
        }
        catch
        {
            // SQLite fallback (tests): ILike not supported.
            var lower = q.ToLowerInvariant();
            var qry2 = _db.VocabularyTerms.AsNoTracking()
                .Where(t => t.Term.ToLower().Contains(lower) || t.Definition.ToLower().Contains(lower));
            if (!string.IsNullOrEmpty(prof)) qry2 = qry2.Where(t => t.ProfessionId == prof || t.ProfessionId == null);
            items = (await qry2.Take(limit).Select(t => new { id = t.Id, lemma = t.Term, definition = t.Definition }).ToListAsync(ct))
                .Cast<object>().ToList();
        }

        return new AiToolExecutionResult(AiToolOutcome.Success,
            LookupRulebookRuleTool.ToJson(new { items }));
    }
}

public sealed class SaveUserNoteTool : IAiToolExecutor
{
    public string Code => "save_user_note";
    public AiToolCategory Category => AiToolCategory.Write;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "title":{"type":"string","minLength":1,"maxLength":120},
        "body_markdown":{"type":"string","minLength":1,"maxLength":2000}
      },
      "required":["title","body_markdown"],
      "additionalProperties":false
    }
    """;

    private readonly LearnerDbContext _db;
    public SaveUserNoteTool(LearnerDbContext db) { _db = db; }

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId))
        {
            return new AiToolExecutionResult(AiToolOutcome.RbacDenied, null, "no_user", "tool requires authenticated user");
        }
        var title = args.GetProperty("title").GetString()!;
        var body = args.GetProperty("body_markdown").GetString()!;
        var now = DateTimeOffset.UtcNow;
        var note = new UserNote
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = ctx.UserId!,
            Title = title,
            BodyMarkdown = body,
            Source = "ai_tool",
            CreatedByFeatureCode = ctx.FeatureCode,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.UserNotes.Add(note);
        await _db.SaveChangesAsync(ct);
        return new AiToolExecutionResult(AiToolOutcome.Success,
            LookupRulebookRuleTool.ToJson(new { note_id = note.Id, created_at = now }));
    }
}

public sealed class BookmarkRecallTermTool : IAiToolExecutor
{
    public string Code => "bookmark_recall_term";
    public AiToolCategory Category => AiToolCategory.Write;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "vocabulary_term_id":{"type":"string","minLength":1,"maxLength":64}
      },
      "required":["vocabulary_term_id"],
      "additionalProperties":false
    }
    """;

    private readonly LearnerDbContext _db;
    public BookmarkRecallTermTool(LearnerDbContext db) { _db = db; }

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId))
        {
            return new AiToolExecutionResult(AiToolOutcome.RbacDenied, null, "no_user", "tool requires authenticated user");
        }
        var vocabId = args.GetProperty("vocabulary_term_id").GetString()!;
        var exists = await _db.VocabularyTerms.AsNoTracking().AnyAsync(t => t.Id == vocabId, ct);
        if (!exists)
        {
            return new AiToolExecutionResult(AiToolOutcome.Success,
                LookupRulebookRuleTool.ToJson(new { bookmarked = false, reason = "term_not_found" }));
        }
        var prior = await _db.RecallBookmarks
            .FirstOrDefaultAsync(b => b.UserId == ctx.UserId && b.VocabularyTermId == vocabId, ct);
        if (prior is not null)
        {
            return new AiToolExecutionResult(AiToolOutcome.Success,
                LookupRulebookRuleTool.ToJson(new { bookmarked = true, bookmark_id = prior.Id, already = true }));
        }
        var b = new RecallBookmark
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = ctx.UserId!,
            VocabularyTermId = vocabId,
            Source = "ai_tool",
            CreatedByFeatureCode = ctx.FeatureCode,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _db.RecallBookmarks.Add(b);
        await _db.SaveChangesAsync(ct);
        return new AiToolExecutionResult(AiToolOutcome.Success,
            LookupRulebookRuleTool.ToJson(new { bookmarked = true, bookmark_id = b.Id }));
    }
}

public sealed class FetchDictionaryDefinitionTool : IAiToolExecutor
{
    public string Code => "fetch_dictionary_definition";
    public AiToolCategory Category => AiToolCategory.ExternalNetwork;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "word":{"type":"string","minLength":1,"maxLength":40}
      },
      "required":["word"],
      "additionalProperties":false
    }
    """;

    public const string HttpClientName = "AiTool.FetchDictionary";
    private const string AllowedHost = "api.dictionaryapi.dev";

    private readonly IHttpClientFactory _factory;
    private readonly IOptionsMonitor<AiToolOptions> _opts;
    private readonly ILogger<FetchDictionaryDefinitionTool> _logger;

    public FetchDictionaryDefinitionTool(
        IHttpClientFactory factory,
        IOptionsMonitor<AiToolOptions> opts,
        ILogger<FetchDictionaryDefinitionTool> logger)
    {
        _factory = factory;
        _opts = opts;
        _logger = logger;
    }

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        var word = args.GetProperty("word").GetString()!.Trim();
        // Word allowlist: ASCII letters + hyphen only.
        foreach (var ch in word)
        {
            if (!(char.IsLetter(ch) || ch == '-'))
            {
                return new AiToolExecutionResult(AiToolOutcome.ArgsInvalid, null,
                    "word_invalid", "word must contain only letters and hyphens");
            }
        }

        // Hard host check (defence in depth).
        var allowed = _opts.CurrentValue.AllowedExternalHosts ?? Array.Empty<string>();
        if (!allowed.Contains(AllowedHost, StringComparer.OrdinalIgnoreCase))
        {
            return new AiToolExecutionResult(AiToolOutcome.BlockedHost, null,
                "host_not_allowed", $"host {AllowedHost} not in AllowedExternalHosts");
        }

        var url = $"https://{AllowedHost}/api/v2/entries/en/{Uri.EscapeDataString(word)}";
        var client = _factory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromMilliseconds(_opts.CurrentValue.ExternalNetworkTimeoutMilliseconds);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.UserAgent.Clear();
            req.Headers.UserAgent.ParseAdd("OET-Tool/1");

            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new AiToolExecutionResult(AiToolOutcome.Success,
                    LookupRulebookRuleTool.ToJson(new { found = false, word }));
            }
            if (!resp.IsSuccessStatusCode)
            {
                return new AiToolExecutionResult(AiToolOutcome.ProviderError, null,
                    "http_status", $"upstream {(int)resp.StatusCode}");
            }

            var maxBytes = _opts.CurrentValue.ExternalNetworkMaxResponseBytes;
            await using var body = await resp.Content.ReadAsStreamAsync(ct);
            using var ms = new MemoryStream(capacity: 16 * 1024);
            var buf = new byte[8 * 1024];
            int read;
            int total = 0;
            while ((read = await body.ReadAsync(buf, 0, buf.Length, ct)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    return new AiToolExecutionResult(AiToolOutcome.ProviderError, null,
                        "body_too_large", "upstream response exceeded max-bytes");
                }
                await ms.WriteAsync(buf, 0, read, ct);
            }
            ms.Position = 0;
            using var doc = JsonDocument.Parse(ms);
            // Project to a flat shape; drop fields we don't need before they hit the model.
            var out0 = new List<object>();
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in doc.RootElement.EnumerateArray())
                {
                    var meanings = new List<object>();
                    if (entry.TryGetProperty("meanings", out var mElem) && mElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var m in mElem.EnumerateArray())
                        {
                            var defs = new List<string>();
                            if (m.TryGetProperty("definitions", out var dElem) && dElem.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var d in dElem.EnumerateArray())
                                {
                                    if (d.TryGetProperty("definition", out var defStr) && defStr.ValueKind == JsonValueKind.String)
                                    {
                                        var s = defStr.GetString();
                                        if (!string.IsNullOrEmpty(s)) defs.Add(s);
                                    }
                                }
                            }
                            var pos = m.TryGetProperty("partOfSpeech", out var posEl) ? posEl.GetString() : null;
                            meanings.Add(new { part_of_speech = pos, definitions = defs });
                        }
                    }
                    var phonetic = entry.TryGetProperty("phonetic", out var ph) && ph.ValueKind == JsonValueKind.String
                        ? ph.GetString() : null;
                    out0.Add(new
                    {
                        word = entry.TryGetProperty("word", out var w) ? w.GetString() : word,
                        phonetic,
                        meanings,
                    });
                }
            }
            return new AiToolExecutionResult(AiToolOutcome.Success,
                LookupRulebookRuleTool.ToJson(new { found = out0.Count > 0, entries = out0 }));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new AiToolExecutionResult(AiToolOutcome.ProviderError, null, "timeout", "upstream timed out");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "FetchDictionaryDefinitionTool HTTP error");
            return new AiToolExecutionResult(AiToolOutcome.ProviderError, null, "http", ex.Message);
        }
    }
}
