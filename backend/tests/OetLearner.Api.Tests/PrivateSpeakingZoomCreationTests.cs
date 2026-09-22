using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

/// <summary>
/// PrivateSpeakingService.CreateZoomMeetingForBookingAsync — retired by the
/// LiveKit rewrite (commit 74fdadd80, "complete native live voice and tutor
/// rooms"). Zoom is no longer provisioned for Private Speaking bookings;
/// LiveKit rooms are provisioned lazily near session time by
/// LiveTutorRoomLifecycleWorker instead (see PrivateSpeakingEndpoints'
/// retired /retry-zoom and /join-token routes, both now 410 Gone). The
/// method survives only as an inert compatibility shim so that a
/// PrivateSpeakingZoomCreate job already queued before the cutover doesn't
/// crash the background job processor on redeploy. These tests pin that
/// contract: the shim never calls Zoom, never throws, and never mutates the
/// booking.
/// </summary>
public sealed class PrivateSpeakingZoomCreationTests
{
    private const string BookingId = "ps-zoom-create-booking";

    [Fact]
    public async Task CreateZoomMeeting_LegacyJobIsANoOp()
    {
        await using var db = NewDb();
        SeedConfirmedBooking(db);
        await db.SaveChangesAsync();

        // Throws if ever called — proves the retired job body never reaches
        // the Zoom API.
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("should not be called"));
        var service = CreateService(db, handler);

        await service.CreateZoomMeetingForBookingAsync(BookingId, CancellationToken.None);

        Assert.Empty(handler.Requests);

        var booking = await db.PrivateSpeakingBookings.SingleAsync(b => b.Id == BookingId);
        Assert.Equal(PrivateSpeakingBookingStatus.Confirmed, booking.Status);
        Assert.Equal(PrivateSpeakingZoomStatus.Pending, booking.ZoomStatus);
        Assert.Null(booking.ZoomMeetingId);
        Assert.False(await db.PrivateSpeakingAuditLogs.AnyAsync(a => a.BookingId == BookingId));
        Assert.False(await db.BackgroundJobs.AnyAsync(j => j.ResourceId == BookingId));
    }

    [Fact]
    public async Task CreateZoomMeeting_DoesNotThrowOrRetryForAPreviouslyFailedBooking()
    {
        await using var db = NewDb();
        SeedConfirmedBooking(db, b =>
        {
            b.ZoomStatus = PrivateSpeakingZoomStatus.Failed;
            b.ZoomRetryCount = 1;
            b.ZoomError = "pre-cutover failure";
        });
        await db.SaveChangesAsync();

        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("should not be called"));
        var service = CreateService(db, handler);

        // A PrivateSpeakingZoomCreate job queued (and already retried once)
        // before the LiveKit cutover must complete quietly on redeploy —
        // not throw and not touch Zoom — otherwise the background job
        // processor would retry it forever.
        await service.CreateZoomMeetingForBookingAsync(BookingId, CancellationToken.None);

        Assert.Empty(handler.Requests);

        var booking = await db.PrivateSpeakingBookings.SingleAsync(b => b.Id == BookingId);
        Assert.Equal(PrivateSpeakingZoomStatus.Failed, booking.ZoomStatus);
        Assert.Equal(1, booking.ZoomRetryCount);
    }

    [Fact]
    public async Task CreateZoomMeeting_IsIdempotentOncePerBooking()
    {
        await using var db = NewDb();
        SeedConfirmedBooking(db, b => b.ZoomStatus = PrivateSpeakingZoomStatus.Created);
        await db.SaveChangesAsync();

        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("should not be called"));
        var service = CreateService(db, handler);

        await service.CreateZoomMeetingForBookingAsync(BookingId, CancellationToken.None);

        Assert.Empty(handler.Requests);
    }

    // -- helpers -----------------------------------------------------------

    private static LearnerDbContext NewDb() =>
        new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static void SeedConfirmedBooking(LearnerDbContext db, Action<PrivateSpeakingBooking>? mutate = null)
    {
        // Required relationship: without the tutor-profile principal the
        // Include(b => b.TutorProfile) query drops the booking entirely.
        db.PrivateSpeakingTutorProfiles.Add(new PrivateSpeakingTutorProfile
        {
            Id = "ps-zoom-create-tutor",
            ExpertUserId = "ps-zoom-create-expert",
            DisplayName = "Test Tutor",
            Timezone = "UTC",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        var booking = new PrivateSpeakingBooking
        {
            Id = BookingId,
            LearnerUserId = "ps-zoom-create-learner",
            TutorProfileId = "ps-zoom-create-tutor",
            Status = PrivateSpeakingBookingStatus.Confirmed,
            SessionStartUtc = DateTimeOffset.UtcNow.AddDays(2),
            DurationMinutes = 30,
            TutorTimezone = "UTC",
            LearnerTimezone = "UTC",
        };
        mutate?.Invoke(booking);
        db.PrivateSpeakingBookings.Add(booking);
    }

    /// <summary>Only the deps the Zoom-creation path touches are real: db,
    /// Zoom service, time provider, logger. The rest are unused on this path.</summary>
    private static PrivateSpeakingService CreateService(LearnerDbContext db, RecordingHandler handler)
        => new(
            db,
            notificationService: null!,
            zoomService: new ZoomMeetingService(
                new StaticHttpClientFactory(handler),
                TestRuntimeSettingsProvider.FromZoomOptions(new ZoomOptions
                {
                    Enabled = true,
                    AccountId = "acct",
                    ClientId = "client",
                    ClientSecret = "secret",
                    ApiBaseUrl = "https://api.zoom.test/v2",
                    TokenUrl = "https://zoom.test/oauth/token",
                    HostUserId = "platform-host",
                }),
                NullLogger<ZoomMeetingService>.Instance),
            calendarService: null!,
            entitlementResolver: null!,
            addonEligibility: null!,
            stripeService: null!,
            paymentGateways: null!,
            platformLinks: null!,
            timeProvider: TimeProvider.System,
            logger: NullLogger<PrivateSpeakingService>.Instance);

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return await respond(request, cancellationToken);
        }
    }

    private sealed class StaticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
