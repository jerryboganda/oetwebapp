using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Mocks;

namespace OetLearner.Api.Tests.Mocks;

public class MockLiveRoomTransitionTests
{
    private const string BookingId = "booking-live-room-test";

    private static LearnerDbContext NewDb(string? name = null) =>
        new(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task AssignedExpert_CanMarkLearnerNoShow_AndBroadcastsAuditedTransition()
    {
        await using var db = NewDb();
        SeedBooking(db);
        await db.SaveChangesAsync();
        var hub = new RecordingHubContext<MockLiveRoomHub>();
        var service = new MockBookingService(db, hub);

        var result = await service.TransitionLiveRoomAsync(
            "expert-tutor",
            ApplicationUserRoles.Expert,
            isAdmin: false,
            BookingId,
            new LiveRoomTransitionRequest(MockLiveRoomStates.LearnerNoShow, "Learner did not join", "expert-no-show-1"),
            CancellationToken.None);

        var projected = Assert.IsType<Dictionary<string, object?>>(result);
        Assert.True(Assert.IsType<bool>(projected["interlocutorCardVisible"]));
        Assert.Equal("expert-tutor", projected["assignedTutorId"]);

        var booking = await db.MockBookings.SingleAsync(b => b.Id == BookingId);
        Assert.Equal(MockLiveRoomStates.LearnerNoShow, booking.LiveRoomState);
        Assert.Equal(MockBookingStatuses.LearnerNoShow, booking.Status);
        Assert.Equal(1, booking.LiveRoomTransitionVersion);

        var transition = await db.MockLiveRoomTransitions.SingleAsync(t => t.BookingId == BookingId);
        Assert.Equal(ApplicationUserRoles.Expert, transition.ActorRole);
        Assert.Equal(MockLiveRoomStates.Waiting, transition.FromState);
        Assert.Equal(MockLiveRoomStates.LearnerNoShow, transition.ToState);
        Assert.Equal("expert-no-show-1", transition.ClientTransitionId);

        Assert.Contains(db.AuditEvents, e => e.Action == "mock_live_room_transitioned" && e.ResourceId == BookingId);
        Assert.Single(hub.ClientsProxy.Messages);
        Assert.Equal(MockLiveRoomHub.BookingGroup(BookingId), hub.ClientsProxy.Messages[0].GroupName);
        Assert.Equal(MockLiveRoomHub.LiveRoomStateChangedEvent, hub.ClientsProxy.Messages[0].Method);
        var payload = Assert.IsType<MockLiveRoomStateChanged>(Assert.Single(hub.ClientsProxy.Messages[0].Args));
        Assert.Equal(MockLiveRoomStates.LearnerNoShow, payload.ToState);
        Assert.Equal(MockLiveRoomStates.LearnerNoShow, payload.LiveRoomState);
    }

    [Fact]
    public async Task Learner_CannotMarkTutorNoShow()
    {
        await using var db = NewDb();
        SeedBooking(db);
        await db.SaveChangesAsync();
        var service = new MockBookingService(db, new RecordingHubContext<MockLiveRoomHub>());

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.TransitionLiveRoomAsync(
            "learner-1",
            ApplicationUserRoles.Learner,
            isAdmin: false,
            BookingId,
            new LiveRoomTransitionRequest(MockLiveRoomStates.TutorNoShow),
            CancellationToken.None));

        Assert.Equal("invalid_transition", ex.ErrorCode);
        Assert.Empty(db.MockLiveRoomTransitions);
    }

    [Fact]
    public async Task DuplicateClientTransitionId_ReturnsExistingState_WithoutNewAuditOrBroadcast()
    {
        await using var db = NewDb();
        SeedBooking(db);
        await db.SaveChangesAsync();
        var hub = new RecordingHubContext<MockLiveRoomHub>();
        var service = new MockBookingService(db, hub);

        var request = new LiveRoomTransitionRequest(MockLiveRoomStates.InProgress, ClientTransitionId: " start-once ");
        await service.TransitionLiveRoomAsync("learner-1", ApplicationUserRoles.Learner, false, BookingId, request, CancellationToken.None);
        await service.TransitionLiveRoomAsync("learner-1", ApplicationUserRoles.Learner, false, BookingId,
            new LiveRoomTransitionRequest(MockLiveRoomStates.InProgress, ClientTransitionId: "start-once"), CancellationToken.None);

        Assert.Equal(1, await db.MockLiveRoomTransitions.CountAsync(t => t.BookingId == BookingId));
        Assert.Equal("start-once", await db.MockLiveRoomTransitions.Select(t => t.ClientTransitionId).SingleAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync(e => e.Action == "mock_live_room_transitioned" && e.ResourceId == BookingId));
        Assert.Single(hub.ClientsProxy.Messages);
    }

    [Fact]
    public async Task Learner_CannotCompleteRoomWithoutFinalizedRecording()
    {
        await using var db = NewDb();
        SeedBooking(db, MockLiveRoomStates.InProgress, MockBookingStatuses.InProgress);
        await db.SaveChangesAsync();
        var service = new MockBookingService(db, new RecordingHubContext<MockLiveRoomHub>());

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.TransitionLiveRoomAsync(
            "learner-1",
            ApplicationUserRoles.Learner,
            isAdmin: false,
            BookingId,
            new LiveRoomTransitionRequest(MockLiveRoomStates.Completed),
            CancellationToken.None));

        Assert.Equal("invalid_transition", ex.ErrorCode);
        Assert.Empty(db.MockLiveRoomTransitions);
    }

    [Fact]
    public async Task Learner_CanCompleteRoomAfterRecordingIsFinalized()
    {
        await using var db = NewDb();
        SeedBooking(db, MockLiveRoomStates.InProgress, MockBookingStatuses.InProgress, recordingFinalizedAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        await db.SaveChangesAsync();
        var service = new MockBookingService(db, new RecordingHubContext<MockLiveRoomHub>());

        await service.TransitionLiveRoomAsync(
            "learner-1",
            ApplicationUserRoles.Learner,
            isAdmin: false,
            BookingId,
            new LiveRoomTransitionRequest(MockLiveRoomStates.Completed),
            CancellationToken.None);

        var booking = await db.MockBookings.SingleAsync(b => b.Id == BookingId);
        Assert.Equal(MockLiveRoomStates.Completed, booking.LiveRoomState);
        Assert.Equal(MockBookingStatuses.Completed, booking.Status);
        Assert.NotNull(booking.CompletedAt);
    }

    [Fact]
    public async Task CancelledBooking_RejectsLiveRoomTransition()
    {
        await using var db = NewDb();
        SeedBooking(db, bookingStatus: MockBookingStatuses.Cancelled);
        await db.SaveChangesAsync();
        var service = new MockBookingService(db, new RecordingHubContext<MockLiveRoomHub>());

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.TransitionLiveRoomAsync(
            "expert-tutor",
            ApplicationUserRoles.Expert,
            isAdmin: false,
            BookingId,
            new LiveRoomTransitionRequest(MockLiveRoomStates.InProgress),
            CancellationToken.None));

        Assert.Equal("booking_finalized", ex.ErrorCode);
        Assert.Empty(db.MockLiveRoomTransitions);
    }

    [Fact]
    public async Task BlankTargetState_ReturnsValidationInsteadOfNullReference()
    {
        await using var db = NewDb();
        SeedBooking(db);
        await db.SaveChangesAsync();
        var service = new MockBookingService(db, new RecordingHubContext<MockLiveRoomHub>());

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.TransitionLiveRoomAsync(
            "learner-1",
            ApplicationUserRoles.Learner,
            isAdmin: false,
            BookingId,
            new LiveRoomTransitionRequest(" "),
            CancellationToken.None));

        Assert.Equal("invalid_state", ex.ErrorCode);
        Assert.Empty(db.MockLiveRoomTransitions);
    }

    [Fact]
    public async Task OverlongTransitionMetadata_ReturnsValidationBeforeSaving()
    {
        await using var db = NewDb();
        SeedBooking(db);
        await db.SaveChangesAsync();
        var service = new MockBookingService(db, new RecordingHubContext<MockLiveRoomHub>());

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.TransitionLiveRoomAsync(
            "learner-1",
            ApplicationUserRoles.Learner,
            isAdmin: false,
            BookingId,
            new LiveRoomTransitionRequest(MockLiveRoomStates.InProgress, ClientTransitionId: new string('x', 97)),
            CancellationToken.None));

        Assert.Equal("client_transition_id_too_long", ex.ErrorCode);
        Assert.Empty(db.MockLiveRoomTransitions);
    }

    private static void SeedBooking(
        LearnerDbContext db,
        string liveRoomState = MockLiveRoomStates.Waiting,
        string bookingStatus = MockBookingStatuses.Scheduled,
        DateTimeOffset? recordingFinalizedAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        db.MockBundles.Add(new MockBundle
        {
            Id = "bundle-live-room-test",
            Title = "Live room test bundle",
            Slug = "live-room-test-bundle",
            MockType = "speaking",
            AppliesToAllProfessions = true,
            Status = ContentStatus.Published,
            EstimatedDurationMinutes = 20,
            SourceProvenance = "Mock live-room transition test seed.",
            CreatedByAdminId = "admin-1",
            UpdatedByAdminId = "admin-1",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        });
        db.MockBookings.Add(new MockBooking
        {
            Id = BookingId,
            UserId = "learner-1",
            MockBundleId = "bundle-live-room-test",
            ScheduledStartAt = now.AddMinutes(30),
            Status = bookingStatus,
            TimezoneIana = "UTC",
            DeliveryMode = MockDeliveryModes.Computer,
            LiveRoomState = liveRoomState,
            AssignedTutorId = "expert-tutor",
            AssignedInterlocutorId = "expert-interlocutor",
            RecordingFinalizedAt = recordingFinalizedAt,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    private sealed class RecordingHubContext<THub> : IHubContext<THub> where THub : Hub
    {
        public RecordingHubContext()
        {
            ClientsProxy = new RecordingHubClients();
            Clients = ClientsProxy;
            Groups = new NoopGroupManager();
        }

        public RecordingHubClients ClientsProxy { get; }
        public IHubClients Clients { get; }
        public IGroupManager Groups { get; }
    }

    private sealed class RecordingHubClients : IHubClients
    {
        public List<RecordedHubMessage> Messages { get; } = [];

        public IClientProxy All => new RecordingClientProxy("all", Messages);
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new RecordingClientProxy("all-except", Messages);
        public IClientProxy Client(string connectionId) => new RecordingClientProxy($"client:{connectionId}", Messages);
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new RecordingClientProxy("clients", Messages);
        public IClientProxy Group(string groupName) => new RecordingClientProxy(groupName, Messages);
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new RecordingClientProxy(groupName, Messages);
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => new RecordingClientProxy("groups", Messages);
        public IClientProxy User(string userId) => new RecordingClientProxy($"user:{userId}", Messages);
        public IClientProxy Users(IReadOnlyList<string> userIds) => new RecordingClientProxy("users", Messages);
    }

    private sealed class RecordingClientProxy(string groupName, List<RecordedHubMessage> messages) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            messages.Add(new RecordedHubMessage(groupName, method, args));
            return Task.CompletedTask;
        }
    }

    private sealed class NoopGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed record RecordedHubMessage(string GroupName, string Method, object?[] Args);
}