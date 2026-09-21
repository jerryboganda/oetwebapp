using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.FreeSamples;

/// <summary>One profession's free sample as offered to a learner.</summary>
/// <param name="ProfessionId">Normalised profession id (lower-case, hyphenated).</param>
/// <param name="ContentId">Writing: scenario id ("D"). Speaking: role-play card id.</param>
/// <param name="State"><c>available</c> | <c>in_progress</c> | <c>used</c></param>
public sealed record FreeSampleOffer(string ProfessionId, string ContentId, string State);

/// <summary>
/// Free Mocks (owner 2026-09-22): every learner gets ONE free AI-graded Writing
/// attempt and ONE free AI-graded Speaking attempt. The server alone decides
/// what is free — the designated item of the profession, once per learner per
/// subtest — and the client never sends a "free" flag.
/// </summary>
public interface IFreeSampleService
{
    /// <summary>Offers for the learner: every profession with a live sample while
    /// unclaimed; only the claimed profession once the learner has started.</summary>
    Task<IReadOnlyList<FreeSampleOffer>> ListAsync(string userId, string subtest, CancellationToken ct);

    /// <summary>Read-only: is <paramref name="contentRef"/> the free sample this
    /// learner may start (or resume) right now? Writing: scenario id. Speaking:
    /// role-play card id or its content-item id.</summary>
    Task<bool> IsOfferedAsync(string userId, string subtest, string contentRef, CancellationToken ct);

    /// <summary>Atomically claims (or re-binds an unspent claim to) the learner's
    /// single sample for <paramref name="attemptId"/>. True when it may run free.</summary>
    Task<bool> TryClaimAsync(string userId, string subtest, string contentRef, string attemptId, CancellationToken ct);

    /// <summary>True when <paramref name="attemptId"/> is the attempt/submission
    /// bound to the learner's free-sample claim (server truth, used to arm the
    /// AI quota bypass at grading time).</summary>
    Task<bool> IsFreeAttemptAsync(string userId, string subtest, string? attemptId, CancellationToken ct);
}

public sealed class FreeSampleService(LearnerDbContext db) : IFreeSampleService
{
    public const string Writing = "writing";
    public const string Speaking = "speaking";

    /// <summary>Master switch (dark launch + kill switch): free samples are granted
    /// ONLY while a <c>FeatureFlag</c> row with this key exists and is Enabled.
    /// Absent row or Enabled=false = OFF, so shipping the code changes nothing until
    /// an admin turns it on, and flipping it off stops every new free grant.</summary>
    public const string FeatureFlagKey = "free_samples_enabled";

    public const string StateAvailable = "available";
    public const string StateInProgress = "in_progress";
    public const string StateUsed = "used";

    public static bool IsSupported(string? subtest) => subtest is Writing or Speaking;

    /// <summary>Lower-case + hyphenated so "Medicine", "occupational_therapy" and
    /// "occupational-therapy" all compare equal across the three profession vocabularies.</summary>
    public static string NormalizeProfession(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-');

    public async Task<IReadOnlyList<FreeSampleOffer>> ListAsync(string userId, string subtest, CancellationToken ct)
    {
        if (!IsSupported(subtest) || !await IsEnabledAsync(ct)) return [];

        var claim = await GetClaimAsync(userId, subtest, ct);
        if (claim is not null)
        {
            var state = await GetBoundStateAsync(claim, ct) == BoundState.Done ? StateUsed : StateInProgress;
            return [new FreeSampleOffer(claim.Profession, claim.ContentId, state)];
        }

        var picks = await ResolvePicksAsync(subtest, ct);
        return picks
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new FreeSampleOffer(p.Key, p.Value, StateAvailable))
            .ToList();
    }

    public async Task<bool> IsOfferedAsync(string userId, string subtest, string contentRef, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId) || !IsSupported(subtest) || !await IsEnabledAsync(ct)) return false;
        var content = await ResolveContentAsync(subtest, contentRef, ct);
        if (content is null) return false;

        var claim = await GetClaimAsync(userId, subtest, ct);
        if (claim is not null)
        {
            // Continuing the SAME sample is fine until it is spent (a designation
            // change after the claim must not strand a learner mid-sample).
            return claim.ContentId == content.ContentId
                && await GetBoundStateAsync(claim, ct) != BoundState.Done;
        }

        var picks = await ResolvePicksAsync(subtest, ct);
        return picks.TryGetValue(content.Profession, out var pick) && pick == content.ContentId;
    }

    public async Task<bool> TryClaimAsync(
        string userId, string subtest, string contentRef, string attemptId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(attemptId)
            || !IsSupported(subtest) || !await IsEnabledAsync(ct))
        {
            return false;
        }
        var content = await ResolveContentAsync(subtest, contentRef, ct);
        if (content is null) return false;

        var claim = await GetClaimAsync(userId, subtest, ct);
        if (claim is not null) return await RebindAsync(claim, content, attemptId, ct);

        var picks = await ResolvePicksAsync(subtest, ct);
        if (!picks.TryGetValue(content.Profession, out var pick) || pick != content.ContentId) return false;

        var now = DateTimeOffset.UtcNow;
        var row = new FreeSampleClaim
        {
            Id = $"fsc-{Guid.NewGuid():N}",
            UserId = userId,
            Subtest = subtest,
            Profession = content.Profession,
            ContentId = content.ContentId,
            AttemptId = attemptId,
            ClaimedAt = now,
            UpdatedAt = now,
        };
        db.FreeSampleClaims.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // Lost the UNIQUE(UserId, Subtest) race (two tabs / double submit):
            // re-read the winner and treat an identical claim as idempotent.
            db.Entry(row).State = EntityState.Detached;
            var raced = await GetClaimAsync(userId, subtest, ct);
            if (raced is null) throw;
            return raced.ContentId == content.ContentId && raced.AttemptId == attemptId;
        }
    }

    public async Task<bool> IsFreeAttemptAsync(string userId, string subtest, string? attemptId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(attemptId) || !IsSupported(subtest)) return false;
        return await db.FreeSampleClaims.AsNoTracking()
            .AnyAsync(c => c.UserId == userId && c.Subtest == subtest && c.AttemptId == attemptId, ct);
    }

    // ── internals ────────────────────────────────────────────────────────────

    private enum BoundState { None, Active, Done, Dead }

    private sealed record ResolvedContent(string Profession, string ContentId);

    private async Task<bool> RebindAsync(FreeSampleClaim claim, ResolvedContent content, string attemptId, CancellationToken ct)
    {
        if (claim.ContentId != content.ContentId) return false;
        if (claim.AttemptId == attemptId) return true; // idempotent (retry-grade, double submit)

        var state = await GetBoundStateAsync(claim, ct);
        // Spent, or another attempt is still live: nothing more is free.
        if (state is BoundState.Done or BoundState.Active) return false;

        // Nothing usable was bound (never started, or its grading failed): the
        // learner's one sample is still unspent, so it moves to this attempt.
        claim.AttemptId = attemptId;
        claim.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Where the claim's bound attempt/submission stands. Derived from the
    /// attempt itself (no completion hooks): graded/evaluating = Done (spent),
    /// failed/abandoned/missing = Dead (re-usable), otherwise Active.</summary>
    private async Task<BoundState> GetBoundStateAsync(FreeSampleClaim claim, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(claim.AttemptId)) return BoundState.None;

        if (claim.Subtest == Speaking)
        {
            var state = await db.Attempts.AsNoTracking()
                .Where(a => a.Id == claim.AttemptId)
                .Select(a => (AttemptState?)a.State)
                .FirstOrDefaultAsync(ct);
            return state switch
            {
                null => BoundState.Dead,
                AttemptState.Submitted or AttemptState.Evaluating or AttemptState.Completed => BoundState.Done,
                AttemptState.Failed or AttemptState.Abandoned => BoundState.Dead,
                _ => BoundState.Active,
            };
        }

        if (!Guid.TryParse(claim.AttemptId, out var submissionId)) return BoundState.Dead;
        var status = await db.WritingSubmissions.AsNoTracking()
            .Where(s => s.Id == submissionId)
            .Select(s => s.Status)
            .FirstOrDefaultAsync(ct);
        return status switch
        {
            null => BoundState.Dead,
            WritingSubmissionStatuses.Graded => BoundState.Done,
            WritingSubmissionStatuses.Failed => BoundState.Dead,
            _ => BoundState.Active,
        };
    }

    private Task<FreeSampleClaim?> GetClaimAsync(string userId, string subtest, CancellationToken ct)
        => db.FreeSampleClaims.FirstOrDefaultAsync(c => c.UserId == userId && c.Subtest == subtest, ct);

    private async Task<bool> IsEnabledAsync(CancellationToken ct)
    {
        var flag = await db.FeatureFlags.AsNoTracking()
            .Where(f => f.Key == FeatureFlagKey)
            .OrderByDescending(f => f.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        return flag?.Enabled ?? false; // fail CLOSED: a billing bypass must be switched on deliberately
    }

    private async Task<ResolvedContent?> ResolveContentAsync(string subtest, string contentRef, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(contentRef)) return null;

        if (subtest == Writing)
        {
            if (!Guid.TryParse(contentRef, out var scenarioId)) return null;
            var profession = await db.WritingScenarios.AsNoTracking()
                .Where(s => s.Id == scenarioId)
                .Select(s => s.Profession)
                .FirstOrDefaultAsync(ct);
            return profession is null
                ? null
                : new ResolvedContent(NormalizeProfession(profession), scenarioId.ToString("D"));
        }

        var card = await db.RolePlayCards.AsNoTracking()
            .Where(c => c.Id == contentRef || c.ContentItemId == contentRef)
            .Select(c => new { c.Id, c.ProfessionId })
            .FirstOrDefaultAsync(ct);
        return card is null ? null : new ResolvedContent(NormalizeProfession(card.ProfessionId), card.Id);
    }

    /// <summary>profession → the content id offered as its free sample. An admin
    /// designation wins while its item is still live; otherwise (no row, or a stale
    /// one) the lowest-order live item of the profession is auto-picked.</summary>
    private async Task<IReadOnlyDictionary<string, string>> ResolvePicksAsync(string subtest, CancellationToken ct)
    {
        var live = subtest == Writing ? await LoadLiveWritingAsync(ct) : await LoadLiveSpeakingAsync(ct);
        var picks = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var candidate in live)
        {
            picks.TryAdd(candidate.Profession, candidate.ContentId); // `live` is already lowest-order first
        }

        var designations = await db.FreeSampleDesignations.AsNoTracking()
            .Where(d => d.Subtest == subtest)
            .ToListAsync(ct);
        foreach (var designation in designations)
        {
            var profession = NormalizeProfession(designation.Profession);
            if (live.Any(l => l.Profession == profession && l.ContentId == designation.ContentId))
            {
                picks[profession] = designation.ContentId;
            }
        }
        return picks;
    }

    /// <summary>Published + candidate-loadable Writing tasks, in learner-library
    /// order (Difficulty, Title, Id) so the auto-pick is what a learner sees first.</summary>
    private async Task<List<ResolvedContent>> LoadLiveWritingAsync(CancellationToken ct)
    {
        var rows = await db.WritingScenarios.AsNoTracking()
            .Where(s => s.Status == "published")
            .Select(s => new
            {
                s.Id,
                s.Profession,
                s.Difficulty,
                s.Title,
                // Same rule as WritingScenarioService.IsCandidateLoadable.
                HasReadable = (s.TaskPromptMarkdown != null && s.TaskPromptMarkdown.Trim() != "")
                    || (s.StimulusPdfMediaAssetId != null && s.StimulusPdfMediaAssetId.Trim() != ""),
                HasCaseNotes = db.WritingScenarioStructuredSentences.Any(x => x.ScenarioId == s.Id),
            })
            .ToListAsync(ct);
        return rows
            .Where(r => r.HasReadable && r.HasCaseNotes && !string.IsNullOrWhiteSpace(r.Profession))
            .OrderBy(r => r.Difficulty)
            .ThenBy(r => r.Title, StringComparer.Ordinal)
            .ThenBy(r => r.Id)
            .Select(r => new ResolvedContent(NormalizeProfession(r.Profession), r.Id.ToString("D")))
            .ToList();
    }

    /// <summary>Published role-play cards whose content-item shell is also published
    /// (grading needs both), ordered by card number, then creation, then id.</summary>
    private async Task<List<ResolvedContent>> LoadLiveSpeakingAsync(CancellationToken ct)
    {
        var rows = await (
            from card in db.RolePlayCards.AsNoTracking()
            join item in db.ContentItems.AsNoTracking() on card.ContentItemId equals item.Id
            where card.Status == ContentStatus.Published
                && item.Status == ContentStatus.Published
                && item.SubtestCode == Speaking
            select new { card.Id, card.ProfessionId, card.DisplayCardNumber, card.CreatedAt })
            .ToListAsync(ct);
        return rows
            .Where(r => !string.IsNullOrWhiteSpace(r.ProfessionId))
            .OrderBy(r => r.DisplayCardNumber ?? int.MaxValue)
            .ThenBy(r => r.CreatedAt)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new ResolvedContent(NormalizeProfession(r.ProfessionId), r.Id))
            .ToList();
    }
}
