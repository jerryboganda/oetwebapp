using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Rulebooks;

/// <summary>
/// One-time bootstrap helper that imports the canonical rulebook JSON files
/// (under /rulebooks at the repo root, embedded as content in the API project)
/// into the DB tables introduced by migration AddRulebookManagement.
///
/// Idempotent: skipped entirely if RulebookVersions already has any rows.
/// We import each (kind, profession) JSON as a single Published row.
/// Subsequent edits live in DB; the JSON files become the seed source only.
/// </summary>
public static class RulebookSeeder
{
    /// <summary>Repo-relative folder for canonical rulebook JSON.</summary>
    private const string RulebookFolderName = "rulebooks";

    public static async Task EnsureAsync(LearnerDbContext db, IWebHostEnvironment env, CancellationToken ct)
    {
        if (await db.RulebookVersions.AnyAsync(ct))
        {
            return;
        }

        var rulebookRoot = ResolveRulebookRoot(env);
        if (rulebookRoot is null || !Directory.Exists(rulebookRoot))
        {
            // No JSON to seed — admin can author from scratch via the UI.
            return;
        }

        var now = DateTimeOffset.UtcNow;

        foreach (var kindDir in Directory.EnumerateDirectories(rulebookRoot))
        {
            var kindName = Path.GetFileName(kindDir).ToLowerInvariant();
            foreach (var profDir in Directory.EnumerateDirectories(kindDir))
            {
                var profName = Path.GetFileName(profDir).ToLowerInvariant();
                if (profName == "common")
                {
                    continue; // assessment-criteria.json lives here, not a rulebook
                }

                var jsonPath = Path.Combine(profDir, "rulebook.v1.json");
                if (!File.Exists(jsonPath))
                {
                    continue;
                }

                try
                {
                    await using var stream = File.OpenRead(jsonPath);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                    var root = doc.RootElement;

                    var version = TryGetString(root, "version") ?? "1.0.0";
                    var authority = TryGetString(root, "authoritySource");
                    var publishedAt = TryGetString(root, "publishedAt");
                    var versionId = $"rb_{kindName}_{profName}_{version}";

                    var rulebook = new RulebookVersion
                    {
                        Id = versionId,
                        Kind = kindName,
                        Profession = profName,
                        Version = version,
                        Status = RulebookStatus.Published,
                        AuthoritySource = authority,
                        CreatedAt = now,
                        UpdatedAt = now,
                        PublishedAt = TryParseDate(publishedAt) ?? now,
                        UpdatedByUserId = "system",
                    };
                    db.RulebookVersions.Add(rulebook);

                    var sectionOrder = 0;
                    if (root.TryGetProperty("sections", out var sectionsEl) && sectionsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var s in sectionsEl.EnumerateArray())
                        {
                            var code = TryGetString(s, "id") ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(code)) continue;
                            db.RulebookSectionRows.Add(new RulebookSectionRow
                            {
                                Id = Guid.NewGuid().ToString(),
                                RulebookVersionId = versionId,
                                Code = code,
                                Title = TryGetString(s, "title") ?? code,
                                OrderIndex = TryGetInt(s, "order") ?? sectionOrder,
                            });
                            sectionOrder++;
                        }
                    }

                    var ruleOrder = 0;
                    if (root.TryGetProperty("rules", out var rulesEl) && rulesEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var r in rulesEl.EnumerateArray())
                        {
                            var code = TryGetString(r, "id") ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(code)) continue;
                            db.RulebookRuleRows.Add(new RulebookRuleRow
                            {
                                Id = Guid.NewGuid().ToString(),
                                RulebookVersionId = versionId,
                                Code = code,
                                SectionCode = TryGetString(r, "section") ?? string.Empty,
                                Title = TryGetString(r, "title") ?? code,
                                Body = TryGetString(r, "body") ?? string.Empty,
                                Severity = TryGetString(r, "severity") ?? "info",
                                AppliesToJson = TryGetRawJson(r, "appliesTo") ?? "\"all\"",
                                TurnStage = TryGetString(r, "turnStage"),
                                ExemplarPhrasesJson = TryGetRawJson(r, "exemplarPhrases"),
                                ForbiddenPatternsJson = TryGetRawJson(r, "forbiddenPatterns"),
                                CheckId = TryGetString(r, "checkId"),
                                ParamsJson = TryGetRawJson(r, "params"),
                                ExamplesJson = TryGetRawJson(r, "examples"),
                                OrderIndex = ruleOrder,
                            });
                            ruleOrder++;
                        }
                    }
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    // Bad JSON or unreadable file — skip; admin can fix in UI.
                    continue;
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static string? ResolveRulebookRoot(IWebHostEnvironment env)
    {
        // 1) Repo layout (dev): backend/src/OetLearner.Api → ../../../rulebooks
        var contentRoot = env.ContentRootPath;
        var candidate = Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "..", RulebookFolderName));
        if (Directory.Exists(candidate)) return candidate;

        // 2) Production container layout (Dockerfile copies repo /rulebooks to /app/rulebooks)
        candidate = Path.Combine(contentRoot, RulebookFolderName);
        if (Directory.Exists(candidate)) return candidate;

        // 3) Walk up from content root looking for the folder.
        var probe = new DirectoryInfo(contentRoot);
        for (var i = 0; i < 6 && probe is not null; i++)
        {
            var test = Path.Combine(probe.FullName, RulebookFolderName);
            if (Directory.Exists(test)) return test;
            probe = probe.Parent;
        }

        return null;
    }

    private static string? TryGetString(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static int? TryGetInt(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)
            ? n
            : null;

    private static string? TryGetRawJson(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var el) && el.ValueKind != JsonValueKind.Null && el.ValueKind != JsonValueKind.Undefined
            ? el.GetRawText()
            : null;

    private static DateTimeOffset? TryParseDate(string? s)
        => DateTimeOffset.TryParse(s, out var d) ? d : null;
}
