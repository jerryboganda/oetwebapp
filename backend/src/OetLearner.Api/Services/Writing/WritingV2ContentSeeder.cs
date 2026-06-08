using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
// WritingV2ContentSeeder â€” OET Writing Module V2 launch content.
//
// Loads the 8 versioned JSON files in Data/Seeds/WritingV2/ on boot and
// idempotently materialises them into the relational Writing V2 tables.
//
//   1. canon-rules.launch-25.json    â†’ WritingCanonRules (25 rules SC-001..025)
//   2. scenarios.diagnostic.json     â†’ WritingScenarios + WritingScenarioStructuredSentences (12)
//   3. exemplars.json                â†’ WritingExemplars + WritingExemplarAnnotations (6)
//   4. lessons.json                  â†’ WritingLessonsV2 (16, 2 per W1-W8)
//   5. drills.sentence.json          â†’ WritingDrills (30, 3 per drill type Ã— 10)
//   6. drills.case-notes.json        â†’ WritingCaseNoteDrills + WritingCaseNoteDrillSentences (12)
//   7. mocks.json                    â†’ mock scenarios first, then WritingMocks (6 + 6)
//   8. common-mistakes.json          â†’ WritingCommonMistakes (20)
//
// Seed order: canon â†’ scenarios â†’ exemplars â†’ lessons â†’ drills â†’ case-note
// drills â†’ mocks (with their scenarios first) â†’ common mistakes.
//
// Embeddings (text-embedding-3-small) are NOT generated at seed time â€” they
// are lazily populated by the WritingExemplarReindexCron tick (WS5).
//
// Idempotency contract:
//   â€¢ Per-table existence check via Id â€” re-runs are no-ops once seeded.
//   â€¢ Deterministic GUIDs in JSON keep ids stable across machines.
//   â€¢ If a partial seed previously ran (e.g., container restart), only the
//     missing rows are added; existing rows are not mutated.
//   â€¢ Never deletes. Removed rows in JSON do not delete DB rows.
//
// Configuration:
//   Writing:V2Seeder:Enabled â€” default true. Set false to disable in tests
//   that need an empty Writing V2 surface.
// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

public static class WritingV2ContentSeeder
{
    private const string SeedFolderRelative = "Data/Seeds/WritingV2";
    private const string ConfigEnabledKey = "Writing:V2Seeder:Enabled";
    // Demo/dummy authored tasks are opt-in only. Default OFF so production and
    // active-development databases stay free of placeholder content. Set
    // Writing:V2Seeder:SeedDemoTasks=true to restore the sample tasks.
    private const string ConfigSeedDemoTasksKey = "Writing:V2Seeder:SeedDemoTasks";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static async Task EnsureAsync(
        LearnerDbContext db,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var enabled = configuration.GetValue<bool?>(ConfigEnabledKey) ?? true;
        if (!enabled)
        {
            logger.LogInformation("WritingV2ContentSeeder: disabled via {Key}; skipping.", ConfigEnabledKey);
            return;
        }

        var folder = ResolveSeedFolder(environment);
        if (!Directory.Exists(folder))
        {
            logger.LogWarning("WritingV2ContentSeeder: seed folder not found at {Folder}; skipping.", folder);
            return;
        }

        try
        {
            await SeedCanonRulesAsync(db, folder, logger, cancellationToken);
            // 2026-05-27 audit fix â€” also materialise the canonical R* rules
            // from rulebooks/writing/{profession}/rulebook.v1.json. The legacy
            // SC-001..025 rows stay so existing references keep working; new
            // rows track the rulebook of record.
            await BackendRulebookCanonBridge.SeedFromRulebooksAsync(db, logger, cancellationToken);
            await SeedScenariosAsync(db, folder, isMockFile: false, fileName: "scenarios.diagnostic.json", logger, cancellationToken);
            await SeedLessonsAsync(db, folder, logger, cancellationToken);
            await SeedSentenceDrillsAsync(db, folder, logger, cancellationToken);
            await SeedCaseNoteDrillsAsync(db, folder, logger, cancellationToken);
            await SeedMocksAsync(db, folder, logger, cancellationToken);
            await SeedCommonMistakesAsync(db, folder, logger, cancellationToken);

            logger.LogInformation("WritingV2ContentSeeder: complete.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WritingV2ContentSeeder: failed; aborting Writing V2 content seed.");
            throw;
        }
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ 1. Canon rules â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    //
    // DEPRECATED 2026-05-27 â€” `canon-rules.launch-25.json` (25 SC-* rules)
    // remains for historical-FK compatibility but the source of truth has
    // moved to the rulebook JSONs at rulebooks/writing/{profession}/
    // rulebook.v1.json, materialised into the same WritingCanonRule table
    // by `BackendRulebookCanonBridge.SeedFromRulebooksAsync` (called next).
    // The SC-* IDs and the R* IDs share the table but never collide.
    //
    // To fully retire the SC-* seed: set `Writing:V2Seeder:Legacy:Disabled`
    // to true in appsettings once the rulebook bridge has run successfully
    // in production for at least one release cycle.
    private const string LegacyCanonDisabledKey = "Writing:V2Seeder:Legacy:Disabled";

    private static async Task SeedCanonRulesAsync(
        LearnerDbContext db, string folder, ILogger logger, CancellationToken ct)
    {
        var path = Path.Combine(folder, "canon-rules.launch-25.json");
        if (!File.Exists(path))
        {
            logger.LogWarning("WritingV2ContentSeeder: canon rules file missing at {Path}; skipping.", path);
            return;
        }

        var payload = await ReadJsonAsync<CanonRulesPayload>(path, ct);
        if (payload?.Rules is null || payload.Rules.Count == 0)
        {
            logger.LogWarning("WritingV2ContentSeeder: canon rules payload empty; skipping.");
            return;
        }

        var existingIds = await db.Set<WritingCanonRule>()
            .AsNoTracking()
            .Select(r => r.Id)
            .ToListAsync(ct);
        var existing = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        var added = 0;
        foreach (var rule in payload.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id)) continue;
            if (existing.Contains(rule.Id)) continue;

            db.Set<WritingCanonRule>().Add(new WritingCanonRule
            {
                Id = rule.Id,
                Category = rule.Category ?? "format",
                AppliesToLetterTypesJson = SerializeOrEmpty(rule.AppliesToLetterTypes),
                AppliesToProfessionsJson = SerializeOrEmpty(rule.AppliesToProfessions),
                Severity = NormaliseSeverity(rule.Severity),
                RuleText = rule.RuleText ?? string.Empty,
                CorrectExamplesJson = SerializeOrEmpty(rule.CorrectExamples),
                IncorrectExamplesJson = SerializeOrEmpty(rule.IncorrectExamples),
                DetectionType = NormaliseDetectionType(rule.DetectionType),
                DetectionConfigJson = rule.DetectionConfig is null
                    ? "{}"
                    : JsonSerializer.Serialize(rule.DetectionConfig),
                LessonId = rule.LessonId,
                Version = rule.Version > 0 ? rule.Version : 1,
                Active = rule.Active,
                CreatedAt = now,
                UpdatedAt = now,
            });
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        logger.LogInformation(
            "WritingV2ContentSeeder: canon rules â€” added {Added}, existing {Existing}, total in JSON {Total}.",
            added, existing.Count, payload.Rules.Count);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ 2. Diagnostic scenarios â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static async Task SeedScenariosAsync(
        LearnerDbContext db, string folder, bool isMockFile,
        string fileName, ILogger logger, CancellationToken ct)
    {
        var path = Path.Combine(folder, fileName);
        if (!File.Exists(path))
        {
            logger.LogWarning("WritingV2ContentSeeder: scenarios file missing at {Path}; skipping.", path);
            return;
        }

        var payload = await ReadJsonAsync<ScenariosPayload>(path, ct);
        if (payload?.Scenarios is null || payload.Scenarios.Count == 0)
        {
            logger.LogWarning("WritingV2ContentSeeder: scenarios payload empty in {File}; skipping.", fileName);
            return;
        }

        var existingIds = await db.Set<WritingScenario>()
            .AsNoTracking()
            .Where(s => payload.Scenarios.Select(p => p.Id).Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync(ct);
        // Track already-seen Ids in this batch too. A payload (or repeated
        // invocation that shares the change tracker) can carry the same
        // scenario Id twice; without this guard the second Add throws
        // "another instance with the same key value is already being tracked",
        // which aborts the whole Writing V2 seed at boot.
        var existing = new HashSet<Guid>(existingIds);

        var now = DateTimeOffset.UtcNow;
        var addedScenarios = 0;
        var addedSentences = 0;

        foreach (var sc in payload.Scenarios)
        {
            if (sc.Id == Guid.Empty) continue;
            if (!existing.Add(sc.Id)) continue;

            db.Set<WritingScenario>().Add(new WritingScenario
            {
                Id = sc.Id,
                Title = Truncate(sc.Title ?? "Untitled scenario", 200),
                LetterType = Truncate(sc.LetterType ?? "LT-RR", 8),
                Profession = Truncate(sc.Profession ?? "Medicine", 64),
                SubDiscipline = string.IsNullOrWhiteSpace(sc.SubDiscipline) ? null : Truncate(sc.SubDiscipline!, 64),
                TopicsJson = SerializeOrEmpty(sc.Topics),
                Difficulty = sc.Difficulty > 0 ? sc.Difficulty : 3,
                EstimatedReadingMinutes = sc.EstimatedReadingMinutes > 0 ? sc.EstimatedReadingMinutes : 5,
                IsDiagnostic = sc.IsDiagnostic,
                Status = Truncate(sc.Status ?? "published", 16),
                Version = sc.Version > 0 ? sc.Version : 1,
                PreviousVersionId = null,
                AuthorId = Truncate(sc.AuthorId ?? "system:seed", 64),
                ApprovedById = null,
                PublishedAt = (sc.Status?.Equals("published", StringComparison.OrdinalIgnoreCase) ?? false) ? now : null,
                CreatedAt = now,
            });
            addedScenarios++;

            if (sc.CaseNotesStructured is not null)
            {
                var ordinal = 0;
                foreach (var sentence in sc.CaseNotesStructured)
                {
                    ordinal++;
                    if (string.IsNullOrWhiteSpace(sentence.Sentence)) continue;
                    db.Set<WritingScenarioStructuredSentence>().Add(new WritingScenarioStructuredSentence
                    {
                        Id = DeterministicGuid(sc.Id, "sentence", ordinal),
                        ScenarioId = sc.Id,
                        Ordinal = ordinal,
                        SentenceText = sentence.Sentence!,
                        RelevanceLabel = NormaliseRelevance(sentence.Relevance),
                        Notes = null,
                        CreatedAt = now,
                    });
                    addedSentences++;
                }
            }
        }

        if (addedScenarios > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        logger.LogInformation(
            "WritingV2ContentSeeder: scenarios from {File} â€” added {Scenarios} scenarios + {Sentences} sentences.",
            fileName, addedScenarios, addedSentences);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ 4. Lessons â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static async Task SeedLessonsAsync(
        LearnerDbContext db, string folder, ILogger logger, CancellationToken ct)
    {
        var path = Path.Combine(folder, "lessons.json");
        if (!File.Exists(path)) return;

        var payload = await ReadJsonAsync<LessonsPayload>(path, ct);
        if (payload?.Lessons is null || payload.Lessons.Count == 0) return;

        var existingIds = await db.Set<WritingLessonV2>()
            .AsNoTracking()
            .Where(l => payload.Lessons.Select(p => p.Id).Contains(l.Id))
            .Select(l => l.Id)
            .ToListAsync(ct);
        var existing = new HashSet<Guid>(existingIds);

        var now = DateTimeOffset.UtcNow;
        var added = 0;
        foreach (var l in payload.Lessons)
        {
            if (l.Id == Guid.Empty) continue;
            if (existing.Contains(l.Id)) continue;

            db.Set<WritingLessonV2>().Add(new WritingLessonV2
            {
                Id = l.Id,
                SubSkill = Truncate(l.SubSkill ?? "W1", 4),
                OrderInCourse = l.OrderInCourse,
                Title = Truncate(l.Title ?? "Untitled lesson", 200),
                BodyMarkdown = l.BodyMarkdown ?? string.Empty,
                VideoUrl = string.IsNullOrWhiteSpace(l.VideoUrl) ? null : Truncate(l.VideoUrl!, 512),
                EstimatedMinutes = l.EstimatedMinutes > 0 ? l.EstimatedMinutes : 8,
                QuizQuestionsJson = l.QuizQuestions is null
                    ? "[]"
                    : JsonSerializer.Serialize(l.QuizQuestions),
                Status = Truncate(l.Status ?? "published", 16),
                CreatedAt = now,
            });
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        logger.LogInformation("WritingV2ContentSeeder: lessons â€” added {Added}.", added);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ 5. Sentence drills â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static async Task SeedSentenceDrillsAsync(
        LearnerDbContext db, string folder, ILogger logger, CancellationToken ct)
    {
        var path = Path.Combine(folder, "drills.sentence.json");
        if (!File.Exists(path)) return;

        var payload = await ReadJsonAsync<SentenceDrillsPayload>(path, ct);
        if (payload?.Drills is null || payload.Drills.Count == 0) return;

        var existingIds = await db.Set<WritingDrill>()
            .AsNoTracking()
            .Where(d => payload.Drills.Select(p => p.Id).Contains(d.Id))
            .Select(d => d.Id)
            .ToListAsync(ct);
        var existing = new HashSet<Guid>(existingIds);

        var now = DateTimeOffset.UtcNow;
        var added = 0;
        foreach (var d in payload.Drills)
        {
            if (d.Id == Guid.Empty) continue;
            if (existing.Contains(d.Id)) continue;

            db.Set<WritingDrill>().Add(new WritingDrill
            {
                Id = d.Id,
                DrillType = Truncate(d.DrillType ?? "opening-builder", 32),
                TargetSubSkill = Truncate(d.TargetSubSkill ?? "W2", 4),
                TargetCanonRuleId = string.IsNullOrWhiteSpace(d.TargetCanonRuleId) ? null : Truncate(d.TargetCanonRuleId!, 16),
                AppliesToProfessionsJson = SerializeOrEmpty(d.AppliesToProfessions),
                AppliesToLetterTypesJson = SerializeOrEmpty(d.AppliesToLetterTypes),
                Difficulty = d.Difficulty > 0 ? d.Difficulty : 1,
                PromptMarkdown = d.PromptMarkdown ?? string.Empty,
                ExpectedAnswer = d.ExpectedAnswer,
                AlternativesJson = SerializeOrEmpty(d.Alternatives),
                GradingMethod = Truncate(d.GradingMethod ?? "llm", 16),
                GradingConfigJson = d.GradingConfig is null ? "{}" : JsonSerializer.Serialize(d.GradingConfig),
                Status = Truncate(d.Status ?? "published", 16),
                CreatedAt = now,
            });
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        logger.LogInformation("WritingV2ContentSeeder: sentence drills â€” added {Added}.", added);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ 6. Case-note drills â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static async Task SeedCaseNoteDrillsAsync(
        LearnerDbContext db, string folder, ILogger logger, CancellationToken ct)
    {
        var path = Path.Combine(folder, "drills.case-notes.json");
        if (!File.Exists(path)) return;

        var payload = await ReadJsonAsync<CaseNoteDrillsPayload>(path, ct);
        if (payload?.Drills is null || payload.Drills.Count == 0) return;

        var existingIds = await db.Set<WritingCaseNoteDrill>()
            .AsNoTracking()
            .Where(d => payload.Drills.Select(p => p.Id).Contains(d.Id))
            .Select(d => d.Id)
            .ToListAsync(ct);
        var existing = new HashSet<Guid>(existingIds);

        var now = DateTimeOffset.UtcNow;
        var addedDrills = 0;
        var addedSentences = 0;
        foreach (var d in payload.Drills)
        {
            if (d.Id == Guid.Empty) continue;
            if (existing.Contains(d.Id)) continue;

            db.Set<WritingCaseNoteDrill>().Add(new WritingCaseNoteDrill
            {
                Id = d.Id,
                Title = Truncate(d.Title ?? "Case-note drill", 200),
                Profession = Truncate(d.Profession ?? "Medicine", 64),
                LetterType = Truncate(d.LetterType ?? "LT-RR", 8),
                Format = Truncate(d.Format ?? "highlight-relevant", 32),
                CaseNotesMarkdown = d.CaseNotesMarkdown ?? string.Empty,
                Difficulty = d.Difficulty > 0 ? d.Difficulty : 2,
                Status = Truncate(d.Status ?? "published", 16),
                CreatedAt = now,
            });
            addedDrills++;

            if (d.CaseNotesStructured is not null)
            {
                foreach (var s in d.CaseNotesStructured)
                {
                    if (string.IsNullOrWhiteSpace(s.SentenceText)) continue;
                    db.Set<WritingCaseNoteDrillSentence>().Add(new WritingCaseNoteDrillSentence
                    {
                        Id = DeterministicGuid(d.Id, "case-sentence", s.Ordinal),
                        DrillId = d.Id,
                        Ordinal = s.Ordinal,
                        SentenceText = s.SentenceText!,
                        RelevanceLabel = NormaliseRelevance(s.RelevanceLabel),
                        Rationale = string.IsNullOrWhiteSpace(s.Rationale) ? null : Truncate(s.Rationale!, 500),
                    });
                    addedSentences++;
                }
            }
        }

        if (addedDrills > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        logger.LogInformation(
            "WritingV2ContentSeeder: case-note drills â€” added {Drills} drills + {Sentences} sentences.",
            addedDrills, addedSentences);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ 7. Mocks â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static async Task SeedMocksAsync(
        LearnerDbContext db, string folder, ILogger logger, CancellationToken ct)
    {
        var path = Path.Combine(folder, "mocks.json");
        if (!File.Exists(path)) return;

        // mocks.json carries both scenarios (prerequisite) and mocks.
        await SeedScenariosAsync(db, folder, isMockFile: true, fileName: "mocks.json", logger, ct);

        var payload = await ReadJsonAsync<MocksPayload>(path, ct);
        if (payload?.Mocks is null || payload.Mocks.Count == 0) return;

        var existingIds = await db.Set<WritingMock>()
            .AsNoTracking()
            .Where(m => payload.Mocks.Select(p => p.Id).Contains(m.Id))
            .Select(m => m.Id)
            .ToListAsync(ct);
        var existing = new HashSet<Guid>(existingIds);

        var now = DateTimeOffset.UtcNow;
        var added = 0;
        foreach (var m in payload.Mocks)
        {
            if (m.Id == Guid.Empty) continue;
            if (existing.Contains(m.Id)) continue;

            db.Set<WritingMock>().Add(new WritingMock
            {
                Id = m.Id,
                ScenarioId = m.ScenarioId,
                Title = Truncate(m.Title ?? "Mock", 200),
                Difficulty = m.Difficulty > 0 ? m.Difficulty : 4,
                Status = Truncate(m.Status ?? "published", 16),
                CreatedAt = now,
            });
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        logger.LogInformation("WritingV2ContentSeeder: mocks â€” added {Added}.", added);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ 8. Common mistakes â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static async Task SeedCommonMistakesAsync(
        LearnerDbContext db, string folder, ILogger logger, CancellationToken ct)
    {
        var path = Path.Combine(folder, "common-mistakes.json");
        if (!File.Exists(path)) return;

        var payload = await ReadJsonAsync<CommonMistakesPayload>(path, ct);
        if (payload?.Mistakes is null || payload.Mistakes.Count == 0) return;

        var existingIds = await db.Set<WritingCommonMistake>()
            .AsNoTracking()
            .Where(m => payload.Mistakes.Select(p => p.Id).Contains(m.Id))
            .Select(m => m.Id)
            .ToListAsync(ct);
        var existing = new HashSet<Guid>(existingIds);

        var now = DateTimeOffset.UtcNow;
        var added = 0;
        foreach (var m in payload.Mistakes)
        {
            if (m.Id == Guid.Empty) continue;
            if (existing.Contains(m.Id)) continue;

            db.Set<WritingCommonMistake>().Add(new WritingCommonMistake
            {
                Id = m.Id,
                Category = Truncate(m.Category ?? "style", 64),
                Summary = Truncate(m.Summary ?? string.Empty, 500),
                ExampleWrong = Truncate(m.ExampleWrong ?? string.Empty, 1000),
                ExampleRight = Truncate(m.ExampleRight ?? string.Empty, 1000),
                CanonRuleId = string.IsNullOrWhiteSpace(m.CanonRuleId) ? null : Truncate(m.CanonRuleId!, 16),
                RelatedSubSkill = string.IsNullOrWhiteSpace(m.RelatedSubSkill) ? null : Truncate(m.RelatedSubSkill!, 4),
                CreatedAt = now,
            });
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        logger.LogInformation("WritingV2ContentSeeder: common mistakes â€” added {Added}.", added);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static string ResolveSeedFolder(IWebHostEnvironment env)
    {
        var candidates = new[]
        {
            Path.Combine(env.ContentRootPath, SeedFolderRelative),
            Path.Combine(AppContext.BaseDirectory, SeedFolderRelative),
        };
        return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken ct) where T : class
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
    }

    private static string SerializeOrEmpty<T>(IEnumerable<T>? values)
        => values is null ? "[]" : JsonSerializer.Serialize(values);

    private static string NormaliseSeverity(string? severity)
    {
        var s = (severity ?? "medium").Trim().ToLowerInvariant();
        return s is "high" or "medium" or "low" ? s : "medium";
    }

    private static string NormaliseDetectionType(string? detectionType)
    {
        var s = (detectionType ?? "regex").Trim().ToLowerInvariant();
        return s is "regex" or "llm" or "structural" ? s : "regex";
    }

    private static string NormaliseRelevance(string? label)
    {
        var s = (label ?? "relevant").Trim().ToLowerInvariant();
        return s is "relevant" or "maybe" or "irrelevant" ? s : "relevant";
    }

    private static string Truncate(string value, int max)
        => string.IsNullOrEmpty(value) ? value : (value.Length <= max ? value : value[..max]);

    // Generates a deterministic GUID from a parent GUID + tag + ordinal so
    // child rows (annotations, sentences) keep stable ids across machines.
    private static Guid DeterministicGuid(Guid parent, string tag, int ordinal)
        => DeterministicGuid(parent, tag, ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static Guid DeterministicGuid(Guid parent, string tag, string key)
    {
        var seed = $"{parent:D}|{tag}|{key}";
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(seed));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        return new Guid(bytes);
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€ JSON DTOs (internal only) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private sealed class CanonRulesPayload
    {
        public int SchemaVersion { get; set; } = 1;
        public int Version { get; set; } = 1;
        public string? Source { get; set; }
        public List<CanonRuleDto>? Rules { get; set; }
    }

    private sealed class CanonRuleDto
    {
        public string? Id { get; set; }
        public string? Category { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("applies_to_letter_types")]
        public List<string>? AppliesToLetterTypes { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("applies_to_professions")]
        public List<string>? AppliesToProfessions { get; set; }
        public string? Severity { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("rule_text")]
        public string? RuleText { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("correct_examples")]
        public List<string>? CorrectExamples { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("incorrect_examples")]
        public List<string>? IncorrectExamples { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("detection_type")]
        public string? DetectionType { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("detection_config")]
        public JsonElement? DetectionConfig { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("lesson_id")]
        public Guid? LessonId { get; set; }
        public int Version { get; set; } = 1;
        public bool Active { get; set; } = true;
    }

    private sealed class ScenariosPayload
    {
        public int SchemaVersion { get; set; } = 1;
        public int Version { get; set; } = 1;
        public string? Source { get; set; }
        public List<ScenarioDto>? Scenarios { get; set; }
    }

    private sealed class ScenarioDto
    {
        public Guid Id { get; set; }
        public string? Title { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("letter_type")]
        public string? LetterType { get; set; }
        public string? Profession { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("sub_discipline")]
        public string? SubDiscipline { get; set; }
        public List<string>? Topics { get; set; }
        public int Difficulty { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("case_notes_markdown")]
        public string? CaseNotesMarkdown { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("case_notes_structured")]
        public List<ScenarioSentenceDto>? CaseNotesStructured { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("estimated_reading_minutes")]
        public int EstimatedReadingMinutes { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("is_diagnostic")]
        public bool IsDiagnostic { get; set; }
        public string? Status { get; set; }
        public int Version { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("author_id")]
        public string? AuthorId { get; set; }
    }

    private sealed class ScenarioSentenceDto
    {
        public string? Sentence { get; set; }
        public string? Relevance { get; set; }
    }

    private sealed class LessonsPayload
    {
        public int SchemaVersion { get; set; } = 1;
        public int Version { get; set; } = 1;
        public string? Source { get; set; }
        public List<LessonDto>? Lessons { get; set; }
    }

    private sealed class LessonDto
    {
        public Guid Id { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("sub_skill")]
        public string? SubSkill { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("order_in_course")]
        public int OrderInCourse { get; set; }
        public string? Title { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("body_markdown")]
        public string? BodyMarkdown { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("video_url")]
        public string? VideoUrl { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("estimated_minutes")]
        public int EstimatedMinutes { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("quiz_questions_json")]
        public JsonElement? QuizQuestions { get; set; }
        public string? Status { get; set; }
    }

    private sealed class SentenceDrillsPayload
    {
        public int SchemaVersion { get; set; } = 1;
        public int Version { get; set; } = 1;
        public string? Source { get; set; }
        public List<SentenceDrillDto>? Drills { get; set; }
    }

    private sealed class SentenceDrillDto
    {
        public Guid Id { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("drill_type")]
        public string? DrillType { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("target_sub_skill")]
        public string? TargetSubSkill { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("target_canon_rule_id")]
        public string? TargetCanonRuleId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("applies_to_professions")]
        public List<string>? AppliesToProfessions { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("applies_to_letter_types")]
        public List<string>? AppliesToLetterTypes { get; set; }
        public int Difficulty { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("prompt_markdown")]
        public string? PromptMarkdown { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("expected_answer")]
        public string? ExpectedAnswer { get; set; }
        public List<string>? Alternatives { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("grading_method")]
        public string? GradingMethod { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("grading_config")]
        public JsonElement? GradingConfig { get; set; }
        public string? Status { get; set; }
    }

    private sealed class CaseNoteDrillsPayload
    {
        public int SchemaVersion { get; set; } = 1;
        public int Version { get; set; } = 1;
        public string? Source { get; set; }
        public List<CaseNoteDrillDto>? Drills { get; set; }
    }

    private sealed class CaseNoteDrillDto
    {
        public Guid Id { get; set; }
        public string? Title { get; set; }
        public string? Profession { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("letter_type")]
        public string? LetterType { get; set; }
        public string? Format { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("scenario_id")]
        public Guid? ScenarioId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("case_notes_markdown")]
        public string? CaseNotesMarkdown { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("case_notes_structured")]
        public List<CaseNoteSentenceDto>? CaseNotesStructured { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("expected_answer_label")]
        public string? ExpectedAnswerLabel { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("expected_answer_explanation")]
        public string? ExpectedAnswerExplanation { get; set; }
        public int Difficulty { get; set; }
        public string? Status { get; set; }
    }

    private sealed class CaseNoteSentenceDto
    {
        public int Ordinal { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("sentence_text")]
        public string? SentenceText { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("relevance_label")]
        public string? RelevanceLabel { get; set; }
        public string? Rationale { get; set; }
    }

    private sealed class MocksPayload
    {
        public int SchemaVersion { get; set; } = 1;
        public int Version { get; set; } = 1;
        public string? Source { get; set; }
        public List<ScenarioDto>? Scenarios { get; set; }
        public List<MockDto>? Mocks { get; set; }
    }

    private sealed class MockDto
    {
        public Guid Id { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("scenario_id")]
        public Guid ScenarioId { get; set; }
        public string? Title { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("target_minutes")]
        public int TargetMinutes { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("reading_phase_minutes")]
        public int ReadingPhaseMinutes { get; set; }
        public int Difficulty { get; set; }
        public string? Status { get; set; }
    }

    private sealed class CommonMistakesPayload
    {
        public int SchemaVersion { get; set; } = 1;
        public int Version { get; set; } = 1;
        public string? Source { get; set; }
        public List<CommonMistakeDto>? Mistakes { get; set; }
    }

    private sealed class CommonMistakeDto
    {
        public Guid Id { get; set; }
        public string? Category { get; set; }
        public string? Summary { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("example_wrong")]
        public string? ExampleWrong { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("example_right")]
        public string? ExampleRight { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("canon_rule_id")]
        public string? CanonRuleId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("related_sub_skill")]
        public string? RelatedSubSkill { get; set; }
    }
}
