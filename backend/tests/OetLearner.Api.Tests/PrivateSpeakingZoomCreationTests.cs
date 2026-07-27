using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

/// <summary>
/// PrivateSpeakingService.CreateZoomMeetingForBookingAsync — the actual
/// meeting-creation job body for paid tutor bookings. Previously only the
/// job-enqueue side was tested; this exercises the real create path against a
/// mocked Zoom HTTP API: booking stamped with meeting id/URLs/password,
/// status advanced to ZoomCreated, calendar sync queued, audit written, and
/// the failure path incrementing ZoomRetryCount and rethrowing for retry.
/// </summary>
public sealed class PrivateSpeakingZoomCreationTests
{
    private const string BookingId = "ps-zoom-create-booking";

    [Fact]
    public async Task CreateZoomMeeting_StampsBookingAndQueuesCalendarSync()
    {
        await using var db = NewDb();
        SeedConfirmedBooking(db);
        await db.SaveChangesAsync();

        var service = CreateService(db, ZoomApiHandler());

        await service.CreateZoomMeetingForBookingAsync(BookingId, CancellationToken.None);

        var booking = await db.PrivateSpeakingBookings.SingleAsync(b => b.Id == BookingId);
        Assert.Equal(PrivateSpeakingBookingStatus.ZoomCreated, booking.Status);
        Assert.Equal(PrivateSpeakingZoomStatus.Created, booking.ZoomStatus);
        Assert.Equal(123456789, booking.ZoomMeetingId);
        Assert.Equal("https://zoom.test/j/123456789", booking.ZoomJoinUrl);
        Assert.Equal("https://zoom.test/s/123456789?zak=host", booking.ZoomStartUrl);
        Assert.Equal("pw123", booking.ZoomMeetingPassword);

        Assert.True(await db.PrivateSpeakingAuditLogs.AnyAsync(a =>
            a.BookingId == BookingId && a.Action == "zoom_created"));
        Assert.True(await db.BackgroundJobs.AnyAsync(j =>
            j.Type == JobType.PrivateSpeakingCalendarSync && j.ResourceId == BookingId));
    }

    [Fact]
    public async Task CreateZoomMeeting_MarksFailedAndRethrowsForRetry()
    {
        await using var db = NewDb();
        SeedConfirmedBooking(db);
        await db.SaveChangesAsync();

        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri!.AbsoluteUri.Contains("/oauth/token", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(JsonResponse("{\"access_token\":\"server-token\",\"token_type\":\"bearer\",\"expires_in\":3600}"));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"code\":200,\"message\":\"boom\"}", Encoding.UTF8, "application/json"),
            });
        });
        var service = CreateService(db, handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateZoomMeetingForBookingAsync(BookingId, CancellationToken.None));

        var booking = await db.PrivateSpeakingBookings.SingleAsync(b => b.Id == BookingId);
        Assert.Equal(PrivateSpeakingZoomStatus.Failed, booking.ZoomStatus);
        Assert.Equal(1, booking.ZoomRetryCount);
        Assert.False(string.IsNullOrEmpty(booking.ZoomError));
        // Not terminal yet — the background job retries up to 3 times.
        Assert.Equal(PrivateSpeakingBookingStatus.Confirmed, booking.Status);
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
            stripeService: null!,
            paymentGateways: null!,
            platformLinks: null!,
            timeProvider: TimeProvider.System,
            logger: NullLogger<PrivateSpeakingService>.Instance);

    private static RecordingHandler ZoomApiHandler() => new((request, _) =>
    {
        if (request.RequestUri!.AbsoluteUri.Contains("/oauth/token", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(JsonResponse("{\"access_token\":\"server-token\",\"token_type\":\"bearer\",\"expires_in\":3600}"));
        }

        return Task.FromResult(JsonResponse(
            "{\"id\":123456789,\"join_url\":\"https://zoom.test/j/123456789\"," +
            "\"start_url\":\"https://zoom.test/s/123456789?zak=host\",\"password\":\"pw123\"}"));
    });

    private static HttpResponseMessage JsonResponse(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

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
