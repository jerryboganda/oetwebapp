using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// W4 — resume path for a leased <see cref="AiOperation"/>.
///
/// <para>
/// If the row is already <see cref="AiOperationState.ProviderSucceeded"/>,
/// complete the domain write from the persisted <see cref="AiOperation.ResultRef"/>
/// and <b>never</b> call a provider. Interactive execution stays in-request
/// via <c>IAiExecutionCoordinator</c> (W2). Feature dispatch for
/// <see cref="AiOperationState.Queued"/> / <see cref="AiOperationState.RetryScheduled"/>
/// arrives with W5–W9; W4 only recovers leases and resumes post-provider work.
/// </para>
/// </summary>
public interface IAiLeasedOperationHandler
{
    /// <summary>Number of provider invocations made by this handler instance.
    /// Production handler is always zero — resume never re-calls the model.</summary>
    int ProviderInvocationCount { get; }

    Task HandleAsync(AiLeasedOperation leased, CancellationToken ct);
}

public sealed class AiLeasedOperationHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<AiLeasedOperationHandler> logger) : IAiLeasedOperationHandler
{
    public int ProviderInvocationCount { get; private set; }

    public async Task HandleAsync(AiLeasedOperation leased, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var row = await db.AiOperations.FirstOrDefaultAsync(o => o.Id == leased.Id, ct);
        if (row is null)
        {
            return;
        }

        if (row.State == AiOperationState.ProviderSucceeded)
        {
            await CompleteFromPersistedAsync(db, row, ct);
            return;
        }

        // Queued/retry rows stay leased until a later wave registers a
        // feature dispatcher. Do not invent a second provider call here.
        logger.LogInformation(
            "AI operation {OperationId} leased in state {State}; W4 resume-only handler does not invoke a provider.",
            row.Id,
            row.State);
    }

    private static async Task CompleteFromPersistedAsync(
        LearnerDbContext db,
        AiOperation row,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(row.ResultRef))
        {
            // Persist-before-commit left us without a result pointer. Stay
            // ProviderSucceeded so a later worker can retry the domain write
            // — still zero provider calls.
            row.LeaseOwner = null;
            row.LeaseExpiresAt = null;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        row.State = AiOperationState.Completed;
        row.LeaseOwner = null;
        row.LeaseExpiresAt = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
