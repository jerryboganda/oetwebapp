using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

public sealed record SpeakingPatientTurnReplay(bool IsDuplicate, string ResponseJson, int SequenceNumber);

public interface ISpeakingPatientTurnService
{
    Task<SpeakingPatientTurnReplay?> TryReplayAsync(string sessionId, string? clientTurnId, CancellationToken ct);

    Task<SpeakingPatientTurn> PersistAsync(
        string sessionId,
        string? clientTurnId,
        string role,
        string text,
        object response,
        CancellationToken ct);

    Task AppendSummaryAsync(string sessionId, string candidateText, string patientText, CancellationToken ct);
}

public sealed class SpeakingPatientTurnService(LearnerDbContext db, TimeProvider clock) : ISpeakingPatientTurnService
{
    private const int MaxSummaryChars = 4000;

    public async Task<SpeakingPatientTurnReplay?> TryReplayAsync(
        string sessionId, string? clientTurnId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientTurnId)) return null;
        var row = await db.SpeakingPatientTurns.AsNoTracking()
            .FirstOrDefaultAsync(t => t.SessionId == sessionId && t.ClientTurnId == clientTurnId, ct);
        if (row is null) return null;
        return new SpeakingPatientTurnReplay(true, row.ResponseJson, row.SequenceNumber);
    }

    public async Task<SpeakingPatientTurn> PersistAsync(
        string sessionId,
        string? clientTurnId,
        string role,
        string text,
        object response,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(clientTurnId))
        {
            var existing = await db.SpeakingPatientTurns
                .FirstOrDefaultAsync(t => t.SessionId == sessionId && t.ClientTurnId == clientTurnId, ct);
            if (existing is not null) return existing;
        }

        var next = await db.SpeakingPatientTurns
            .Where(t => t.SessionId == sessionId)
            .Select(t => (int?)t.SequenceNumber)
            .MaxAsync(ct) ?? 0;

        var row = new SpeakingPatientTurn
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionId = sessionId,
            ClientTurnId = string.IsNullOrWhiteSpace(clientTurnId) ? null : clientTurnId.Trim(),
            SequenceNumber = next + 1,
            Role = string.IsNullOrWhiteSpace(role) ? "patient" : role,
            Text = text ?? string.Empty,
            ResponseJson = JsonSerializer.Serialize(response),
            CreatedAt = clock.GetUtcNow(),
        };
        db.SpeakingPatientTurns.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.Entry(row).State = EntityState.Detached;
            if (!string.IsNullOrWhiteSpace(clientTurnId))
            {
                var raced = await db.SpeakingPatientTurns
                    .FirstOrDefaultAsync(t => t.SessionId == sessionId && t.ClientTurnId == clientTurnId, ct);
                if (raced is not null) return raced;
            }

            throw;
        }

        return row;
    }

    public async Task AppendSummaryAsync(string sessionId, string candidateText, string patientText, CancellationToken ct)
    {
        var session = await db.SpeakingSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return;
        var addition = $"C: {Trim(candidateText)}\nP: {Trim(patientText)}\n";
        var combined = (session.ConversationSummaryText ?? string.Empty) + addition;
        if (combined.Length > MaxSummaryChars)
        {
            combined = combined[^MaxSummaryChars..];
        }

        session.ConversationSummaryText = combined;
        session.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    private static string Trim(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var t = value.Trim();
        return t.Length <= 280 ? t : t[..280];
    }
}
