using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Writing;

// ═════════════════════════════════════════════════════════════════════════════
// WritingV2ContentSeeder — OET Writing Module V2 launch content.
//
// Loads the 8 versioned JSON files in Data/Seeds/WritingV2/ on boot and
// idempotently materialises them into the relational Writing V2 tables.
//
//   1. canon-rules.launch-25.json    → WritingCanonRules (25 rules SC-001..025)
//   2. scenarios.diagnostic.json     → WritingScenarios + WritingScenarioStructuredSentences (12)
//   3. exemplars.json                → WritingExemplars + WritingExemplarAnnotations (6)
//   4. lessons.json                  → WritingLessonsV2 (16, 2 per W1-W8)
//   5. drills.sentence.json          → WritingDrills (30, 3 per drill type × 10)
//   6. drills.case-notes.json        → WritingCaseNoteDrills + WritingCaseNoteDrillSentences (12)
//   7. mocks.json                    → mock scenarios first, then WritingMocks (6 + 6)
//   8. common-mistakes.json          → WritingCommonMistakes (20)
//
// Seed order: canon → scenarios → exemplars → lessons → drills → case-note
// drills → mocks (with their scenarios first) → common mistakes.
//
// Embeddings (text-embedding-3-small) are NOT generated at seed time — they
// are lazily populated by the WritingExemplarReindexCron tick (WS5).
//
// Idempotency contract:
//   • Per-table existence check via Id — re-runs are no-ops once seeded.
//   • Deterministic GUIDs in JSON keep ids stable across machines.
//   • If a partial seed previously ran (e.g., container restart), only the
//     missing rows are added; existing rows are not mutated.
//   • Never deletes. Removed rows in JSON do not delete DB rows.
//
// Configuration:
//   Writing:V2Seeder:Enabled — default true. Set false to disable in tests
//   that need an empty Writing V2 surface.
// ═════════════════════════════════════════════════════════════════════════════

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
            // 2026-05-27 audit fix — also materialise the canonical R* rules
            // from rulebooks/writing/{profession}/rulebook.v1.json. The legacy
            // SC-001..025 rows stay so existing references keep working; new
            // rows track the rulebook of record.
            await BackendRulebookCanonBridge.SeedFromRulebooksAsync(db, logger, cancellationToken);
            await SeedScenariosAsync(db, folder, isMockFile: false, fileName: "scenarios.diagnostic.json", logger, cancellationToken);
            await SeedExemplarsAsync(db, folder, logger, cancellationToken);
            await SeedLessonsAsync(db, folder, logger, cancellationToken);
            await SeedSentenceDrillsAsync(db, folder, logger, cancellationToken);
            await SeedCaseNoteDrillsAsync(db, folder, logger, cancellationToken);
            await SeedMocksAsync(db, folder, logger, cancellationToken);
            await SeedCommonMistakesAsync(db, folder, logger, cancellationToken);

            // 2026-05-31 — exam-faithful authored demo TASKS (spec §4/§5/§6). These are
            // fully-structured WritingScenarios (recipient, model answer exemplar, key +
            // irrelevant content checklists) the admin Task Builder + learner attempt flow
            // can exercise out of the box. Original content only; idempotent by InternalCode.
            // Opt-in only (default OFF) — these are placeholder/demo tasks, not real content.
            if (configuration.GetValue<bool?>(ConfigSeedDemoTasksKey) ?? false)
            {
                await SeedDemoWritingTasksAsync(db, logger, cancellationToken);
            }

            logger.LogInformation("WritingV2ContentSeeder: complete.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WritingV2ContentSeeder: failed; aborting Writing V2 content seed.");
            throw;
        }
    }

    // ───────────────────────── 1. Canon rules ──────────────────────────────
    //
    // DEPRECATED 2026-05-27 — `canon-rules.launch-25.json` (25 SC-* rules)
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
            "WritingV2ContentSeeder: canon rules — added {Added}, existing {Existing}, total in JSON {Total}.",
            added, existing.Count, payload.Rules.Count);
    }

    // ──────────────────────── 2. Diagnostic scenarios ──────────────────────

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
        var existing = new HashSet<Guid>(existingIds);

        var now = DateTimeOffset.UtcNow;
        var addedScenarios = 0;
        var addedSentences = 0;

        foreach (var sc in payload.Scenarios)
        {
            if (sc.Id == Guid.Empty) continue;
            if (existing.Contains(sc.Id)) continue;

            db.Set<WritingScenario>().Add(new WritingScenario
            {
                Id = sc.Id,
                Title = Truncate(sc.Title ?? "Untitled scenario", 200),
                LetterType = Truncate(sc.LetterType ?? "LT-RR", 8),
                Profession = Truncate(sc.Profession ?? "Medicine", 64),
                SubDiscipline = string.IsNullOrWhiteSpace(sc.SubDiscipline) ? null : Truncate(sc.SubDiscipline!, 64),
                TopicsJson = SerializeOrEmpty(sc.Topics),
                Difficulty = sc.Difficulty > 0 ? sc.Difficulty : 3,
                CaseNotesMarkdown = sc.CaseNotesMarkdown ?? string.Empty,
                CaseNotesStructuredJson = sc.CaseNotesStructured is null
                    ? null
                    : JsonSerializer.Serialize(sc.CaseNotesStructured),
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
            "WritingV2ContentSeeder: scenarios from {File} — added {Scenarios} scenarios + {Sentences} sentences.",
            fileName, addedScenarios, addedSentences);
    }

    // ─────────────────────────── 3. Exemplars ──────────────────────────────

    private static async Task SeedExemplarsAsync(
        LearnerDbContext db, string folder, ILogger logger, CancellationToken ct)
    {
        var path = Path.Combine(folder, "exemplars.json");
        if (!File.Exists(path))
        {
            logger.LogWarning("WritingV2ContentSeeder: exemplars file missing at {Path}; skipping.", path);
            return;
        }

        var payload = await ReadJsonAsync<ExemplarsPayload>(path, ct);
        if (payload?.Exemplars is null || payload.Exemplars.Count == 0) return;

        var existingIds = await db.Set<WritingExemplar>()
            .AsNoTracking()
            .Where(e => payload.Exemplars.Select(p => p.Id).Contains(e.Id))
            .Select(e => e.Id)
            .ToListAsync(ct);
        var existing = new HashSet<Guid>(existingIds);

        var now = DateTimeOffset.UtcNow;
        var addedExemplars = 0;
        var addedAnnotations = 0;

        foreach (var ex in payload.Exemplars)
        {
            if (ex.Id == Guid.Empty) continue;
            if (existing.Contains(ex.Id)) continue;

            db.Set<WritingExemplar>().Add(new WritingExemplar
            {
                Id = ex.Id,
                ScenarioId = ex.ScenarioId,
                LetterType = Truncate(ex.LetterType ?? "LT-RR", 8),
                Profession = Truncate(ex.Profession ?? "Medicine", 64),
                LetterContent = ex.LetterContent ?? string.Empty,
                AnnotationsJson = ex.Annotations is null
                    ? "[]"
                    : JsonSerializer.Serialize(ex.Annotations),
                TargetBand = Truncate(ex.TargetBand ?? "A", 8),
                Status = Truncate(ex.Status ?? "published", 16),
                AuthorId = Truncate(ex.AuthorId ?? "system:seed", 64),
                PublishedAt = (ex.Status?.Equals("published", StringComparison.OrdinalIgnoreCase) ?? false) ? now : null,
                CreatedAt = now,
            });
            addedExemplars++;

            if (ex.Annotations is not null)
            {
                var ordinal = 0;
                foreach (var a in ex.Annotations)
                {
                    ordinal++;
                    if (string.IsNullOrWhiteSpace(a.Note)) continue;
                    db.Set<WritingExemplarAnnotation>().Add(new WritingExemplarAnnotation
                    {
                        Id = DeterministicGuid(ex.Id, "annotation", ordinal),
                        ExemplarId = ex.Id,
                        Ordinal = ordinal,
                        CharStart = a.CharStart,
                        CharEnd = a.CharEnd,
                        AnnotationType = string.IsNullOrWhiteSpace(a.RuleId) ? "note" : "rule",
                        RuleId = string.IsNullOrWhiteSpace(a.RuleId) ? null : Truncate(a.RuleId!, 16),
                        Note = Truncate(a.Note!, 1000),
                        CreatedAt = now,
                    });
                    addedAnnotations++;
                }
            }
        }

        if (addedExemplars > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        logger.LogInformation(
            "WritingV2ContentSeeder: exemplars — added {Exemplars} exemplars + {Annotations} annotations.",
            addedExemplars, addedAnnotations);
    }

    // ─────────────────────────── 4. Lessons ────────────────────────────────

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
        logger.LogInformation("WritingV2ContentSeeder: lessons — added {Added}.", added);
    }

    // ─────────────────────── 5. Sentence drills ────────────────────────────

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
        logger.LogInformation("WritingV2ContentSeeder: sentence drills — added {Added}.", added);
    }

    // ────────────────────── 6. Case-note drills ────────────────────────────

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
            "WritingV2ContentSeeder: case-note drills — added {Drills} drills + {Sentences} sentences.",
            addedDrills, addedSentences);
    }

    // ─────────────────────────── 7. Mocks ──────────────────────────────────

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
        logger.LogInformation("WritingV2ContentSeeder: mocks — added {Added}.", added);
    }

    // ───────────────────── 8. Common mistakes ──────────────────────────────

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
        logger.LogInformation("WritingV2ContentSeeder: common mistakes — added {Added}.", added);
    }

    // ─────────────────── 9. Exam-faithful authored demo TASKS ──────────────
    //
    // Seeds 3 COMPLETE WritingScenarios (the enriched exam-closure task shape:
    // recipient, model-answer exemplar, key/irrelevant content checklists, fixed
    // instructions, word guide, simulation modes). Each is idempotent on its
    // InternalCode — if a scenario with that code already exists, it is skipped.
    // ORIGINAL content only (no real OET exam material).
    private static async Task SeedDemoWritingTasksAsync(
        LearnerDbContext db, ILogger logger, CancellationToken ct)
    {
        var specs = BuildDemoTaskSpecs();
        var codes = specs.Select(s => s.InternalCode).ToList();

        var existingCodes = await db.Set<WritingScenario>()
            .AsNoTracking()
            .Where(s => s.InternalCode != null && codes.Contains(s.InternalCode))
            .Select(s => s.InternalCode!)
            .ToListAsync(ct);
        var existing = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        var addedTasks = 0;
        var addedExemplars = 0;
        var addedChecklist = 0;

        foreach (var spec in specs)
        {
            if (existing.Contains(spec.InternalCode))
            {
                continue; // idempotency guard — already seeded.
            }

            // Deterministic ids so re-running across machines keeps rows stable.
            var scenarioId = DeterministicGuid(DemoTaskNamespace, "scenario", spec.InternalCode);
            var exemplarId = DeterministicGuid(DemoTaskNamespace, "exemplar", spec.InternalCode);

            // Linked ORIGINAL model-answer exemplar (180–200-word letter).
            db.Set<WritingExemplar>().Add(new WritingExemplar
            {
                Id = exemplarId,
                ScenarioId = scenarioId,
                LetterType = Truncate(spec.LetterType, 8),
                Profession = Truncate(spec.Profession, 64),
                LetterContent = spec.ModelAnswer,
                AnnotationsJson = "[]",
                TargetBand = "A",
                Status = "published",
                AuthorId = "seed",
                PublishedAt = now,
                CreatedAt = now,
            });
            addedExemplars++;

            db.Set<WritingScenario>().Add(new WritingScenario
            {
                Id = scenarioId,
                Title = Truncate(spec.Title, 200),
                LetterType = Truncate(spec.LetterType, 8),
                Profession = Truncate(spec.Profession, 64),
                TopicsJson = "[]",
                Difficulty = spec.Difficulty,
                CaseNotesMarkdown = FlattenCaseNotes(spec.CaseNoteSections),
                CaseNoteSectionsJson = JsonSerializer.Serialize(spec.CaseNoteSections, JsonOptions),
                EstimatedReadingMinutes = 5,
                IsDiagnostic = false,
                Status = "published",
                Version = 1,
                AuthorId = "seed",
                ContentOwnerId = "seed",
                PublishedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                InternalCode = spec.InternalCode,
                TaskPromptMarkdown = spec.TaskPrompt,
                WriterRole = Truncate(spec.WriterRole, 256),
                TodayDate = Truncate(spec.TodayDate, 64),
                RecipientJson = JsonSerializer.Serialize(spec.Recipient, JsonOptions),
                ExpectedPurpose = spec.ExpectedPurpose,
                ExpectedAction = spec.ExpectedAction,
                FixedInstructionsJson = JsonSerializer.Serialize(StandardFixedInstructions, JsonOptions),
                WordGuideMin = 180,
                WordGuideMax = 200,
                ReadingTimeSeconds = 300,
                WritingTimeSeconds = 2400,
                SimulationModes = "both",
                MarkingMode = "tutor",
                ModelAnswerExemplarId = exemplarId,
                SourceProvenance = "Original demo content authored in-house for the OET Writing module.",
                IntegrityAcknowledgedById = "seed",
                IntegrityAcknowledgedAt = now,
            });
            addedTasks++;

            var ordinal = 0;
            foreach (var item in spec.Checklist)
            {
                db.Set<WritingContentChecklistItem>().Add(new WritingContentChecklistItem
                {
                    Id = DeterministicGuid(scenarioId, "checklist", ordinal),
                    ScenarioId = scenarioId,
                    ItemText = Truncate(item.ItemText, 500),
                    Category = Truncate(item.Category, 64),
                    Importance = item.Importance,
                    RequiredStatus = item.RequiredStatus,
                    LinkedCaseNoteSection = item.LinkedCaseNoteSection is null ? null : Truncate(item.LinkedCaseNoteSection, 200),
                    ExpectedRepresentation = item.ExpectedRepresentation is null ? null : Truncate(item.ExpectedRepresentation, 1000),
                    CommonError = item.CommonError is null ? null : Truncate(item.CommonError, 1000),
                    Ordinal = ordinal,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                ordinal++;
                addedChecklist++;
            }
        }

        if (addedTasks > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        logger.LogInformation(
            "WritingV2ContentSeeder: demo writing tasks — added {Tasks} tasks + {Exemplars} model answers + {Checklist} checklist items (existing {Existing}).",
            addedTasks, addedExemplars, addedChecklist, existing.Count);
    }

    // Stable namespace seed for the demo tasks' deterministic GUIDs.
    private static readonly Guid DemoTaskNamespace = new("d3a9f17c-2b4e-4c1a-9f6d-8e0c5b2a7e10");

    // The 4 standard OET writing-task instruction lines (spec §5.2).
    private static readonly string[] StandardFixedInstructions =
    {
        "Expand the relevant notes into complete sentences",
        "Do not use note form",
        "Use letter format",
        "The body of the letter should be approximately 180–200 words",
    };

    private static string FlattenCaseNotes(IReadOnlyList<DemoCaseNoteSection> sections)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var section in sections)
        {
            sb.Append("## ").AppendLine(section.Heading);
            foreach (var item in section.Items)
            {
                sb.Append("- ").AppendLine(item);
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static List<DemoTaskSpec> BuildDemoTaskSpecs() => new()
    {
        // ── 1. Medicine — routine referral ──────────────────────────────────
        new DemoTaskSpec
        {
            InternalCode = "MED-WR-S01",
            Title = "Routine referral: Mr Harold Bennett (hypertension review)",
            Profession = "Medicine",
            LetterType = "routine_referral",
            Difficulty = 3,
            WriterRole = "You are a general practitioner at Riverside Family Practice.",
            TodayDate = "14 March 2026",
            TaskPrompt = "Using the information in the case notes, write a referral letter to the cardiologist requesting assessment and ongoing management of Mr Bennett's poorly controlled hypertension.",
            ExpectedPurpose = "Refer the patient for specialist cardiology assessment of poorly controlled hypertension despite treatment.",
            ExpectedAction = "Request specialist review, investigation of secondary causes, and advice on optimising antihypertensive therapy.",
            Recipient = new DemoRecipient
            {
                Name = "Dr Amelia Cross",
                Role = "Consultant Cardiologist",
                Organisation = "Riverside General Hospital",
                Address = "Department of Cardiology, 22 Parkland Avenue, Riverside",
            },
            CaseNoteSections = new List<DemoCaseNoteSection>
            {
                new("Patient details", new List<string>
                {
                    "Harold Bennett, 58 years old, male",
                    "Retired schoolteacher; lives with wife",
                    "Non-smoker; alcohol 6 units/week",
                }),
                new("Relevant history", new List<string>
                {
                    "Hypertension diagnosed 2019",
                    "Type 2 diabetes (diet-controlled) since 2021",
                    "Father: myocardial infarction aged 60",
                    "No known drug allergies",
                }),
                new("Presenting complaint", new List<string>
                {
                    "3-month history of occasional morning headaches",
                    "Reports good medication adherence",
                    "Denies chest pain, breathlessness or palpitations",
                }),
                new("Examination/findings", new List<string>
                {
                    "BP 168/98 mmHg (repeated 164/96)",
                    "BMI 29; pulse 78 regular",
                    "Urinalysis: trace protein",
                    "Recent bloods: eGFR 74, normal electrolytes",
                }),
                new("Management/plan", new List<string>
                {
                    "Current: amlodipine 10 mg daily, ramipril 10 mg daily",
                    "BP remains above target despite dual therapy",
                    "Referral for specialist assessment and secondary-cause screening",
                }),
            },
            ModelAnswer =
                "Dear Dr Cross,\n\n" +
                "Re: Mr Harold Bennett, 58 years old\n\n" +
                "I am writing to refer Mr Bennett, a 58-year-old retired schoolteacher, for specialist assessment of his poorly controlled hypertension.\n\n" +
                "Mr Bennett was diagnosed with hypertension in 2019 and also has diet-controlled type 2 diabetes. His father suffered a myocardial infarction at the age of 60. He has presented over the past three months with occasional morning headaches, although he denies chest pain, breathlessness or palpitations and reports good adherence to his medication.\n\n" +
                "On examination, his blood pressure was 168/98 mmHg, confirmed on repeat measurement. His BMI is 29 and urinalysis revealed trace protein. Recent blood tests showed an eGFR of 74 with normal electrolytes. Despite treatment with amlodipine 10 mg and ramipril 10 mg daily, his blood pressure remains above target.\n\n" +
                "I would be grateful for your assessment, including screening for secondary causes, and your advice on optimising his treatment.\n\n" +
                "Yours sincerely,\nDr Riverside",
            Checklist = new List<DemoChecklistItem>
            {
                new("Patient name and age (Mr Bennett, 58)", "patient_identity", "high", "required", "Patient details", "Mr Harold Bennett, a 58-year-old man", "Omitting the patient's age."),
                new("Purpose: referral for specialist assessment of hypertension", "main_reason", "high", "required", "Management/plan", "I am writing to refer ... for assessment of poorly controlled hypertension", "Failing to state the reason for referral up front."),
                new("Poorly controlled BP despite dual therapy", "diagnosis", "high", "required", "Examination/findings", "BP 168/98 despite amlodipine and ramipril", "Listing the BP reading without noting it is uncontrolled."),
                new("Current antihypertensive medication", "medication", "high", "required", "Management/plan", "amlodipine 10 mg and ramipril 10 mg daily", "Omitting current doses."),
                new("Relevant history (diabetes, family history of MI)", "history", "medium", "required", "Relevant history", "diet-controlled type 2 diabetes; father had an MI at 60", "Leaving out cardiovascular risk factors."),
                new("Request for specialist review / secondary-cause screening", "follow_up", "medium", "required", "Management/plan", "I would be grateful for your assessment and advice", "Not making a clear request of the specialist."),
                new("Patient's hobby of gardening", "social", "low", "irrelevant", null, null, "Including pastimes that do not affect management."),
                new("Wife's recent knee surgery", "social", "low", "irrelevant", null, null, "Adding family members' unrelated medical details."),
                new("Childhood appendicectomy aged 12", "history", "low", "irrelevant", null, null, "Including remote history irrelevant to the referral."),
            },
        },

        // ── 2. Nursing — discharge / update ─────────────────────────────────
        new DemoTaskSpec
        {
            InternalCode = "NUR-WR-S01",
            Title = "Discharge update: Mrs Edith Palmer (post-operative care)",
            Profession = "Nursing",
            LetterType = "update_discharge",
            Difficulty = 3,
            WriterRole = "You are the registered nurse coordinating discharge on the surgical ward at Meadowbrook Hospital.",
            TodayDate = "9 May 2026",
            TaskPrompt = "Using the information in the case notes, write a letter to the community nurse who will take over Mrs Palmer's care at home following her discharge.",
            ExpectedPurpose = "Hand over post-operative community nursing care for a patient discharged after hip replacement.",
            ExpectedAction = "Request wound care, mobility support and monitoring during the patient's recovery at home.",
            Recipient = new DemoRecipient
            {
                Name = "Ms Sarah Donnelly",
                Role = "Community Nurse",
                Organisation = "Meadowbrook Community Nursing Service",
                Address = "8 Elm Court, Meadowbrook",
            },
            CaseNoteSections = new List<DemoCaseNoteSection>
            {
                new("Patient details", new List<string>
                {
                    "Edith Palmer, 74 years old, female",
                    "Lives alone in a ground-floor flat",
                    "Daughter visits daily; previously independent",
                }),
                new("Relevant history", new List<string>
                {
                    "Osteoarthritis; hypertension",
                    "Left total hip replacement on 2 May 2026",
                    "Allergic to penicillin (rash)",
                }),
                new("Presenting complaint", new List<string>
                {
                    "Admitted electively for hip replacement",
                    "Uncomplicated post-operative recovery",
                    "Pain well controlled on oral analgesia",
                }),
                new("Examination/findings", new List<string>
                {
                    "Surgical wound clean and dry; staples in situ",
                    "Mobilising short distances with a frame",
                    "Afebrile; observations stable",
                }),
                new("Management/plan", new List<string>
                {
                    "Remove staples on 16 May 2026 (day 14)",
                    "Continue paracetamol and prescribed analgesia",
                    "Daily wound check; encourage prescribed exercises",
                    "Monitor for signs of infection or DVT",
                }),
            },
            ModelAnswer =
                "Dear Ms Donnelly,\n\n" +
                "Re: Mrs Edith Palmer, 74 years old\n\n" +
                "I am writing to hand over the care of Mrs Palmer, who is being discharged home today following a left total hip replacement performed on 2 May 2026.\n\n" +
                "Mrs Palmer is a 74-year-old woman who lives alone in a ground-floor flat, with daily support from her daughter. She has a history of osteoarthritis and hypertension, and she is allergic to penicillin. Her post-operative recovery has been uncomplicated and her pain is well controlled on oral analgesia.\n\n" +
                "On discharge, her surgical wound is clean and dry with staples in situ, and she is mobilising short distances with a frame. She remains afebrile with stable observations.\n\n" +
                "I would be grateful if you could review her wound daily, remove the staples on 16 May, and encourage her prescribed exercises. Please also monitor her for any signs of wound infection or deep vein thrombosis.\n\n" +
                "Yours sincerely,\nWard Nurse",
            Checklist = new List<DemoChecklistItem>
            {
                new("Patient name and age (Mrs Palmer, 74)", "patient_identity", "high", "required", "Patient details", "Mrs Edith Palmer, a 74-year-old woman", "Omitting the patient's age."),
                new("Purpose: handover of post-discharge community care", "main_reason", "high", "required", "Presenting complaint", "I am writing to hand over the care of Mrs Palmer following discharge", "Not stating the handover purpose clearly."),
                new("Recent hip replacement and uncomplicated recovery", "diagnosis", "high", "required", "Relevant history", "left total hip replacement on 2 May; uncomplicated recovery", "Omitting the date or nature of surgery."),
                new("Wound care and staple removal on day 14", "follow_up", "high", "required", "Management/plan", "review the wound daily and remove staples on 16 May", "Failing to specify the staple-removal date."),
                new("Penicillin allergy", "allergy", "high", "required", "Relevant history", "she is allergic to penicillin", "Leaving out a documented drug allergy."),
                new("Monitoring for infection or DVT", "follow_up", "medium", "required", "Management/plan", "monitor for signs of infection or deep vein thrombosis", "Not requesting post-operative monitoring."),
                new("Social support (lives alone, daughter visits)", "social", "medium", "optional", "Patient details", "lives alone with daily support from her daughter", "Overlooking the home-support context."),
                new("Patient's preference for tea over coffee", "social", "low", "irrelevant", null, null, "Including trivial personal preferences."),
                new("Daughter's holiday plans next month", "social", "low", "irrelevant", null, null, "Adding unrelated family information."),
            },
        },

        // ── 3. Pharmacy — urgent referral ───────────────────────────────────
        new DemoTaskSpec
        {
            InternalCode = "PHA-WR-S01",
            Title = "Urgent referral: Mr Daniel Owusu (suspected adverse drug reaction)",
            Profession = "Pharmacy",
            LetterType = "urgent_referral",
            Difficulty = 4,
            WriterRole = "You are the community pharmacist at Greenway Pharmacy.",
            TodayDate = "21 June 2026",
            TaskPrompt = "Using the information in the case notes, write an urgent letter to the patient's general practitioner regarding a suspected adverse drug reaction that requires same-day review.",
            ExpectedPurpose = "Alert the GP to a suspected serious adverse drug reaction requiring urgent same-day assessment.",
            ExpectedAction = "Request urgent review, possible cessation of the implicated medicine, and renal monitoring.",
            Recipient = new DemoRecipient
            {
                Name = "Dr Priya Nair",
                Role = "General Practitioner",
                Organisation = "Greenway Medical Centre",
                Address = "5 Hawthorn Road, Greenway",
            },
            CaseNoteSections = new List<DemoCaseNoteSection>
            {
                new("Patient details", new List<string>
                {
                    "Daniel Owusu, 66 years old, male",
                    "Collected repeat prescription this morning",
                    "Accompanied by his son",
                }),
                new("Relevant history", new List<string>
                {
                    "Type 2 diabetes; hypertension; gout",
                    "Recently started allopurinol (10 days ago)",
                    "No previously documented drug allergies",
                }),
                new("Presenting complaint", new List<string>
                {
                    "2-day history of widespread itchy rash",
                    "Facial swelling and mild fever today",
                    "Reports feeling generally unwell",
                }),
                new("Examination/findings", new List<string>
                {
                    "Widespread maculopapular rash on trunk and arms",
                    "Mild periorbital swelling; temperature 37.9°C",
                    "No breathing difficulty or wheeze at present",
                }),
                new("Management/plan", new List<string>
                {
                    "Suspected adverse reaction to allopurinol",
                    "Advised to stop allopurinol immediately",
                    "Urgent same-day GP review recommended",
                    "Consider renal function check and antihistamine",
                }),
            },
            ModelAnswer =
                "Dear Dr Nair,\n\n" +
                "Re: Mr Daniel Owusu, 66 years old — urgent\n\n" +
                "I am writing to refer Mr Owusu urgently, as I suspect he is experiencing a serious adverse reaction to allopurinol and requires same-day review.\n\n" +
                "Mr Owusu is a 66-year-old man with type 2 diabetes, hypertension and gout, who started allopurinol ten days ago. He collected his repeat prescription this morning and reported a two-day history of a widespread, itchy rash, with facial swelling and a mild fever developing today. He feels generally unwell.\n\n" +
                "On examination, he had a widespread maculopapular rash over his trunk and arms, mild periorbital swelling and a temperature of 37.9°C. He has no breathing difficulty at present.\n\n" +
                "I have advised him to stop the allopurinol immediately. I would be grateful if you could review him urgently today, consider checking his renal function, and arrange appropriate treatment.\n\n" +
                "Yours sincerely,\nCommunity Pharmacist",
            Checklist = new List<DemoChecklistItem>
            {
                new("Patient name and age (Mr Owusu, 66)", "patient_identity", "high", "required", "Patient details", "Mr Daniel Owusu, a 66-year-old man", "Omitting the patient's age."),
                new("Urgency clearly conveyed", "urgency", "high", "required", "Management/plan", "refer urgently ... requires same-day review", "Not signalling that the referral is urgent."),
                new("Suspected adverse reaction to allopurinol", "diagnosis", "high", "required", "Management/plan", "I suspect a serious adverse reaction to allopurinol", "Failing to name the suspected cause."),
                new("Recently started allopurinol (timeline)", "medication", "high", "required", "Relevant history", "started allopurinol ten days ago", "Leaving out when the medicine was started."),
                new("Presenting symptoms (rash, facial swelling, fever)", "symptoms", "high", "required", "Presenting complaint", "widespread itchy rash, facial swelling and mild fever", "Describing the reaction vaguely."),
                new("Action taken / requested (stop drug, renal check)", "follow_up", "medium", "required", "Management/plan", "advised to stop allopurinol; please check renal function", "Not stating the action taken or requested."),
                new("Patient's recent overseas travel for leisure", "social", "low", "irrelevant", null, null, "Including travel unrelated to the reaction."),
                new("Son's occupation as an accountant", "social", "low", "irrelevant", null, null, "Adding companions' irrelevant details."),
                new("Well-controlled gout over the past year", "history", "low", "irrelevant", null, null, "Including stable background detail that does not affect the urgent issue."),
            },
        },
    };

    // ── Demo-task seed value types (internal only) ──────────────────────────

    private sealed class DemoTaskSpec
    {
        public string InternalCode { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Profession { get; init; } = string.Empty;
        public string LetterType { get; init; } = string.Empty;
        public int Difficulty { get; init; } = 3;
        public string WriterRole { get; init; } = string.Empty;
        public string TodayDate { get; init; } = string.Empty;
        public string TaskPrompt { get; init; } = string.Empty;
        public string ExpectedPurpose { get; init; } = string.Empty;
        public string ExpectedAction { get; init; } = string.Empty;
        public DemoRecipient Recipient { get; init; } = new();
        public List<DemoCaseNoteSection> CaseNoteSections { get; init; } = new();
        public string ModelAnswer { get; init; } = string.Empty;
        public List<DemoChecklistItem> Checklist { get; init; } = new();
    }

    private sealed class DemoRecipient
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("role")]
        public string Role { get; init; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("organisation")]
        public string Organisation { get; init; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("address")]
        public string Address { get; init; } = string.Empty;
    }

    private sealed class DemoCaseNoteSection
    {
        public DemoCaseNoteSection(string heading, List<string> items)
        {
            Heading = heading;
            Items = items;
        }

        [System.Text.Json.Serialization.JsonPropertyName("heading")]
        public string Heading { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("items")]
        public List<string> Items { get; init; }
    }

    private sealed class DemoChecklistItem
    {
        public DemoChecklistItem(
            string itemText, string category, string importance, string requiredStatus,
            string? linkedCaseNoteSection, string? expectedRepresentation, string? commonError)
        {
            ItemText = itemText;
            Category = category;
            Importance = importance;
            RequiredStatus = requiredStatus;
            LinkedCaseNoteSection = linkedCaseNoteSection;
            ExpectedRepresentation = expectedRepresentation;
            CommonError = commonError;
        }

        public string ItemText { get; }
        public string Category { get; }
        public string Importance { get; }
        public string RequiredStatus { get; }
        public string? LinkedCaseNoteSection { get; }
        public string? ExpectedRepresentation { get; }
        public string? CommonError { get; }
    }

    // ───────────────────────────── helpers ─────────────────────────────────

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

    // ─────────────────── JSON DTOs (internal only) ─────────────────────────

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

    private sealed class ExemplarsPayload
    {
        public int SchemaVersion { get; set; } = 1;
        public int Version { get; set; } = 1;
        public string? Source { get; set; }
        public List<ExemplarDto>? Exemplars { get; set; }
    }

    private sealed class ExemplarDto
    {
        public Guid Id { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("scenario_id")]
        public Guid? ScenarioId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("letter_type")]
        public string? LetterType { get; set; }
        public string? Profession { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("letter_content")]
        public string? LetterContent { get; set; }
        public List<ExemplarAnnotationDto>? Annotations { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("target_band")]
        public string? TargetBand { get; set; }
        public string? Status { get; set; }
        public int Version { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("author_id")]
        public string? AuthorId { get; set; }
    }

    private sealed class ExemplarAnnotationDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("char_start")]
        public int? CharStart { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("char_end")]
        public int? CharEnd { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("rule_id")]
        public string? RuleId { get; set; }
        public string? Note { get; set; }
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
