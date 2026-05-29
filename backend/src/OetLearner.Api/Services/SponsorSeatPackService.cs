using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public interface ISponsorSeatPackService
{
    Task<SponsorSeatPackListResponse> ListPacksAsync(string sponsorId, CancellationToken ct);
    Task<SponsorSeatPack> PurchasePackAsync(string sponsorId, PurchaseSeatPackRequest request, CancellationToken ct);
    Task<SponsorSeatAssignment> AssignSeatAsync(string sponsorId, Guid seatPackId, AssignSeatRequest request, CancellationToken ct);
    Task RevokeSeatAsync(string sponsorId, Guid assignmentId, CancellationToken ct);
    Task<SponsorBillingLedgerResponse> GetLedgerAsync(string sponsorId, int page, int pageSize, CancellationToken ct);
}

public sealed class SponsorSeatPackService(LearnerDbContext db, TimeProvider clock, ILogger<SponsorSeatPackService> logger) : ISponsorSeatPackService
{
    public async Task<SponsorSeatPackListResponse> ListPacksAsync(string sponsorId, CancellationToken ct)
    {
        var packs = await db.SponsorSeatPacks
            .AsNoTracking()
            .Where(p => p.SponsorId == sponsorId)
            .OrderByDescending(p => p.PurchasedAt)
            .ToListAsync(ct);

        var totalSeats = packs.Where(p => p.Status == "active").Sum(p => p.TotalSeats);
        var assignedSeats = packs.Where(p => p.Status == "active").Sum(p => p.AssignedSeats);

        return new SponsorSeatPackListResponse(
            TotalSeats: totalSeats,
            AssignedSeats: assignedSeats,
            AvailableSeats: totalSeats - assignedSeats,
            Packs: packs.Select(p => new SeatPackSummary(
                p.Id, p.Name, p.TotalSeats, p.AssignedSeats,
                p.UnitPrice, p.Currency, p.Status, p.PurchasedAt, p.ExpiresAt)).ToList());
    }

    public async Task<SponsorSeatPack> PurchasePackAsync(string sponsorId, PurchaseSeatPackRequest request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        if (request.TotalSeats < 1) throw new ArgumentException("Must purchase at least 1 seat.");
        if (request.UnitPrice < 0) throw new ArgumentException("Unit price cannot be negative.");

        var now = clock.GetUtcNow();
        var pack = new SponsorSeatPack
        {
            Id = Guid.NewGuid(),
            SponsorId = sponsorId,
            Name = request.Name,
            TotalSeats = request.TotalSeats,
            AssignedSeats = 0,
            UnitPrice = request.UnitPrice,
            Currency = request.Currency ?? "GBP",
            StripePaymentId = request.StripePaymentId,
            Status = "active",
            PurchasedAt = now,
            ExpiresAt = request.ExpiresAt,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.SponsorSeatPacks.Add(pack);
        db.SponsorBillingEvents.Add(new SponsorBillingEvent
        {
            Id = Guid.NewGuid(),
            SponsorId = sponsorId,
            SeatPackId = pack.Id,
            EventType = "purchase",
            Amount = pack.TotalSeats * pack.UnitPrice,
            Currency = pack.Currency,
            SeatsDelta = pack.TotalSeats,
            Description = $"Purchased {pack.TotalSeats}-seat pack: {pack.Name}",
            CreatedAt = now,
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Sponsor {SponsorId} purchased {Seats}-seat pack {PackId}", sponsorId, pack.TotalSeats, pack.Id);
        return pack;
    }

    public async Task<SponsorSeatAssignment> AssignSeatAsync(string sponsorId, Guid seatPackId, AssignSeatRequest request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LearnerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LearnerEmail);

        var pack = await db.SponsorSeatPacks
            .FirstOrDefaultAsync(p => p.Id == seatPackId && p.SponsorId == sponsorId, ct)
            ?? throw new InvalidOperationException("Seat pack not found.");

        if (pack.Status != "active")
            throw new InvalidOperationException($"Seat pack is {pack.Status}, cannot assign seats.");

        if (pack.AssignedSeats >= pack.TotalSeats)
            throw new InvalidOperationException("No available seats in this pack.");

        var now = clock.GetUtcNow();
        var assignment = new SponsorSeatAssignment
        {
            Id = Guid.NewGuid(),
            SeatPackId = seatPackId,
            LearnerId = request.LearnerId,
            LearnerEmail = request.LearnerEmail,
            Status = "assigned",
            AssignedAt = now,
        };

        pack.AssignedSeats++;
        pack.UpdatedAt = now;

        db.SponsorSeatAssignments.Add(assignment);
        db.SponsorBillingEvents.Add(new SponsorBillingEvent
        {
            Id = Guid.NewGuid(),
            SponsorId = sponsorId,
            SeatPackId = seatPackId,
            EventType = "seat_assigned",
            SeatsDelta = 1,
            Description = $"Seat assigned to {request.LearnerEmail}",
            ActorUserId = sponsorId,
            CreatedAt = now,
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seat assigned in pack {PackId} to learner {LearnerId}", seatPackId, request.LearnerId);
        return assignment;
    }

    public async Task RevokeSeatAsync(string sponsorId, Guid assignmentId, CancellationToken ct)
    {
        var assignment = await db.SponsorSeatAssignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId, ct)
            ?? throw new InvalidOperationException("Seat assignment not found.");

        var pack = await db.SponsorSeatPacks
            .FirstOrDefaultAsync(p => p.Id == assignment.SeatPackId && p.SponsorId == sponsorId, ct)
            ?? throw new InvalidOperationException("Seat pack not found for this sponsor.");

        if (assignment.Status != "assigned")
            throw new InvalidOperationException($"Seat is already {assignment.Status}.");

        var now = clock.GetUtcNow();
        assignment.Status = "revoked";
        assignment.RevokedAt = now;
        pack.AssignedSeats = Math.Max(0, pack.AssignedSeats - 1);
        pack.UpdatedAt = now;

        db.SponsorBillingEvents.Add(new SponsorBillingEvent
        {
            Id = Guid.NewGuid(),
            SponsorId = sponsorId,
            SeatPackId = pack.Id,
            EventType = "seat_revoked",
            SeatsDelta = -1,
            Description = $"Seat revoked from {assignment.LearnerEmail}",
            ActorUserId = sponsorId,
            CreatedAt = now,
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seat revoked: assignment {AssignmentId} in pack {PackId}", assignmentId, pack.Id);
    }

    public async Task<SponsorBillingLedgerResponse> GetLedgerAsync(string sponsorId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.SponsorBillingEvents
            .AsNoTracking()
            .Where(e => e.SponsorId == sponsorId)
            .OrderByDescending(e => e.CreatedAt);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new SponsorBillingLedgerResponse(total, items);
    }
}

// ─── Contracts ───────────────────────────────────────────────────────────────
public record PurchaseSeatPackRequest(
    string Name,
    int TotalSeats,
    decimal UnitPrice,
    string? Currency,
    string? StripePaymentId,
    DateTimeOffset? ExpiresAt);

public record AssignSeatRequest(string LearnerId, string LearnerEmail);

public record SeatPackSummary(
    Guid Id, string Name, int TotalSeats, int AssignedSeats,
    decimal UnitPrice, string Currency, string Status,
    DateTimeOffset PurchasedAt, DateTimeOffset? ExpiresAt);

public record SponsorSeatPackListResponse(
    int TotalSeats, int AssignedSeats, int AvailableSeats,
    List<SeatPackSummary> Packs);

public record SponsorBillingLedgerResponse(int Total, List<SponsorBillingEvent> Items);
