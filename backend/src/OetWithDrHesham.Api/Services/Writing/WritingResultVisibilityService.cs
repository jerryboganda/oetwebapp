using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Writing;

/// <summary>
/// Resolves and upserts the Writing result-visibility configuration (spec §15.1).
/// A single global row (id = "global") provides defaults; an optional per-scenario
/// override row stands in for that scenario. Used by the learner feedback service to
/// gate what a learner may see.
/// </summary>
public interface IWritingResultVisibilityService
{
    /// <summary>
    /// Returns the effective config for a scenario: the scenario override row when one
    /// exists, otherwise the global default row (created on first read if missing).
    /// </summary>
    Task<WritingResultVisibilityConfig> ResolveAsync(Guid? scenarioId, CancellationToken ct);

    /// <summary>Effective config mapped to the wire DTO.</summary>
    Task<WritingResultVisibilityDto> ResolveDtoAsync(Guid? scenarioId, CancellationToken ct);

    /// <summary>
    /// Upserts the global row (scenarioId null) or a scenario override row.
    /// </summary>
    Task<WritingResultVisibilityDto> UpsertAsync(WritingResultVisibilityDto dto, Guid? scenarioId, CancellationToken ct);
}

/// <inheritdoc />
public class WritingResultVisibilityService(
    LearnerDbContext db,
    TimeProvider clock,
    ILogger<WritingResultVisibilityService> logger) : IWritingResultVisibilityService
{
    private const string GlobalId = "global";

    public async Task<WritingResultVisibilityConfig> ResolveAsync(Guid? scenarioId, CancellationToken ct)
    {
        var global = await db.WritingResultVisibilityConfigs
            .FirstOrDefaultAsync(c => c.Id == GlobalId, ct);
        if (global is null)
        {
            // Create the global default row on first read. Entity defaults already
            // encode the spec defaults (AI estimate hidden, everything else shown).
            global = new WritingResultVisibilityConfig
            {
                Id = GlobalId,
                ScenarioId = null,
                UpdatedAt = clock.GetUtcNow(),
            };
            db.WritingResultVisibilityConfigs.Add(global);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Created default global writing result-visibility config");
        }

        if (scenarioId is { } sid)
        {
            var overrideRow = await db.WritingResultVisibilityConfigs
                .FirstOrDefaultAsync(c => c.ScenarioId == sid, ct);
            if (overrideRow is not null)
            {
                // Booleans have no tri-state, so an existing override row fully
                // represents the scenario's effective config (override over global).
                return overrideRow;
            }
        }

        return global;
    }

    public async Task<WritingResultVisibilityDto> ResolveDtoAsync(Guid? scenarioId, CancellationToken ct)
    {
        var config = await ResolveAsync(scenarioId, ct);
        return ToDto(config);
    }

    public async Task<WritingResultVisibilityDto> UpsertAsync(WritingResultVisibilityDto dto, Guid? scenarioId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        WritingResultVisibilityConfig row;

        if (scenarioId is { } sid)
        {
            row = await db.WritingResultVisibilityConfigs
                .FirstOrDefaultAsync(c => c.ScenarioId == sid, ct)
                ?? AddNew(new WritingResultVisibilityConfig { Id = sid.ToString(), ScenarioId = sid });
        }
        else
        {
            row = await db.WritingResultVisibilityConfigs
                .FirstOrDefaultAsync(c => c.Id == GlobalId, ct)
                ?? AddNew(new WritingResultVisibilityConfig { Id = GlobalId, ScenarioId = null });
        }

        ApplyDto(row, dto);
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Upserted writing result-visibility config {ConfigId} (scenario {ScenarioId})",
            row.Id,
            row.ScenarioId);

        return ToDto(row);

        WritingResultVisibilityConfig AddNew(WritingResultVisibilityConfig created)
        {
            db.WritingResultVisibilityConfigs.Add(created);
            return created;
        }
    }

    private static void ApplyDto(WritingResultVisibilityConfig row, WritingResultVisibilityDto dto)
    {
        // ScenarioId is governed by the upsert target, not the payload, so it is not copied here.
        row.ShowSubmissionReceived = dto.ShowSubmissionReceived;
        row.ShowAiEstimate = dto.ShowAiEstimate;
        row.ShowTutorScore = dto.ShowTutorScore;
        row.ShowFullCriteria = dto.ShowFullCriteria;
        row.ShowAnnotatedResponse = dto.ShowAnnotatedResponse;
        row.ShowMissingContent = dto.ShowMissingContent;
        row.ShowModelAnswer = dto.ShowModelAnswer;
        row.ShowContentChecklist = dto.ShowContentChecklist;
        row.AllowRewrite = dto.AllowRewrite;
    }

    private static WritingResultVisibilityDto ToDto(WritingResultVisibilityConfig c) => new(
        c.ScenarioId,
        c.ShowSubmissionReceived,
        c.ShowAiEstimate,
        c.ShowTutorScore,
        c.ShowFullCriteria,
        c.ShowAnnotatedResponse,
        c.ShowMissingContent,
        c.ShowModelAnswer,
        c.ShowContentChecklist,
        c.AllowRewrite,
        c.UpdatedAt);
}

// DTO (WS-B6). Mirrors lib/writing/types.ts WritingResultVisibilityDto (camelCase on the wire).
// No "id" field by design — the wire contract keys on the optional scenarioId.
public record WritingResultVisibilityDto(
    Guid? ScenarioId,
    bool ShowSubmissionReceived,
    bool ShowAiEstimate,
    bool ShowTutorScore,
    bool ShowFullCriteria,
    bool ShowAnnotatedResponse,
    bool ShowMissingContent,
    bool ShowModelAnswer,
    bool ShowContentChecklist,
    bool AllowRewrite,
    DateTimeOffset UpdatedAt);
