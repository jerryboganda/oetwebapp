using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts.Classes;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Domain.Classes;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.Classes;

/// <summary>
/// CRUD + availability + earnings calculation for the Zoom-backed tutor stack.
/// See OET_ZOOM_INTEGRATION_PLAN.md §7-§9. Earnings are computed in USD using
/// a configurable credit USD rate and revenue-share percentage (defaults per
/// plan §29 open decisions: 1 credit = 1 USD, tutor share = 70%).
/// </summary>
public sealed class TutorService(
    LearnerDbContext db,
    IRuntimeSettingsProvider runtimeSettings,
    TimeProvider timeProvider,
    ILogger<TutorService> logger) : ITutorService
{
    /// <summary>Default tutor revenue share when no override is configured. Plan §29 OD-7.</summary>
    public const decimal DefaultRevenueSharePercent = 0.70m;

    /// <summary>Default USD value per credit when no override is configured. Plan §29 OD-7.</summary>
    public const decimal DefaultCreditUsdValue = 1.00m;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TutorProfileDto> GetByUserIdAsync(string userId, CancellationToken ct)
    {
        var tutor = await db.Tutors.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == userId, ct)
            ?? throw ApiException.NotFound("tutor_not_found", "Tutor profile not found.");
        return MapTutor(tutor);
    }

    public async Task<TutorProfileDto> CreateAsync(string userId, TutorUpsertRequest request, CancellationToken ct)
    {
        var existing = await db.Tutors.FirstOrDefaultAsync(t => t.UserId == userId, ct);
        if (existing is not null)
        {
            throw ApiException.Conflict("tutor_already_exists", "A tutor profile already exists for this user.");
        }

        ValidateUpsertRequest(request);
        var now = timeProvider.GetUtcNow();
        var tutor = new Tutor
        {
            Id = $"TUT-{Guid.NewGuid():N}",
            UserId = userId,
            DisplayName = request.DisplayName.Trim(),
            DisplayNameAr = NormalizeOptional(request.DisplayNameAr),
            Bio = NormalizeOptional(request.Bio) ?? string.Empty,
            BioAr = NormalizeOptional(request.BioAr),
            AvatarUrl = NormalizeOptional(request.AvatarUrl),
            SpecialtiesJson = SerializeStringArray(request.Specialties),
            LanguagesJson = SerializeStringArray(request.Languages),
            HourlyRateUsd = request.HourlyRateUsd is > 0m ? request.HourlyRateUsd : null,
            TimeZone = NormalizeOptional(request.TimeZone) ?? "UTC",
            IsActive = request.IsActive ?? true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Tutors.Add(tutor);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created tutor profile {TutorId} for user {UserId}", tutor.Id, userId);
        return MapTutor(tutor);
    }

    public async Task<TutorProfileDto> UpdateAsync(string userId, TutorUpsertRequest request, CancellationToken ct)
    {
        ValidateUpsertRequest(request);
        var tutor = await db.Tutors.FirstOrDefaultAsync(t => t.UserId == userId, ct)
            ?? throw ApiException.NotFound("tutor_not_found", "Tutor profile not found.");

        tutor.DisplayName = request.DisplayName.Trim();
        tutor.DisplayNameAr = NormalizeOptional(request.DisplayNameAr);
        if (request.Bio is not null)
        {
            tutor.Bio = NormalizeOptional(request.Bio) ?? string.Empty;
        }

        if (request.BioAr is not null)
        {
            tutor.BioAr = NormalizeOptional(request.BioAr);
        }

        if (request.AvatarUrl is not null)
        {
            tutor.AvatarUrl = NormalizeOptional(request.AvatarUrl);
        }

        if (request.Specialties is not null)
        {
            tutor.SpecialtiesJson = SerializeStringArray(request.Specialties);
        }

        if (request.Languages is not null)
        {
            tutor.LanguagesJson = SerializeStringArray(request.Languages);
        }

        if (request.HourlyRateUsd.HasValue)
        {
            tutor.HourlyRateUsd = request.HourlyRateUsd > 0m ? request.HourlyRateUsd : null;
        }

        if (!string.IsNullOrWhiteSpace(request.TimeZone))
        {
            tutor.TimeZone = request.TimeZone.Trim();
        }

        if (request.IsActive.HasValue)
        {
            tutor.IsActive = request.IsActive.Value;
        }

        tutor.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return MapTutor(tutor);
    }

    public async Task<IReadOnlyList<TutorAvailabilityDto>> GetAvailabilityAsync(string userId, CancellationToken ct)
    {
        var tutor = await db.Tutors.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == userId, ct)
            ?? throw ApiException.NotFound("tutor_not_found", "Tutor profile not found.");

        var slots = await db.TutorAvailabilities.AsNoTracking()
            .Where(slot => slot.TutorId == tutor.Id)
            .OrderBy(slot => slot.DayOfWeek)
            .ThenBy(slot => slot.StartTime)
            .ToListAsync(ct);

        return slots.Select(MapSlot).ToList();
    }

    public async Task<IReadOnlyList<TutorAvailabilityDto>> ReplaceAvailabilityAsync(
        string userId,
        IReadOnlyList<TutorAvailabilityUpsertRequest> slots,
        CancellationToken ct)
    {
        var tutor = await db.Tutors.FirstOrDefaultAsync(t => t.UserId == userId, ct)
            ?? throw ApiException.NotFound("tutor_not_found", "Tutor profile not found.");

        foreach (var slot in slots)
        {
            if (slot.EndTime <= slot.StartTime)
            {
                throw ApiException.Validation("tutor_availability_invalid_range", "Availability end time must be after start time.");
            }
        }

        // Detect overlap within the same day before persisting.
        foreach (var grouping in slots.GroupBy(s => s.DayOfWeek))
        {
            var ordered = grouping.OrderBy(s => s.StartTime).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].StartTime < ordered[i - 1].EndTime)
                {
                    throw ApiException.Validation("tutor_availability_overlap", "Availability slots cannot overlap on the same day.");
                }
            }
        }

        var existing = await db.TutorAvailabilities.Where(slot => slot.TutorId == tutor.Id).ToListAsync(ct);
        db.TutorAvailabilities.RemoveRange(existing);

        foreach (var request in slots)
        {
            db.TutorAvailabilities.Add(new TutorAvailability
            {
                Id = $"TAV-{Guid.NewGuid():N}",
                TutorId = tutor.Id,
                DayOfWeek = request.DayOfWeek,
                StartTime = request.StartTime,
                EndTime = request.EndTime,
                IsActive = request.IsActive,
            });
        }

        tutor.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);

        return await GetAvailabilityAsync(userId, ct);
    }

    public async Task<TutorEarningsDto> GetEarningsAsync(string userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var tutor = await db.Tutors.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == userId, ct)
            ?? throw ApiException.NotFound("tutor_not_found", "Tutor profile not found.");

        // A tutor "owns" a class when the underlying LiveClass.TutorProfileId
        // points to a PrivateSpeakingTutorProfile whose ExpertUserId matches
        // the tutor's UserId (the bridge for now until LiveClass.TutorId is
        // added in a later wave — see plan §9.2 TBD).
        var ownedClassIds = await db.LiveClasses.AsNoTracking()
            .Where(liveClass => liveClass.TutorProfile != null && liveClass.TutorProfile.ExpertUserId == userId)
            .Select(liveClass => liveClass.Id)
            .ToListAsync(ct);

        if (ownedClassIds.Count == 0)
        {
            return new TutorEarningsDto(from, to, 0m, 0m, DefaultRevenueSharePercent, []);
        }

        var sessionsQuery = db.LiveClassSessions.AsNoTracking()
            .Where(session => ownedClassIds.Contains(session.LiveClassId));
        if (from.HasValue)
        {
            sessionsQuery = sessionsQuery.Where(session => session.ScheduledStartAt >= from.Value);
        }

        if (to.HasValue)
        {
            sessionsQuery = sessionsQuery.Where(session => session.ScheduledStartAt <= to.Value);
        }

        var sessions = await sessionsQuery
            .Include(session => session.LiveClass)
            .OrderBy(session => session.ScheduledStartAt)
            .ToListAsync(ct);

        var revenueShare = DefaultRevenueSharePercent;
        var creditUsdValue = DefaultCreditUsdValue;
        var lines = new List<TutorEarningsLineDto>(sessions.Count);
        var totalGross = 0m;
        var totalNet = 0m;

        foreach (var session in sessions)
        {
            var attended = await db.LiveClassEnrollments.AsNoTracking()
                .CountAsync(enrollment => enrollment.ClassSessionId == session.Id
                    && enrollment.Status == LiveClassEnrollmentStatus.Attended, ct);
            if (attended == 0)
            {
                continue;
            }

            var creditCost = Math.Max(0, session.LiveClass.CreditCost);
            var gross = creditCost * creditUsdValue * attended;
            var net = gross * revenueShare;
            totalGross += gross;
            totalNet += net;
            lines.Add(new TutorEarningsLineDto(
                session.Id,
                session.LiveClassId,
                session.LiveClass.Title,
                session.ScheduledStartAt,
                attended,
                creditCost,
                creditUsdValue,
                revenueShare,
                Math.Round(gross, 2),
                Math.Round(net, 2)));
        }

        return new TutorEarningsDto(from, to, Math.Round(totalGross, 2), Math.Round(totalNet, 2), revenueShare, lines);
    }

    public async Task<string?> ProvisionZoomUserAsync(string userId, CancellationToken ct)
    {
        var tutor = await db.Tutors.FirstOrDefaultAsync(t => t.UserId == userId, ct)
            ?? throw ApiException.NotFound("tutor_not_found", "Tutor profile not found.");

        if (!string.IsNullOrWhiteSpace(tutor.ZoomUserId))
        {
            return tutor.ZoomUserId;
        }

        // v2 stub per plan §6.4: until proper per-tutor Zoom user provisioning
        // lands, every tutor session is hosted by the platform default Zoom
        // user (configured via runtime settings or appsettings Zoom:HostUserId).
        // We persist the default so the join-token path can look it up
        // without re-reading settings each time.
        var settings = await runtimeSettings.GetAsync(ct);
        var fallbackHost = settings.Zoom.HostUserId;
        if (string.IsNullOrWhiteSpace(fallbackHost))
        {
            return null;
        }

        tutor.ZoomUserId = fallbackHost;
        tutor.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Provisioned default Zoom host user for tutor {TutorId}", tutor.Id);
        return tutor.ZoomUserId;
    }

    public async Task<Tutor> EnsureTutorByUserIdAsync(string userId, CancellationToken ct)
        => await db.Tutors.FirstOrDefaultAsync(t => t.UserId == userId, ct)
            ?? throw ApiException.NotFound("tutor_not_found", "Tutor profile not found.");

    // -- helpers -----------------------------------------------------------

    private static TutorProfileDto MapTutor(Tutor tutor)
        => new(
            tutor.Id,
            tutor.UserId,
            tutor.DisplayName,
            tutor.DisplayNameAr,
            tutor.Bio,
            tutor.BioAr,
            tutor.AvatarUrl,
            DeserializeStringArray(tutor.SpecialtiesJson),
            DeserializeStringArray(tutor.LanguagesJson),
            tutor.HourlyRateUsd,
            tutor.TimeZone,
            tutor.ZoomUserId,
            tutor.IsActive,
            tutor.CreatedAt,
            tutor.UpdatedAt);

    private static TutorAvailabilityDto MapSlot(TutorAvailability slot)
        => new(slot.Id, slot.DayOfWeek, slot.StartTime, slot.EndTime, slot.IsActive);

    private static void ValidateUpsertRequest(TutorUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw ApiException.Validation("tutor_display_name_required", "Display name is required.");
        }

        if (request.HourlyRateUsd is < 0m)
        {
            throw ApiException.Validation("tutor_hourly_rate_invalid", "Hourly rate must be non-negative.");
        }
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string SerializeStringArray(IReadOnlyList<string>? values)
    {
        var clean = (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return JsonSerializer.Serialize(clean, JsonOptions);
    }

    private static IReadOnlyList<string> DeserializeStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
