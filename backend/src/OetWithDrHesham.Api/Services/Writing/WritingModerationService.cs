using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Writing;

/// <summary>
/// Double-marking moderation lifecycle (WS-B4). For scenarios with MarkingMode == "double" a
/// <see cref="WritingModeration"/> row tracks the first and second markers' scores, the variance
/// between them, and (when variance exceeds the threshold) escalation to a senior marker.
///
/// Statuses: pending_first → pending_second → (finalized | pending_moderation) → finalized.
/// </summary>
public interface IWritingModerationService
{
    Task<WritingModeration?> GetAsync(Guid submissionId, CancellationToken ct);

    /// <summary>
    /// Records a marker's submission against the moderation row, creating it on the first submit.
    /// Returns the moderation row, or null when the row could not be advanced (e.g. a third distinct
    /// marker tries to submit). Variance auto-finalizes when within <paramref name="varianceThreshold"/>.
    /// </summary>
    Task<WritingModeration> RecordMarkerScoreAsync(
        Guid submissionId,
        string markerId,
        string markerSequence,
        WritingCriteriaScores score,
        int varianceThreshold,
        CancellationToken ct);

    /// <summary>Senior moderator sets the final score and finalizes a row in pending_moderation.</summary>
    Task<WritingModeration?> FinalizeAsync(
        Guid submissionId,
        string seniorMarkerId,
        WritingCriteriaScores finalScore,
        string finalDecisionNote,
        CancellationToken ct);
}

public sealed class WritingModerationService : IWritingModerationService
{
    public const int DefaultVarianceThreshold = 3;

    private readonly LearnerDbContext _db;
    private readonly ILogger<WritingModerationService> _logger;

    public WritingModerationService(LearnerDbContext db, ILogger<WritingModerationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<WritingModeration?> GetAsync(Guid submissionId, CancellationToken ct)
    {
        return await _db.WritingModerations
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.SubmissionId == submissionId, ct);
    }

    public async Task<WritingModeration> RecordMarkerScoreAsync(
        Guid submissionId,
        string markerId,
        string markerSequence,
        WritingCriteriaScores score,
        int varianceThreshold,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var moderation = await _db.WritingModerations
            .FirstOrDefaultAsync(m => m.SubmissionId == submissionId, ct);

        if (moderation is null)
        {
            // First marker — create the row.
            moderation = new WritingModeration
            {
                Id = Guid.NewGuid(),
                SubmissionId = submissionId,
                FirstMarkerId = markerId,
                FirstScoreJson = JsonSerializer.Serialize(score),
                Status = "pending_second",
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.WritingModerations.Add(moderation);
            await _db.SaveChangesAsync(ct);
            return moderation;
        }

        // Re-submission by the same first marker before a second exists — refresh first score.
        if (moderation.SecondScoreJson is null
            && (string.Equals(moderation.FirstMarkerId, markerId, StringComparison.Ordinal)
                || string.Equals(markerSequence, "first", StringComparison.OrdinalIgnoreCase)
                   && moderation.FirstMarkerId == markerId))
        {
            moderation.FirstMarkerId = markerId;
            moderation.FirstScoreJson = JsonSerializer.Serialize(score);
            moderation.Status = "pending_second";
            moderation.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
            return moderation;
        }

        // Second marker (distinct from the first) — fill second score and compute variance.
        if (moderation.SecondScoreJson is null
            && !string.Equals(moderation.FirstMarkerId, markerId, StringComparison.Ordinal))
        {
            moderation.SecondMarkerId = markerId;
            moderation.SecondScoreJson = JsonSerializer.Serialize(score);

            var first = Deserialize(moderation.FirstScoreJson);
            var firstRaw = first?.RawTotal ?? 0;
            var secondRaw = score.RawTotal;
            var variance = Math.Abs(firstRaw - secondRaw);
            moderation.VariancePoints = variance;

            if (variance <= varianceThreshold && first is not null)
            {
                // Auto-finalize: per-criterion average, rounded.
                var final = Average(first, score);
                moderation.FinalScoreJson = JsonSerializer.Serialize(final);
                moderation.Status = "finalized";
                moderation.VarianceReason = $"Auto-finalized: variance {variance} within threshold {varianceThreshold}.";
            }
            else
            {
                moderation.Status = "pending_moderation";
                moderation.VarianceReason = $"Variance {variance} exceeds threshold {varianceThreshold}; senior moderation required.";
            }

            moderation.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
            return moderation;
        }

        // Second marker re-submission (already finalized rows just refresh second score + recompute).
        if (!string.Equals(moderation.FirstMarkerId, markerId, StringComparison.Ordinal)
            && (moderation.SecondMarkerId is null
                || string.Equals(moderation.SecondMarkerId, markerId, StringComparison.Ordinal)))
        {
            moderation.SecondMarkerId = markerId;
            moderation.SecondScoreJson = JsonSerializer.Serialize(score);

            var first = Deserialize(moderation.FirstScoreJson);
            var variance = Math.Abs((first?.RawTotal ?? 0) - score.RawTotal);
            moderation.VariancePoints = variance;

            if (moderation.Status != "finalized")
            {
                if (variance <= varianceThreshold && first is not null)
                {
                    moderation.FinalScoreJson = JsonSerializer.Serialize(Average(first, score));
                    moderation.Status = "finalized";
                    moderation.VarianceReason = $"Auto-finalized: variance {variance} within threshold {varianceThreshold}.";
                }
                else
                {
                    moderation.Status = "pending_moderation";
                    moderation.VarianceReason = $"Variance {variance} exceeds threshold {varianceThreshold}; senior moderation required.";
                }
            }

            moderation.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
            return moderation;
        }

        // No advancement possible (e.g. a third distinct marker) — return current state unchanged.
        _logger.LogWarning(
            "Moderation for submission {SubmissionId} not advanced by marker {MarkerId} (status {Status}).",
            submissionId, markerId, moderation.Status);
        return moderation;
    }

    public async Task<WritingModeration?> FinalizeAsync(
        Guid submissionId,
        string seniorMarkerId,
        WritingCriteriaScores finalScore,
        string finalDecisionNote,
        CancellationToken ct)
    {
        var moderation = await _db.WritingModerations
            .FirstOrDefaultAsync(m => m.SubmissionId == submissionId, ct);
        if (moderation is null)
        {
            return null;
        }

        moderation.SeniorMarkerId = seniorMarkerId;
        moderation.FinalScoreJson = JsonSerializer.Serialize(finalScore);
        moderation.FinalDecisionNote = finalDecisionNote;
        moderation.Status = "finalized";
        moderation.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return moderation;
    }

    private static WritingCriteriaScores Average(WritingCriteriaScores a, WritingCriteriaScores b)
    {
        static int Avg(int x, int y) => (int)Math.Round((x + y) / 2.0, MidpointRounding.AwayFromZero);
        return new WritingCriteriaScores(
            Avg(a.C1Purpose, b.C1Purpose),
            Avg(a.C2Content, b.C2Content),
            Avg(a.C3Conciseness, b.C3Conciseness),
            Avg(a.C4Genre, b.C4Genre),
            Avg(a.C5Organisation, b.C5Organisation),
            Avg(a.C6Language, b.C6Language));
    }

    private static WritingCriteriaScores? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<WritingCriteriaScores>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
