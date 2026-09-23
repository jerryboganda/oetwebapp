using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.FreeSamples;

namespace OetLearner.Api.Services;

/// <summary>
/// Resolves the one free Writing scenario and one free Speaking card for a
/// profession. Delegates to <see cref="FreeSampleService.ResolvePickAsync"/> —
/// the single source of truth for "which item is free" (admin designation /
/// FreeTierConfig selection while live, else the lowest-order live item of that
/// profession) — so the free tier and the free sample can never disagree, and
/// only the given profession's own item is ever returned.
/// </summary>
public interface IFreeTierContentResolver
{
    Task<WritingScenario?> ResolveWritingScenarioAsync(string? professionId, CancellationToken ct);
    Task<RolePlayCard?> ResolveSpeakingCardAsync(string? professionId, CancellationToken ct);
    Task<bool> IsFeaturedSpeakingCardAsync(string? professionId, string cardId, CancellationToken ct);
}

public sealed class FreeTierContentResolver(LearnerDbContext db) : IFreeTierContentResolver
{
    public async Task<WritingScenario?> ResolveWritingScenarioAsync(string? professionId, CancellationToken ct)
    {
        var pick = await new FreeSampleService(db).ResolvePickAsync(FreeSampleService.Writing, professionId, ct);
        return Guid.TryParse(pick, out var scenarioId)
            ? await db.WritingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scenarioId, ct)
            : null;
    }

    public async Task<RolePlayCard?> ResolveSpeakingCardAsync(string? professionId, CancellationToken ct)
    {
        var pick = await new FreeSampleService(db).ResolvePickAsync(FreeSampleService.Speaking, professionId, ct);
        return pick is null
            ? null
            : await db.RolePlayCards.AsNoTracking().FirstOrDefaultAsync(card => card.Id == pick, ct);
    }

    public async Task<bool> IsFeaturedSpeakingCardAsync(string? professionId, string cardId, CancellationToken ct)
        => string.Equals(
            await new FreeSampleService(db).ResolvePickAsync(FreeSampleService.Speaking, professionId, ct),
            cardId,
            StringComparison.Ordinal);
}
