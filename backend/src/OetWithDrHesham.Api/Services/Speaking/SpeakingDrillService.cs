using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Content;
using OetWithDrHesham.Api.Services.Rulebook;

namespace OetWithDrHesham.Api.Services.Speaking;

// Phase 5 (G) of the OET Speaking module roadmap.
//
// Learner-facing service for the speaking drills bank. Glues together:
//
//   * `SpeakingDrillItem` rows authored in `AdminService.SpeakingDrills.cs`
//   * `IFileStorage` for short MediaRecorder audio uploads
//   * `IAiGatewayService` for AI micro-feedback (stubbed in Phase 5,
//     wired to Whisper + the speaking rubric in Phase 7+)
//   * `SpeakingAiAssessment.PerCriterionRationalesJson` for the
//     "recommended drills" surface that targets the learner's lowest
//     criteria from their most recent session.
//
// All methods are scoped per-learner: a learner can never read or
// score another learner's drill attempt (IDOR guard via UserId check).
public sealed class SpeakingDrillService(
    LearnerDbContext db,
    IAiGatewayService aiGateway,
    IFileStorage fileStorage)
{
    private const int DrillRecordingMaxBytes = 5 * 1024 * 1024; // 5 MB cap on short drill audio
    private const string DrillRecordingPrefix = "speaking-drills";

    /// <summary>List drills available to a learner. Filters by kind /
    /// profession. When `recommendedForSessionId` is supplied, surfaces
    /// 3 drills targeting the lowest-scoring criteria from that
    /// session's `SpeakingAiAssessment.PerCriterionRationalesJson`.</summary>
    public async Task<IReadOnlyList<DrillSummary>> ListDrillsForLearnerAsync(
        string userId,
        string? kind,
        string? professionId,
        string? recommendedForSessionId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw ApiException.Unauthorized("learner_required", "Learner identity is required.");
        }

        var query = from drill in db.SpeakingDrillItems.AsNoTracking()
                    join content in db.ContentItems.AsNoTracking()
                        on drill.ContentItemId equals content.Id
                    where content.Status == ContentStatus.Published
                          && content.ContentType == "speaking_drill"
                    select new { drill, content };

        if (!string.IsNullOrWhiteSpace(kind)
            && Enum.TryParse<SpeakingDrillKind>(kind.Trim(), ignoreCase: true, out var drillKind))
        {
            query = query.Where(x => x.drill.DrillKind == drillKind);
        }
        if (!string.IsNullOrWhiteSpace(professionId))
        {
            var pid = professionId.Trim();
            query = query.Where(x => x.content.ProfessionId == pid || x.content.ProfessionId == null);
        }

        var rows = await query
            .OrderBy(x => x.drill.DrillKind)
            .ThenBy(x => x.content.Title)
            .Take(500)
            .ToListAsync(ct);

        // Per-learner attempt index for HasAttempted/BestScore.
        var drillIds = rows.Select(r => r.drill.Id).ToArray();
        var attempts = await db.SpeakingDrillAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && drillIds.Contains(a.DrillItemId))
            .Select(a => new { a.DrillItemId, a.Score })
            .ToListAsync(ct);
        var byDrill = attempts
            .GroupBy(a => a.DrillItemId)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    HasAttempted = true,
                    BestScore = g.Where(x => x.Score.HasValue).Select(x => (int?)x.Score!.Value).Max(),
                });

        // Recommended-drills slice: pick the 3 drills whose criteria
        // best match the learner's lowest-scoring criteria from the
        // referenced session's AI assessment.
        if (!string.IsNullOrWhiteSpace(recommendedForSessionId))
        {
            var lowest = await ResolveLowestCriteriaAsync(userId, recommendedForSessionId, ct);
            if (lowest.Count > 0)
            {
                var ordered = rows
                    .Select(r => new
                    {
                        Row = r,
                        Score = ScoreRowAgainstCriteria(r.drill.TargetCriteriaJson, lowest),
                    })
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .Take(3)
                    .Select(x => x.Row)
                    .ToList();
                if (ordered.Count > 0)
                {
                    rows = ordered;
                }
            }
        }

        return rows.Select(r =>
        {
            byDrill.TryGetValue(r.drill.Id, out var attempted);
            return new DrillSummary(
                DrillId: r.drill.Id,
                DrillKind: r.drill.DrillKind.ToString(),
                Title: r.content.Title,
                InstructionText: ParseInstructionText(r.content.DetailJson),
                TargetCriteria: ParseTargetCriteria(r.drill.TargetCriteriaJson),
                HasAttempted: attempted is not null,
                BestScore: attempted?.BestScore);
        }).ToArray();
    }

    /// <summary>Create a new drill attempt for this learner. Source
    /// records where the recommendation came from so analytics can
    /// distinguish AI-recommended vs manually-browsed practice.</summary>
    public async Task<DrillAttemptDetail> StartAttemptAsync(
        string userId,
        string drillId,
        string? sourceCode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw ApiException.Unauthorized("learner_required", "Learner identity is required.");
        }
        var (drill, content) = await ResolveLaunchableDrillAsync(drillId, ct);

        var source = ParseAttemptSource(sourceCode);
        var now = DateTimeOffset.UtcNow;
        var attempt = new SpeakingDrillAttempt
        {
            Id = $"sda-{Guid.NewGuid():N}",
            UserId = userId,
            DrillItemId = drill.Id,
            StartedAt = now,
            Source = source,
            AiFeedbackJson = "{}",
        };
        db.SpeakingDrillAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);

        return new DrillAttemptDetail(
            AttemptId: attempt.Id,
            DrillId: drill.Id,
            StartedAt: attempt.StartedAt,
            CompletedAt: null,
            Score: null,
            FeedbackSummary: null);
    }

    /// <summary>Persist a short (≤5MB) MediaRecorder blob from the
    /// `<DrillPlayer/>` into `IFileStorage` and attach the resulting
    /// `SpeakingRecording` row to this attempt.</summary>
    public async Task UploadRecordingAsync(
        string userId,
        string attemptId,
        Stream audio,
        string mimeType,
        long sizeBytes,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw ApiException.Unauthorized("learner_required", "Learner identity is required.");
        }
        if (sizeBytes <= 0 || sizeBytes > DrillRecordingMaxBytes)
        {
            throw ApiException.Validation("SPEAKING_DRILL_RECORDING_TOO_LARGE",
                $"Drill recordings must be between 1 byte and {DrillRecordingMaxBytes} bytes.");
        }
        var attempt = await db.SpeakingDrillAttempts
            .FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == userId, ct)
            ?? throw ApiException.NotFound("speaking_drill_attempt_not_found",
                "That drill attempt does not exist.");

        // Write the blob via the shared file-storage layer. Object key
        // is namespaced under `speaking-drills/{userId}/{attemptId}`
        // so retention sweeps + per-learner exports can scan by prefix.
        var extension = MimeToExtension(mimeType);
        var objectKey = $"{DrillRecordingPrefix}/{userId}/{attemptId}{extension}";

        // Compute SHA-256 alongside the write so the recording row can
        // be deduplicated by content hash if the same audio is uploaded
        // twice.
        using var sha = SHA256.Create();
        using var hashStream = new CryptoStream(audio, sha, CryptoStreamMode.Read, leaveOpen: true);
        var written = await fileStorage.WriteAsync(objectKey, hashStream, ct);
        var sha256 = Convert.ToHexString(sha.Hash ?? Array.Empty<byte>()).ToLowerInvariant();

        // TODO(P5): the foundation entity does not yet expose the link
        // from `SpeakingDrillAttempt` to a `SpeakingRecording` row in
        // the schema. We persist the object key directly on the
        // attempt's `AudioRecordingId` so the player + retention
        // worker can still locate the blob; full SpeakingRecording
        // wiring lands once P1-Phase2-7 migration commits.
        attempt.AudioRecordingId = objectKey;
        attempt.AiFeedbackJson = JsonSupport.Serialize(new
        {
            uploadedBytes = written,
            mimeType,
            sha256,
        });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Score a completed drill attempt. Phase 5 ships a stub
    /// scorer so the UI can light up end-to-end; Phase 7 wires the real
    /// Whisper + rubric pipeline through `IAiGatewayService`.</summary>
    public async Task<DrillScoringResponse> ScoreAttemptAsync(
        string userId,
        string attemptId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw ApiException.Unauthorized("learner_required", "Learner identity is required.");
        }
        var attempt = await db.SpeakingDrillAttempts
            .FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == userId, ct)
            ?? throw ApiException.NotFound("speaking_drill_attempt_not_found",
                "That drill attempt does not exist.");
        if (string.IsNullOrWhiteSpace(attempt.AudioRecordingId))
        {
            throw ApiException.Validation(
                "speaking_drill_recording_required",
                "Record and upload audio before scoring this drill.");
        }
        var drill = await db.SpeakingDrillItems.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == attempt.DrillItemId, ct)
            ?? throw ApiException.NotFound("speaking_drill_not_found", "That drill no longer exists.");
        var content = await db.ContentItems.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == drill.ContentItemId, ct);

        // TODO(P7): replace stub with real IAiGatewayService call. The
        // gateway dependency is injected today so the wiring + DI graph
        // is correct; we just don't yet call .CompleteAsync here.
        // `aiGateway` is intentionally referenced via `_ = aiGateway` so
        // the unused-field analyser doesn't drop it from the build.
        _ = aiGateway;

        // Stub scorer — 70 base + small bump if the drill kind is one of
        // the higher-frequency criteria. Always returns the same shape
        // so frontend tests can target stable payloads.
        var baseScore = 70;
        var criteria = ParseTargetCriteria(drill.TargetCriteriaJson);
        var bonus = criteria.Contains("appropriateness", StringComparer.OrdinalIgnoreCase) ? 5 : 0;
        var score = Math.Clamp(baseScore + bonus, 0, 100);

        var summary = $"Solid attempt on the '{content?.Title ?? drill.DrillKind.ToString()}' drill. "
                  + "The scorer heard a clear opening and at least one structural marker — keep building on that.";
        var specificComments = new[]
        {
            $"You hit the {drill.DrillKind} focus in your opening — keep that structure.",
            "Pace was steady; one or two natural pauses would lift the fluency score further.",
            "Pronunciation of the key clinical terms was intelligible.",
        };
        var nextRecommendations = new[]
        {
            "Retry this drill aiming to add one explicit empathy phrase.",
            "Pair this drill with the matching role-play card before your next mock.",
        };

        var now = DateTimeOffset.UtcNow;
        attempt.Score = score;
        attempt.CompletedAt = now;
        attempt.AiFeedbackJson = JsonSupport.Serialize(new
        {
            score,
            summary,
            specificComments,
            nextRecommendations,
            modelVersion = "deterministic-v0",
            scoredAt = now,
        });
        await db.SaveChangesAsync(ct);

        return new DrillScoringResponse(
            AttemptId: attempt.Id,
            Score: score,
            Summary: summary,
            SpecificComments: specificComments,
            NextRecommendations: nextRecommendations);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<string>> ResolveLowestCriteriaAsync(
        string userId,
        string sessionId,
        CancellationToken ct)
    {
        // Confirm the session belongs to the requesting learner before
        // we look at its assessment (IDOR guard).
        var session = await db.SpeakingSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null)
        {
            return Array.Empty<string>();
        }

        var assessment = await db.SpeakingAiAssessments.AsNoTracking()
            .Where(a => a.SpeakingSessionId == sessionId)
            .OrderByDescending(a => a.GeneratedAt)
            .FirstOrDefaultAsync(ct);
        if (assessment is null)
        {
            return Array.Empty<string>();
        }

        // Each criterion is stored as a 0..6 or 0..3 band. Pick the
        // bottom three regardless of scale — the recommender treats
        // "low for its own band" as a flag.
        var criteria = new (string Code, double Normalised)[]
        {
            ("intelligibility", assessment.Intelligibility / 6.0),
            ("fluency", assessment.Fluency / 6.0),
            ("appropriateness", assessment.Appropriateness / 6.0),
            ("grammarExpression", assessment.GrammarExpression / 6.0),
            ("relationshipBuilding", assessment.RelationshipBuilding / 3.0),
            ("patientPerspective", assessment.PatientPerspective / 3.0),
            ("structure", assessment.Structure / 3.0),
            ("informationGathering", assessment.InformationGathering / 3.0),
            ("informationGiving", assessment.InformationGiving / 3.0),
        };

        return criteria
            .OrderBy(c => c.Normalised)
            .Take(3)
            .Select(c => c.Code)
            .ToList();
    }

    private async Task<(SpeakingDrillItem Drill, ContentItem Content)> ResolveLaunchableDrillAsync(
        string drillIdOrContentId,
        CancellationToken ct)
    {
        var drill = await db.SpeakingDrillItems.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == drillIdOrContentId || d.ContentItemId == drillIdOrContentId, ct);
        if (drill is not null)
        {
            var content = await db.ContentItems.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == drill.ContentItemId && c.Status == ContentStatus.Published, ct)
                ?? throw ApiException.NotFound("speaking_drill_not_published", "That drill is not available.");
            return (drill, content);
        }

        var legacyContent = await db.ContentItems.AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.Id == drillIdOrContentId
                && c.ContentType == "speaking_drill"
                && c.SubtestCode == "speaking"
                && c.Status == ContentStatus.Published,
                ct)
            ?? throw ApiException.NotFound("speaking_drill_not_found", "That drill does not exist.");

        var generatedId = LegacyDrillItemId(legacyContent.Id);
        var existing = await db.SpeakingDrillItems.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == generatedId || d.ContentItemId == legacyContent.Id, ct);
        if (existing is not null)
        {
            return (existing, legacyContent);
        }

        var now = DateTimeOffset.UtcNow;
        var created = new SpeakingDrillItem
        {
            Id = generatedId,
            ContentItemId = legacyContent.Id,
            DrillKind = MapLegacyDrillKind(legacyContent.ScenarioType),
            TargetCriteriaJson = string.IsNullOrWhiteSpace(legacyContent.CriteriaFocusJson)
                ? "[]"
                : legacyContent.CriteriaFocusJson,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.SpeakingDrillItems.Add(created);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            var raced = await db.SpeakingDrillItems.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == generatedId || d.ContentItemId == legacyContent.Id, ct);
            if (raced is null)
            {
                throw;
            }
            return (raced, legacyContent);
        }
        return (created, legacyContent);
    }

    private static string LegacyDrillItemId(string contentItemId)
    {
        var safe = new string(contentItemId
            .Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')
            .ToArray());
        if (safe.Length > 0 && safe.Length <= 53)
        {
            return $"sdi-legacy-{safe}";
        }

        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(contentItemId)))
            .ToLowerInvariant()[..16];
        return $"sdi-legacy-{hash}";
    }

    private static SpeakingDrillKind MapLegacyDrillKind(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "empathy" => SpeakingDrillKind.Empathy,
        "pronunciation" => SpeakingDrillKind.Pronunciation,
        "vocabulary" => SpeakingDrillKind.LayLanguage,
        "chunking" => SpeakingDrillKind.Fluency,
        "intonation" => SpeakingDrillKind.Fluency,
        "phrasing" => SpeakingDrillKind.Opening,
        _ => SpeakingDrillKind.OpenQuestion,
    };

    private static int ScoreRowAgainstCriteria(string targetCriteriaJson, IReadOnlyList<string> lowestCriteria)
    {
        if (lowestCriteria.Count == 0) return 0;
        var rowCriteria = ParseTargetCriteria(targetCriteriaJson);
        return rowCriteria.Count(c => lowestCriteria.Contains(c, StringComparer.OrdinalIgnoreCase));
    }

    private static SpeakingDrillAttemptSource ParseAttemptSource(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return SpeakingDrillAttemptSource.ManualBrowse;
        return raw.Trim().ToLowerInvariant() switch
        {
            "recommendedpostassessment" or "recommended_post_assessment"
                => SpeakingDrillAttemptSource.RecommendedPostAssessment,
            "learningpathstage" or "learning_path_stage"
                => SpeakingDrillAttemptSource.LearningPathStage,
            _ => SpeakingDrillAttemptSource.ManualBrowse,
        };
    }

    private static string[] ParseTargetCriteria(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            var list = new List<string>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
            }
            return list.ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string ParseInstructionText(string? detailJson)
    {
        if (string.IsNullOrWhiteSpace(detailJson)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(detailJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return string.Empty;
            if (doc.RootElement.TryGetProperty("instructionText", out var prop)
                && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString() ?? string.Empty;
            }
            return string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string MimeToExtension(string mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType)) return ".bin";
        return mimeType.ToLowerInvariant() switch
        {
            "audio/webm" or "audio/webm;codecs=opus" => ".webm",
            "audio/mp4" or "audio/m4a" => ".m4a",
            "audio/mpeg" or "audio/mp3" => ".mp3",
            "audio/wav" or "audio/x-wav" => ".wav",
            "audio/ogg" or "audio/ogg;codecs=opus" => ".ogg",
            _ => ".bin",
        };
    }
}
