using System.Reflection;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.LiveClasses;

namespace OetLearner.Api.Tests.LiveClasses;

/// <summary>
/// Verifies that the LiveClassService DTO projection methods do not leak
/// host-only Zoom fields (ZoomStartUrl, ZoomPasscode, ZoomMeetingId) to
/// learner-facing responses, and documents any raw-S3-key exposure in
/// recording DTOs.
/// </summary>
public sealed class LiveClassProjectionTests
{
    // -------------------------------------------------------------------------
    // 1. Learner join token: must NOT expose ZoomStartUrl or ZoomPasscode
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LearnerJoinToken_ReturnsJoinUrl_NotStartUrl()
    {
        var session = NewSession();
        var token = await InvokeCreateJoinTokenAsync(session, "Learner Name", "learner@example.com", role: 0);

        // Learner (role=0) should receive the join URL, not the host start URL.
        Assert.Equal("https://zoom.us/j/learner-join", token.JoinUrl);
        Assert.NotEqual("https://zoom.us/s/host-secret", token.JoinUrl);
    }

    [Fact]
    public async Task LearnerJoinToken_PropertyNames_DoNotContainStartUrl()
    {
        var session = NewSession();
        var token = await InvokeCreateJoinTokenAsync(session, "Learner Name", "learner@example.com", role: 0);

        // The DTO record type itself should not have a property named ZoomStartUrl.
        var propertyNames = PropertyNames(token);
        Assert.DoesNotContain("ZoomStartUrl", propertyNames);
    }

    [Fact]
    public async Task LearnerJoinToken_DoesNotContainZoomPasscode_AsDistinctField()
    {
        var session = NewSession();
        var token = await InvokeCreateJoinTokenAsync(session, "Learner Name", "learner@example.com", role: 0);

        // The DTO uses 'PassWord' (Zoom SDK convention), not a field literally named ZoomPasscode.
        var propertyNames = PropertyNames(token);
        Assert.DoesNotContain("ZoomPasscode", propertyNames);
    }

    [Fact]
    public async Task ExpertJoinToken_ReturnsStartUrl_NotJoinUrl()
    {
        var session = NewSession();
        var token = await InvokeCreateJoinTokenAsync(session, "Expert Name", "expert@example.com", role: 1);

        // Expert (role=1) should receive the host start URL.
        Assert.Equal("https://zoom.us/s/host-secret", token.JoinUrl);
    }

    // -------------------------------------------------------------------------
    // 2. Session summary: must NOT expose ZoomMeetingId or ZoomPasscode
    // -------------------------------------------------------------------------

    [Fact]
    public void SessionSummaryDto_PropertyNames_DoNotContainZoomMeetingId()
    {
        var liveClass = NewLiveClass();
        var session = NewSession();
        var dto = InvokeMapSessionSummary(liveClass, session, isEnrolled: false, now: DateTimeOffset.UtcNow);

        var propertyNames = PropertyNames(dto);
        Assert.DoesNotContain("ZoomMeetingId", propertyNames);
        Assert.DoesNotContain("ZoomPasscode", propertyNames);
        Assert.DoesNotContain("ZoomStartUrl", propertyNames);
        Assert.DoesNotContain("ZoomJoinUrl", propertyNames);
    }

    [Fact]
    public void ListItemDto_PropertyNames_DoNotContainZoomFields()
    {
        var liveClass = NewLiveClass();
        var dto = InvokeMapListItem(liveClass, enrolledSessionIds: [], now: DateTimeOffset.UtcNow);

        var propertyNames = PropertyNames(dto);
        Assert.DoesNotContain("ZoomMeetingId", propertyNames);
        Assert.DoesNotContain("ZoomPasscode", propertyNames);
        Assert.DoesNotContain("ZoomStartUrl", propertyNames);
        Assert.DoesNotContain("ZoomJoinUrl", propertyNames);
    }

    [Fact]
    public void SessionSummaryDto_ContainsExpectedPublicFields()
    {
        var liveClass = NewLiveClass();
        var session = NewSession();
        var dto = InvokeMapSessionSummary(liveClass, session, isEnrolled: true, now: DateTimeOffset.UtcNow);

        Assert.Equal(session.Id, dto.Id);
        Assert.Equal(session.ScheduledStartAt, dto.ScheduledStartAt);
        Assert.Equal(session.Capacity, dto.Capacity);
        Assert.Equal(session.EnrolledCount, dto.EnrolledCount);
        Assert.True(dto.IsEnrolled);
    }

    // -------------------------------------------------------------------------
    // 3. Recording DTO: storage keys must be resolved through IFileStorage.
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordingDto_VideoUrl_UsesStorageResolvedReadUrl()
    {
        var recording = NewRecording();
        var dto = InvokeMapRecording(recording);

        Assert.Equal("/test-media/recordings%2Fsession-1%2Fvideo.mp4", dto.VideoUrl);
        Assert.Equal("/test-media/recordings%2Fsession-1%2Ftranscript.vtt", dto.TranscriptUrl);
        Assert.DoesNotContain("recordings/session-1", dto.VideoUrl);

        var propertyNames = PropertyNames(dto);
        Assert.DoesNotContain("S3VideoKey", propertyNames);
        Assert.DoesNotContain("S3AudioKey", propertyNames);
        Assert.DoesNotContain("S3TranscriptKey", propertyNames);
    }

    [Fact]
    public void RecordingDto_PropertyNames_DoNotContainS3InternalKeys()
    {
        var recording = NewRecording();
        var dto = InvokeMapRecording(recording);

        var propertyNames = PropertyNames(dto);
        Assert.DoesNotContain("S3VideoKey", propertyNames);
        Assert.DoesNotContain("S3AudioKey", propertyNames);
        Assert.DoesNotContain("S3TranscriptKey", propertyNames);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static LiveClass NewLiveClass()
        => new()
        {
            Id = "LC-test-1",
            Slug = "test-class",
            Title = "Test Live Class",
            Description = "A test class",
            Type = LiveClassType.GroupClass,
            ProfessionTrack = "Nursing",
            Level = "B2",
            DefaultDurationMinutes = 60,
            DefaultCapacity = 30,
            CreditCost = 5,
            Status = LiveClassStatus.Published,
            TagsJson = "[]",
            RecurrenceJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Sessions = [NewSession()],
        };

    private static LiveClassSession NewSession()
        => new()
        {
            Id = "LCS-test-1",
            LiveClassId = "LC-test-1",
            ScheduledStartAt = DateTimeOffset.UtcNow.AddDays(1),
            ScheduledEndAt = DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
            Capacity = 30,
            EnrolledCount = 5,
            Status = LiveClassSessionStatus.Scheduled,
            ZoomMeetingId = 123456789,
            ZoomMeetingNumber = "123456789",
            ZoomJoinUrl = "https://zoom.us/j/learner-join",
            ZoomStartUrl = "https://zoom.us/s/host-secret",
            ZoomPasscode = "host-password",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private static LiveClassRecording NewRecording()
        => new()
        {
            Id = "LCR-test-1",
            ClassSessionId = "LCS-test-1",
            Status = LiveClassRecordingStatus.Ready,
            S3VideoKey = "recordings/session-1/video.mp4",
            S3AudioKey = "recordings/session-1/audio.mp3",
            S3TranscriptKey = "recordings/session-1/transcript.vtt",
            TranscriptText = "Hello world",
            AiSummary = "Summary",
            ChaptersJson = "[]",
            ActionItemsJson = "[]",
            RecordedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(365),
        };

    private static async Task<LiveClassJoinTokenResponse> InvokeCreateJoinTokenAsync(
        LiveClassSession session,
        string displayName,
        string? email,
        int role)
    {
        // CreateJoinToken is a private instance method, so we need a service instance.
        // We use reflection to access the private method directly.
        var serviceType = typeof(LiveClassService);
        var method = serviceType.GetMethod("CreateJoinTokenAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("CreateJoinTokenAsync method not found on LiveClassService.");

        // Build a minimal service instance using the internal constructor via reflection.
        // We stub out the ZoomMeetingService with a fake that only needs MeetingSdkKey
        // and GenerateMeetingSdkSignature.
        var service = CreateServiceStub();
        var task = (Task<LiveClassJoinTokenResponse>)(method.Invoke(service, [session, displayName, email, role, CancellationToken.None])
            ?? throw new InvalidOperationException("CreateJoinTokenAsync returned null."));
        return await task;
    }

    private static LiveClassSessionSummaryDto InvokeMapSessionSummary(
        LiveClass liveClass,
        LiveClassSession session,
        bool isEnrolled,
        DateTimeOffset now)
    {
        var method = typeof(LiveClassService).GetMethod(
                "MapSessionSummary",
                BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("MapSessionSummary not found.");
        return (LiveClassSessionSummaryDto)(method.Invoke(null, [liveClass, session, isEnrolled, now, false])
            ?? throw new InvalidOperationException("MapSessionSummary returned null."));
    }

    private static LiveClassListItemDto InvokeMapListItem(
        LiveClass liveClass,
        HashSet<string> enrolledSessionIds,
        DateTimeOffset now)
    {
        var method = typeof(LiveClassService).GetMethod(
                "MapListItem",
                BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("MapListItem not found.");
        return (LiveClassListItemDto)(method.Invoke(null, [liveClass, enrolledSessionIds, now, false])
            ?? throw new InvalidOperationException("MapListItem returned null."));
    }

    private static LiveClassRecordingDto InvokeMapRecording(LiveClassRecording recording)
    {
        var method = typeof(LiveClassService).GetMethod(
                "MapRecording",
                BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("MapRecording not found.");
        return (LiveClassRecordingDto)(method.Invoke(CreateServiceStub(), [recording])
            ?? throw new InvalidOperationException("MapRecording returned null."));
    }

    /// <summary>
    /// Creates a minimal <see cref="LiveClassService"/> instance for testing private
    /// instance methods (specifically <c>CreateJoinTokenAsync</c>). All dependencies are
    /// either null or faked with the minimum surface needed.
    /// </summary>
    private static LiveClassService CreateServiceStub()
    {
        // ZoomMeetingService reads effective runtime settings; leave SDK credentials blank
        // so GenerateMeetingSdkSignatureAsync returns null, which is acceptable here.
        var zoomOptions = new ZoomOptions();
        var zoomService = new ZoomMeetingService(
            httpClientFactory: null!,
            runtimeSettings: TestRuntimeSettingsProvider.FromZoomOptions(zoomOptions),
            logger: null!);

        var serviceType = typeof(LiveClassService);
        var ctor = serviceType.GetConstructors().FirstOrDefault()
            ?? throw new InvalidOperationException("LiveClassService has no public constructor.");

        var ctorParams = ctor.GetParameters().Select(p =>
        {
            if (p.ParameterType == typeof(ZoomMeetingService))
            {
                return (object?)zoomService;
            }

            if (p.ParameterType == typeof(IFileStorage))
            {
                return new TestFileStorage();
            }

            if (p.ParameterType == typeof(TimeProvider))
            {
                return TimeProvider.System;
            }

            return p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
        }).ToArray();

        return (LiveClassService)ctor.Invoke(ctorParams);
    }

    private static string[] PropertyNames(object obj)
        => obj.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name)
            .ToArray();

    private sealed class TestFileStorage : IFileStorage
    {
        public Task<long> WriteAsync(string key, Stream source, CancellationToken ct) => Task.FromResult(0L);
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream());
        public Task<Stream> OpenWriteAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream());
        public bool Exists(string key) => true;
        public bool Delete(string key) => true;
        public long Length(string key) => 0;
        public void Move(string sourceKey, string destKey, bool overwrite) { }
        public int DeletePrefix(string prefix) => 0;
        public string? TryResolveLocalPath(string key) => null;
        public Uri? ResolveReadUrl(string key, TimeSpan ttl) => new($"/test-media/{Uri.EscapeDataString(key)}", UriKind.Relative);
    }
}
