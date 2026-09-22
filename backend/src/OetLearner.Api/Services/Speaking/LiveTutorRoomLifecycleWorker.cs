using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Speaking;

public sealed class LiveTutorRoomLifecycleWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<LiveTutorRoomLifecycleWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProvisionLookback = TimeSpan.FromHours(2);
    private static readonly TimeSpan ProvisionLookahead = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Live tutor room lifecycle sweep failed.");
            }

            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var exams = scope.ServiceProvider.GetRequiredService<SpeakingExamService>();
        var rooms = scope.ServiceProvider.GetRequiredService<SpeakingLiveRoomService>();
        var now = clock.GetUtcNow();

        var bookings = await db.PrivateSpeakingBookings
            .Where(b => (b.Status is PrivateSpeakingBookingStatus.Confirmed
                    or PrivateSpeakingBookingStatus.ZoomCreated
                    or PrivateSpeakingBookingStatus.InProgress)
                && b.SessionStartUtc >= now - ProvisionLookback
                && b.SessionStartUtc <= now + ProvisionLookahead)
            .OrderBy(b => b.SessionStartUtc)
            .Take(250)
            .ToListAsync(ct);

        var provisioned = 0;
        foreach (var booking in bookings)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(booking.ExamSessionId))
                {
                    await exams.CreateExamForBookingAsync(booking.LearnerUserId, booking.Id, ct);
                }

                var exam = await db.SpeakingExamSessions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == booking.ExamSessionId, ct);
                if (exam is null || exam.Mode != SpeakingExamMode.LiveTutor
                    || string.IsNullOrWhiteSpace(exam.SessionAId))
                {
                    logger.LogWarning(
                        "LiveTutorRoomLifecycleWorker booking_pending_exam bookingId={BookingId}",
                        booking.Id);
                    continue;
                }

                var room = await rooms.ProvisionForBookingAsync(
                    booking.Id,
                    booking.LearnerUserId,
                    exam.SessionAId,
                    ct);
                if (room is not null) provisioned++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "LiveTutorRoomLifecycleWorker booking_provision_failed bookingId={BookingId}",
                    booking.Id);
            }
        }

        await rooms.TearDownExpiredRoomsAsync(ct);
        return provisioned;
    }
}
