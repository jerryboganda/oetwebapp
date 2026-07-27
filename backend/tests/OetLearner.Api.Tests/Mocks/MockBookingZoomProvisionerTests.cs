using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Mocks;

namespace OetLearner.Api.Tests.Mocks;

/// <summary>
/// MockBookingZoomProvisioner — the background-job body that turns a mock
/// booking's ZoomStatus=pending into a REAL Zoom meeting (id + join/start
/// URLs + password), with idempotent re-runs, failure bookkeeping, and a
/// clean skip when the integration is disabled.
/// </summary>
public sealed class MockBookingZoomProvisionerTests
{
    private const string BookingId = "zoom-provision-booking";

    [Fact]
    public async Task CreatesRealMeetingAndStampsBooking()
    {
        await using var db = NewDb();
        SeedBooking(db);
        await db.SaveChangesAsync();

        var handler = ZoomApiHandler();
        var provisioner = CreateProvisioner(db, handler);

        await provisioner.CreateZoomMeetingForMockBookingAsync(BookingId, CancellationToken.None);

        var booking = await db.MockBookings.SingleAsync(b => b.Id == BookingId);
        Assert.Equal(MockBookingZoomStatuses.Created, booking.ZoomStatus);
        Assert.Equal("123456789", booking.ZoomMeetingId);
        Assert.Equal("https://zoom.test/j/123456789", booking.ZoomJoinUrl);
        Assert.Equal("https://zoom.test/s/123456789?zak=host", booking.ZoomStartUrl);
        Assert.Equal("pw123", booking.ZoomMeetingPassword);
        Assert.Null(booking.ZoomError);
        Assert.True(await db.AuditEvents.AnyAsync(a =>
            a.Action == "mock_booking_zoom_created" && a.ResourceId == BookingId));

        // Presentation gates now surface the real links.
        Assert.Equal("https://zoom.test/j/123456789", MockBookingPresentation.LearnerZoomJoinUrl(booking));
        Assert.Equal("https://zoom.test/s/123456789?zak=host", MockBookingPresentation.ExpertZoomStartUrl(booking));
    }

    [Fact]
    public async Task MarksFailedAndRethrowsOnZoomApiError()
    {
        await using var db = NewDb();
        SeedBooking(db);
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
        var provisioner = CreateProvisioner(db, handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provisioner.CreateZoomMeetingForMockBookingAsync(BookingId, CancellationToken.None));

        var booking = await db.MockBookings.SingleAsync(b => b.Id == BookingId);
        Assert.Equal(MockBookingZoomStatuses.Failed, booking.ZoomStatus);
        Assert.Equal(1, booking.ZoomRetryCount);
        Assert.False(string.IsNullOrEmpty(booking.ZoomError));
        Assert.Null(MockBookingPresentation.LearnerZoomJoinUrl(booking));
    }

    [Fact]
    public async Task SkipsQuietlyWhenZoomDisabled()
    {
        await using var db = NewDb();
        SeedBooking(db);
        await db.SaveChangesAsync();

        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("should not be called"));
        var provisioner = new MockBookingZoomProvisioner(
            db,
            new ZoomMeetingService(
                new StaticHttpClientFactory(handler),
                TestRuntimeSettingsProvider.FromZoomOptions(new ZoomOptions { Enabled = false }),
                NullLogger<ZoomMeetingService>.Instance),
            NullLogger<MockBookingZoomProvisioner>.Instance);

        await provisioner.CreateZoomMeetingForMockBookingAsync(BookingId, CancellationToken.None);

        var booking = await db.MockBookings.SingleAsync(b => b.Id == BookingId);
        Assert.Equal(MockBookingZoomStatuses.Pending, booking.ZoomStatus);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task IdempotentWhenMeetingAlreadyCreated()
    {
        await using var db = NewDb();
        SeedBooking(db, b =>
        {
            b.ZoomStatus = MockBookingZoomStatuses.Created;
            b.ZoomMeetingId = "555000111";
            b.ZoomJoinUrl = "https://zoom.test/j/555000111";
            b.ZoomStartUrl = "https://zoom.test/s/555000111";
        });
        await db.SaveChangesAsync();

        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("should not be called"));
        var provisioner = CreateProvisioner(db, handler);

        await provisioner.CreateZoomMeetingForMockBookingAsync(BookingId, CancellationToken.None);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SkipsCancelledBooking()
    {
        await using var db = NewDb();
        SeedBooking(db, b => b.Status = MockBookingStatuses.Cancelled);
        await db.SaveChangesAsync();

        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("should not be called"));
        var provisioner = CreateProvisioner(db, handler);

        await provisioner.CreateZoomMeetingForMockBookingAsync(BookingId, CancellationToken.None);

        Assert.Empty(handler.Requests);
    }

    // -- helpers -----------------------------------------------------------

    private static LearnerDbContext NewDb() =>
        new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static void SeedBooking(LearnerDbContext db, Action<MockBooking>? mutate = null)
    {
        var now = DateTimeOffset.UtcNow;
        db.MockBundles.Add(new MockBundle
        {
            Id = "zoom-provision-bundle",
            Title = "Zoom provisioning bundle",
            Slug = "zoom-provision-bundle",
            MockType = MockTypes.FinalReadiness,
            AppliesToAllProfessions = true,
            Status = ContentStatus.Published,
            EstimatedDurationMinutes = 20,
            ReleasePolicy = MockReleasePolicies.AfterTeacherMarking,
            SourceStatus = MockSourceStatuses.Original,
            QualityStatus = MockQualityStatuses.Approved,
            SourceProvenance = "Zoom provisioner test seed.",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        });
        var booking = new MockBooking
        {
            Id = BookingId,
            UserId = "zoom-provision-learner",
            MockBundleId = "zoom-provision-bundle",
            ScheduledStartAt = now.AddDays(2),
            TimezoneIana = "UTC",
            Status = MockBookingStatuses.Scheduled,
            DeliveryMode = MockDeliveryModes.Computer,
            LiveRoomState = MockLiveRoomStates.Waiting,
            ZoomStatus = MockBookingZoomStatuses.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
        mutate?.Invoke(booking);
        db.MockBookings.Add(booking);
    }

    private static MockBookingZoomProvisioner CreateProvisioner(LearnerDbContext db, RecordingHandler handler)
        => new(
            db,
            new ZoomMeetingService(
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
            NullLogger<MockBookingZoomProvisioner>.Instance);

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
