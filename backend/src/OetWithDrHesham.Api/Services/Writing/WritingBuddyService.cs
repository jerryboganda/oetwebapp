using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Writing;

// ── DTOs surfaced to the endpoint layer ──────────────────────────────────────

// PartnerDisplayName = anonymised label for the other learner.
// IsUserA            = current learner is UserA on the pair row;
//                      the client uses this to render the right half of
//                      the weekly check-in.
public sealed record WritingBuddyPairView(
    Guid Id,
    string Profession,
    string MatchedAtBand,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EndedAt,
    string? EndedReason,
    string PartnerDisplayName,
    bool IsUserA);

// MineMessage = true when the current learner is the author. Used by
//               the UI to flip alignment without leaking the partner id.
public sealed record WritingBuddyMessageView(
    Guid Id,
    Guid PairId,
    string FromUserId,
    bool MineMessage,
    string BodyMarkdown,
    DateTimeOffset SentAt,
    DateTimeOffset? ReadAt);

public sealed record WritingBuddyCheckInView(
    Guid Id,
    Guid PairId,
    DateOnly WeekStartDate,
    string? MyReportJson,
    string? PartnerReportJson,
    DateTimeOffset? CompletedAt);

public sealed record WritingBuddyOptInResultView(bool OptedIn, Guid? ActivePairId);

// Status: `matched` if a partner was paired immediately, `queued`
//         if the learner is opted-in but no candidate was available.
public sealed record WritingBuddyMatchResultView(
    string Status,
    Guid? PairId,
    string? PartnerDisplayName);

// ── Service interface ────────────────────────────────────────────────────────

public interface IWritingBuddyService
{
    Task<WritingBuddyOptInResultView> OptInAsync(string userId, CancellationToken ct);
    Task<WritingBuddyMatchResultView> RequestMatchAsync(string userId, CancellationToken ct);
    Task<WritingBuddyPairView?> GetActivePairAsync(string userId, CancellationToken ct);
    Task<WritingBuddyMessageView> SendMessageAsync(string userId, Guid pairId, string body, CancellationToken ct);
    Task<IReadOnlyList<WritingBuddyMessageView>> GetMessagesAsync(string userId, Guid pairId, int take, CancellationToken ct);
    Task<WritingBuddyCheckInView> SubmitWeeklyCheckInAsync(string userId, Guid pairId, string reportJson, CancellationToken ct);
    Task<bool> EndPairAsync(string userId, Guid pairId, string reason, CancellationToken ct);
}

public sealed class WritingBuddyService(
    LearnerDbContext db,
    TimeProvider clock,
    ILogger<WritingBuddyService> logger) : IWritingBuddyService
{
    private const int MessageMaxLength = 500;
    private const int MessagesPerDayPerSender = 10;
    private const int DefaultMessageFetch = 50;

    // ────────────────────────────────────────────────────────────────────
    // OPT-IN
    // ────────────────────────────────────────────────────────────────────

    public async Task<WritingBuddyOptInResultView> OptInAsync(string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var profile = await db.LearnerWritingProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? throw ApiException.NotFound(
                "writing_profile_not_found",
                "Complete the Writing profile before opting into the Buddy System.");
        profile.OptInBuddy = true;
        profile.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        var active = await FindActivePairForUserAsync(userId, ct);
        return new WritingBuddyOptInResultView(true, active?.Id);
    }

    // ────────────────────────────────────────────────────────────────────
    // MATCHING — same profession, ±1 band, not already paired
    // ────────────────────────────────────────────────────────────────────

    public async Task<WritingBuddyMatchResultView> RequestMatchAsync(string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var profile = await db.LearnerWritingProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? throw ApiException.NotFound("writing_profile_not_found", "Profile required.");

        if (!profile.OptInBuddy)
        {
            throw ApiException.Conflict(
                "writing_buddy_not_opted_in",
                "Opt into the Buddy System before requesting a match.");
        }

        var existing = await FindActivePairForUserAsync(userId, ct);
        if (existing is not null)
        {
            return new WritingBuddyMatchResultView(
                "matched",
                existing.Id,
                BuildDisplayName(profile.Profession, profile.TargetCountry, existing.UserAId == userId ? existing.UserBId : existing.UserAId));
        }

        // Candidate pool: opted-in, same profession, band within ±1, no active pair.
        var profession = profile.Profession;
        var candidateBands = BandsWithinOne(profile.TargetBand);

        // Pre-filter on the indexed (Profession, OptInBuddy) signal.
        var candidates = await (
            from p in db.LearnerWritingProfiles.AsNoTracking()
            where p.UserId != userId
                  && p.OptInBuddy
                  && p.Profession == profession
                  && candidateBands.Contains(p.TargetBand)
            select new { p.UserId, p.TargetBand, p.Profession, p.TargetCountry })
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return new WritingBuddyMatchResultView("queued", null, null);
        }

        // Drop anyone who already has an active pair (could be either side).
        var candidateIds = candidates.Select(c => c.UserId).ToHashSet(StringComparer.Ordinal);
        var pairedSides = await db.WritingBuddyPairs.AsNoTracking()
            .Where(b => b.Status == "active"
                        && (candidateIds.Contains(b.UserAId) || candidateIds.Contains(b.UserBId)))
            .Select(b => new { b.UserAId, b.UserBId })
            .ToListAsync(ct);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in pairedSides)
        {
            taken.Add(pair.UserAId);
            taken.Add(pair.UserBId);
        }
        var available = candidates.Where(c => !taken.Contains(c.UserId)).ToList();
        if (available.Count == 0)
        {
            return new WritingBuddyMatchResultView("queued", null, null);
        }

        // Prefer exact-band match, then anything in the ±1 window.
        var pick = available.FirstOrDefault(c => string.Equals(c.TargetBand, profile.TargetBand, StringComparison.OrdinalIgnoreCase))
                   ?? available[0];

        var (a, b) = OrderUsers(userId, pick.UserId);
        var now = clock.GetUtcNow();
        var newPair = new WritingBuddyPair
        {
            Id = Guid.NewGuid(),
            UserAId = a,
            UserBId = b,
            CreatedAt = now,
            Profession = profession,
            MatchedAtBand = profile.TargetBand,
            Status = "active",
        };
        db.WritingBuddyPairs.Add(newPair);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Partial unique index race — another concurrent match grabbed it.
            logger.LogInformation(ex, "Buddy match race for {UserId}; rechecking.", userId);
            var nowExisting = await FindActivePairForUserAsync(userId, ct);
            if (nowExisting is not null)
            {
                return new WritingBuddyMatchResultView(
                    "matched",
                    nowExisting.Id,
                    BuildDisplayName(profile.Profession, profile.TargetCountry, nowExisting.UserAId == userId ? nowExisting.UserBId : nowExisting.UserAId));
            }
            return new WritingBuddyMatchResultView("queued", null, null);
        }

        return new WritingBuddyMatchResultView(
            "matched",
            newPair.Id,
            BuildDisplayName(profession, pick.TargetCountry, pick.UserId));
    }

    // ────────────────────────────────────────────────────────────────────
    // CURRENT PAIR
    // ────────────────────────────────────────────────────────────────────

    public async Task<WritingBuddyPairView?> GetActivePairAsync(string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var pair = await FindActivePairForUserAsync(userId, ct);
        if (pair is null) return null;
        var partnerId = pair.UserAId == userId ? pair.UserBId : pair.UserAId;
        var partnerProfile = await db.LearnerWritingProfiles.AsNoTracking()
            .Where(p => p.UserId == partnerId)
            .Select(p => new { p.Profession, p.TargetCountry })
            .FirstOrDefaultAsync(ct);
        return new WritingBuddyPairView(
            pair.Id,
            pair.Profession,
            pair.MatchedAtBand,
            pair.Status,
            pair.CreatedAt,
            pair.EndedAt,
            pair.EndedReason,
            BuildDisplayName(
                partnerProfile?.Profession ?? pair.Profession,
                partnerProfile?.TargetCountry,
                partnerId),
            IsUserA: pair.UserAId == userId);
    }

    // ────────────────────────────────────────────────────────────────────
    // MESSAGING
    // ────────────────────────────────────────────────────────────────────

    public async Task<WritingBuddyMessageView> SendMessageAsync(string userId, Guid pairId, string body, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (string.IsNullOrWhiteSpace(body))
        {
            throw ApiException.Validation("writing_buddy_empty_message", "Message body is required.");
        }
        if (body.Length > MessageMaxLength)
        {
            throw ApiException.Validation(
                "writing_buddy_message_too_long",
                $"Buddy messages are capped at {MessageMaxLength} characters.");
        }

        var pair = await LoadAndAuthoriseActivePairAsync(userId, pairId, ct);

        var since = clock.GetUtcNow().AddHours(-24);
        var sentLast24h = await db.WritingBuddyMessages
            .CountAsync(m => m.FromUserId == userId && m.SentAt >= since, ct);
        if (sentLast24h >= MessagesPerDayPerSender)
        {
            throw ApiException.TooManyRequests(
                "writing_buddy_rate_limited",
                $"You can send at most {MessagesPerDayPerSender} buddy messages per day.");
        }

        var msg = new WritingBuddyMessage
        {
            Id = Guid.NewGuid(),
            PairId = pair.Id,
            FromUserId = userId,
            BodyMarkdown = body.Trim(),
            SentAt = clock.GetUtcNow(),
        };
        db.WritingBuddyMessages.Add(msg);
        await db.SaveChangesAsync(ct);

        return new WritingBuddyMessageView(
            msg.Id, msg.PairId, msg.FromUserId, MineMessage: true,
            msg.BodyMarkdown, msg.SentAt, msg.ReadAt);
    }

    public async Task<IReadOnlyList<WritingBuddyMessageView>> GetMessagesAsync(string userId, Guid pairId, int take, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var safeTake = Math.Clamp(take <= 0 ? DefaultMessageFetch : take, 1, 200);
        // Authorise on the pair before reading the inbox.
        _ = await LoadAndAuthorisePairAsync(userId, pairId, allowEnded: true, ct: ct);

        var rows = await db.WritingBuddyMessages.AsNoTracking()
            .Where(m => m.PairId == pairId)
            .OrderByDescending(m => m.SentAt)
            .Take(safeTake)
            .ToListAsync(ct);

        // Side-effect: mark partner-authored messages as read.
        var nowUtc = clock.GetUtcNow();
        var unreadFromPartner = await db.WritingBuddyMessages
            .Where(m => m.PairId == pairId && m.FromUserId != userId && m.ReadAt == null)
            .ToListAsync(ct);
        if (unreadFromPartner.Count > 0)
        {
            foreach (var msg in unreadFromPartner)
            {
                msg.ReadAt = nowUtc;
            }
            await db.SaveChangesAsync(ct);
        }

        return rows.OrderBy(m => m.SentAt).Select(m => new WritingBuddyMessageView(
            m.Id,
            m.PairId,
            m.FromUserId,
            MineMessage: m.FromUserId == userId,
            m.BodyMarkdown,
            m.SentAt,
            m.FromUserId != userId ? nowUtc : m.ReadAt)).ToList();
    }

    // ────────────────────────────────────────────────────────────────────
    // WEEKLY CHECK-IN
    // ────────────────────────────────────────────────────────────────────

    public async Task<WritingBuddyCheckInView> SubmitWeeklyCheckInAsync(string userId, Guid pairId, string reportJson, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var pair = await LoadAndAuthoriseActivePairAsync(userId, pairId, ct);
        var weekStart = StartOfIsoWeek(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        var entry = await db.WritingBuddyCheckIns
            .FirstOrDefaultAsync(c => c.PairId == pairId && c.WeekStartDate == weekStart, ct);
        if (entry is null)
        {
            entry = new WritingBuddyCheckIn
            {
                Id = Guid.NewGuid(),
                PairId = pairId,
                WeekStartDate = weekStart,
            };
            db.WritingBuddyCheckIns.Add(entry);
        }

        var safeReport = string.IsNullOrWhiteSpace(reportJson) ? "{}" : reportJson;
        if (pair.UserAId == userId)
        {
            entry.UserAReportJson = safeReport;
        }
        else
        {
            entry.UserBReportJson = safeReport;
        }
        if (entry.UserAReportJson is not null && entry.UserBReportJson is not null && entry.CompletedAt is null)
        {
            entry.CompletedAt = clock.GetUtcNow();
        }
        await db.SaveChangesAsync(ct);

        var isUserA = pair.UserAId == userId;
        return new WritingBuddyCheckInView(
            entry.Id,
            entry.PairId,
            entry.WeekStartDate,
            MyReportJson: isUserA ? entry.UserAReportJson : entry.UserBReportJson,
            PartnerReportJson: isUserA ? entry.UserBReportJson : entry.UserAReportJson,
            entry.CompletedAt);
    }

    // ────────────────────────────────────────────────────────────────────
    // END PAIR
    // ────────────────────────────────────────────────────────────────────

    public async Task<bool> EndPairAsync(string userId, Guid pairId, string reason, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var pair = await LoadAndAuthoriseActivePairAsync(userId, pairId, ct);
        pair.Status = "ended";
        pair.EndedAt = clock.GetUtcNow();
        pair.EndedReason = string.IsNullOrWhiteSpace(reason) ? "user_ended" : reason[..Math.Min(reason.Length, 64)];
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────

    private async Task<WritingBuddyPair?> FindActivePairForUserAsync(string userId, CancellationToken ct)
    {
        return await db.WritingBuddyPairs
            .Where(p => p.Status == "active" && (p.UserAId == userId || p.UserBId == userId))
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<WritingBuddyPair> LoadAndAuthoriseActivePairAsync(string userId, Guid pairId, CancellationToken ct)
        => await LoadAndAuthorisePairAsync(userId, pairId, allowEnded: false, ct);

    private async Task<WritingBuddyPair> LoadAndAuthorisePairAsync(string userId, Guid pairId, bool allowEnded, CancellationToken ct)
    {
        var pair = await db.WritingBuddyPairs.FirstOrDefaultAsync(p => p.Id == pairId, ct)
            ?? throw ApiException.NotFound("writing_buddy_pair_not_found", "Buddy pair was not found.");
        if (pair.UserAId != userId && pair.UserBId != userId)
        {
            throw ApiException.Forbidden("writing_buddy_pair_forbidden", "You are not part of this pair.");
        }
        if (!allowEnded && pair.Status != "active")
        {
            throw ApiException.Conflict("writing_buddy_pair_inactive", "This buddy pair is no longer active.");
        }
        return pair;
    }

    private static (string a, string b) OrderUsers(string left, string right)
        => string.CompareOrdinal(left, right) <= 0 ? (left, right) : (right, left);

    // ±1 band — `bandLabel` is a short ladder ("A", "B+", "B", "C+", "C", "D", "E").
    private static readonly string[] BandLadder = ["A", "B+", "B", "C+", "C", "D", "E"];

    private static IReadOnlyList<string> BandsWithinOne(string targetBand)
    {
        var idx = Array.IndexOf(BandLadder, targetBand);
        if (idx < 0)
        {
            // Unknown band — fall back to exact match to avoid pairing too widely.
            return new[] { targetBand };
        }
        var bands = new List<string>(3) { BandLadder[idx] };
        if (idx > 0) bands.Add(BandLadder[idx - 1]);
        if (idx < BandLadder.Length - 1) bands.Add(BandLadder[idx + 1]);
        return bands;
    }

    private static string BuildDisplayName(string profession, string? country, string fallbackUserId)
    {
        var roleLabel = profession switch
        {
            "medicine" => "Doctor",
            "nursing" => "Nurse",
            "dentistry" => "Dentist",
            "pharmacy" => "Pharmacist",
            "physiotherapy" => "Physiotherapist",
            "occupational-therapy" => "Occupational Therapist",
            "podiatry" => "Podiatrist",
            "optometry" => "Optometrist",
            "veterinary" => "Veterinarian",
            "radiography" => "Radiographer",
            "speech-pathology" => "Speech Pathologist",
            "dietetics" => "Dietician",
            _ => "Healthcare Professional",
        };
        if (!string.IsNullOrWhiteSpace(country))
        {
            return $"Anonymous {roleLabel} ({country.ToUpperInvariant()})";
        }
        // Deterministic suffix so the partner sees the same label between page loads.
        var hash = Math.Abs(string.GetHashCode(fallbackUserId, StringComparison.Ordinal)) % 1000;
        return string.Create(CultureInfo.InvariantCulture, $"Anonymous {roleLabel} #{hash:D3}");
    }

    /// <summary>
    /// ISO-8601 Monday-start of the week containing <paramref name="date"/>.
    /// </summary>
    private static DateOnly StartOfIsoWeek(DateOnly date)
    {
        int diff = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-diff);
    }
}
