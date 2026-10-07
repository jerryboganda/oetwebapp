using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Live AI Credit action pricing (SAMI §9.1): "Sami reads these charges from
/// the live entitlement/credit configuration service. The values above are the
/// current handover baseline, not constants embedded in prompts or code."
/// Cached per request scope; admins edit through /v1/admin/ai/credit-costs.
/// </summary>
public interface IAiCreditCostService
{
    Task<IReadOnlyList<AiCreditCost>> GetAllAsync(CancellationToken ct);

    /// <summary>The charge for an action, or null when the action code is unknown.</summary>
    Task<AiCreditCost?> GetAsync(string actionCode, CancellationToken ct);

    /// <summary>Upserts one action's cost. Rejects negative credits.</summary>
    Task<AiCreditCost> UpsertAsync(string actionCode, int credits, bool enabled, string description, string? adminId, CancellationToken ct);

    /// <summary>Seeds any missing baseline rows (idempotent; runs at startup).</summary>
    Task SeedDefaultsAsync(CancellationToken ct);
}

public sealed class AiCreditCostService(LearnerDbContext db, TimeProvider clock) : IAiCreditCostService
{
    public static readonly string WritingAssessment = "writing.assessment";
    public static readonly string SpeakingRoleCard = "speaking.role_card";
    public static readonly string SpeakingExamFull = "speaking.exam_full";
    public static readonly string ReadingAnalysis = "reading.analysis";
    public static readonly string ListeningAnalysis = "listening.analysis";
    public static readonly string ListeningPartA = "listening.part_a";
    public static readonly string PdfDeepAnalysis = "pdf.deep_analysis";
    public static readonly string VoiceLive = "voice.live";

    /// <summary>The handover baseline (SAMI PDF §9.1). Seeded once; admin rows win afterwards.</summary>
    internal static readonly (string Code, int Credits, bool Enabled, string Description)[] Baseline =
    [
        (WritingAssessment, 2, true, "One Writing case note / letter assessment"),
        (SpeakingRoleCard, 2, true, "One Speaking role card assessment"),
        (SpeakingExamFull, 4, true, "Full two-card Speaking exam assessment"),
        (ReadingAnalysis, 1, true, "Full Reading exam analysis"),
        (ListeningAnalysis, 1, true, "Full Listening exam analysis"),
        (ListeningPartA, 1, true, "Listening Part A analysis"),
        (PdfDeepAnalysis, 0, false, "Large PDF deep analysis (configurable; charge shown before start)"),
        (VoiceLive, 0, false, "Live voice role play / extended audio (configurable conversion)"),
    ];

    public async Task<IReadOnlyList<AiCreditCost>> GetAllAsync(CancellationToken ct)
        => await db.AiCreditCosts.AsNoTracking()
            .OrderBy(c => c.ActionCode)
            .ToListAsync(ct);

    public async Task<AiCreditCost?> GetAsync(string actionCode, CancellationToken ct)
        => await db.AiCreditCosts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ActionCode == actionCode, ct);

    public async Task<AiCreditCost> UpsertAsync(string actionCode, int credits, bool enabled, string description, string? adminId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actionCode)) throw new ArgumentException("Action code is required.");
        if (credits < 0) throw new ArgumentException("Credits cannot be negative.");
        var row = await db.AiCreditCosts.FirstOrDefaultAsync(c => c.ActionCode == actionCode, ct);
        if (row is null)
        {
            row = new AiCreditCost { ActionCode = actionCode.Trim().ToLowerInvariant() };
            db.AiCreditCosts.Add(row);
        }
        row.Credits = credits;
        row.Enabled = enabled;
        row.Description = description.Length > 256 ? description[..256] : description;
        row.UpdatedByAdminId = adminId;
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task SeedDefaultsAsync(CancellationToken ct)
    {
        var existing = await db.AiCreditCosts.AsNoTracking()
            .Select(c => c.ActionCode)
            .ToListAsync(ct);
        var known = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = clock.GetUtcNow();
        var added = 0;
        foreach (var (code, credits, enabled, description) in Baseline)
        {
            if (known.Contains(code)) continue;
            db.AiCreditCosts.Add(new AiCreditCost
            {
                ActionCode = code,
                Credits = credits,
                Enabled = enabled,
                Description = description,
                UpdatedAt = now,
            });
            added += 1;
        }
        if (added > 0) await db.SaveChangesAsync(ct);
    }
}
