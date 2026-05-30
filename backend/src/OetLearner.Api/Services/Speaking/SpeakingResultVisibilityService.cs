using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

/// <summary>
/// WS6 — resolves and upserts the Speaking result-visibility configuration
/// (Developer Implementation Notes §10). A single global row (id = "global")
/// provides defaults; an optional per-role-play-card override row stands in for
/// that card. Used by the learner result surface to gate what a learner may see.
///
/// Mirrors <see cref="OetLearner.Api.Services.Writing.IWritingResultVisibilityService"/>.
/// </summary>
public interface ISpeakingResultVisibilityService
{
    /// <summary>
    /// Returns the effective config for a role-play card: the card override row
    /// when one exists, otherwise the global default row (created on first read
    /// if missing).
    /// </summary>
    Task<SpeakingResultVisibilityConfig> ResolveAsync(string? rolePlayCardId, CancellationToken ct);

    /// <summary>Effective config mapped to the wire DTO.</summary>
    Task<SpeakingResultVisibilityDto> ResolveDtoAsync(string? rolePlayCardId, CancellationToken ct);

    /// <summary>
    /// Upserts the global row (rolePlayCardId null) or a card override row.
    /// </summary>
    Task<SpeakingResultVisibilityDto> UpsertAsync(SpeakingResultVisibilityDto dto, string? rolePlayCardId, CancellationToken ct);
}

/// <inheritdoc />
public sealed class SpeakingResultVisibilityService(
    LearnerDbContext db,
    TimeProvider clock,
    ILogger<SpeakingResultVisibilityService> logger) : ISpeakingResultVisibilityService
{
    private const string GlobalId = "global";

    public async Task<SpeakingResultVisibilityConfig> ResolveAsync(string? rolePlayCardId, CancellationToken ct)
    {
        var global = await db.SpeakingResultVisibilityConfigs
            .FirstOrDefaultAsync(c => c.Id == GlobalId, ct);
        if (global is null)
        {
            // Create the global default row on first read. Entity defaults
            // already encode the spec defaults (everything shown).
            global = new SpeakingResultVisibilityConfig
            {
                Id = GlobalId,
                RolePlayCardId = null,
                UpdatedAt = clock.GetUtcNow(),
            };
            db.SpeakingResultVisibilityConfigs.Add(global);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Created default global speaking result-visibility config");
        }

        if (!string.IsNullOrWhiteSpace(rolePlayCardId))
        {
            var overrideRow = await db.SpeakingResultVisibilityConfigs
                .FirstOrDefaultAsync(c => c.RolePlayCardId == rolePlayCardId, ct);
            if (overrideRow is not null)
            {
                // Booleans have no tri-state, so an existing override row fully
                // represents the card's effective config (override over global).
                return overrideRow;
            }
        }

        return global;
    }

    public async Task<SpeakingResultVisibilityDto> ResolveDtoAsync(string? rolePlayCardId, CancellationToken ct)
    {
        var config = await ResolveAsync(rolePlayCardId, ct);
        return ToDto(config);
    }

    public async Task<SpeakingResultVisibilityDto> UpsertAsync(SpeakingResultVisibilityDto dto, string? rolePlayCardId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        SpeakingResultVisibilityConfig row;

        if (!string.IsNullOrWhiteSpace(rolePlayCardId))
        {
            row = await db.SpeakingResultVisibilityConfigs
                .FirstOrDefaultAsync(c => c.RolePlayCardId == rolePlayCardId, ct)
                ?? AddNew(new SpeakingResultVisibilityConfig
                {
                    Id = $"card:{rolePlayCardId}",
                    RolePlayCardId = rolePlayCardId,
                });
        }
        else
        {
            row = await db.SpeakingResultVisibilityConfigs
                .FirstOrDefaultAsync(c => c.Id == GlobalId, ct)
                ?? AddNew(new SpeakingResultVisibilityConfig { Id = GlobalId, RolePlayCardId = null });
        }

        ApplyDto(row, dto);
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Upserted speaking result-visibility config {ConfigId} (card {RolePlayCardId})",
            row.Id,
            row.RolePlayCardId);

        return ToDto(row);

        SpeakingResultVisibilityConfig AddNew(SpeakingResultVisibilityConfig created)
        {
            db.SpeakingResultVisibilityConfigs.Add(created);
            return created;
        }
    }

    private static void ApplyDto(SpeakingResultVisibilityConfig row, SpeakingResultVisibilityDto dto)
    {
        // RolePlayCardId is governed by the upsert target, not the payload.
        row.ShowSubmissionReceived = dto.ShowSubmissionReceived;
        row.ShowAiEstimate = dto.ShowAiEstimate;
        row.ShowReadinessBand = dto.ShowReadinessBand;
        row.ShowTutorScore = dto.ShowTutorScore;
        row.ShowFullCriteria = dto.ShowFullCriteria;
        row.ShowTranscript = dto.ShowTranscript;
        row.ShowTutorComments = dto.ShowTutorComments;
        row.ShowRecommendedDrills = dto.ShowRecommendedDrills;
        row.AllowReattempt = dto.AllowReattempt;
    }

    private static SpeakingResultVisibilityDto ToDto(SpeakingResultVisibilityConfig c) => new(
        c.RolePlayCardId,
        c.ShowSubmissionReceived,
        c.ShowAiEstimate,
        c.ShowReadinessBand,
        c.ShowTutorScore,
        c.ShowFullCriteria,
        c.ShowTranscript,
        c.ShowTutorComments,
        c.ShowRecommendedDrills,
        c.AllowReattempt,
        c.UpdatedAt);
}

/// <summary>
/// WS6 wire contract. Mirrors lib/api/speaking-result-visibility.ts
/// (camelCase on the wire). No "id" field by design — the wire contract keys on
/// the optional rolePlayCardId.
/// </summary>
public record SpeakingResultVisibilityDto(
    string? RolePlayCardId,
    bool ShowSubmissionReceived,
    bool ShowAiEstimate,
    bool ShowReadinessBand,
    bool ShowTutorScore,
    bool ShowFullCriteria,
    bool ShowTranscript,
    bool ShowTutorComments,
    bool ShowRecommendedDrills,
    bool AllowReattempt,
    DateTimeOffset UpdatedAt);
