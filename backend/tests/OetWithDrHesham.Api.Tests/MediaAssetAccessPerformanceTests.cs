using System.Data.Common;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Content;
using OetWithDrHesham.Api.Services.Entitlements;
using OetWithDrHesham.Api.Services.Reading;
using OetWithDrHesham.Api.Services.VideoLibrary;
using OetWithDrHesham.Api.Tests.Infrastructure;

namespace OetWithDrHesham.Api.Tests;

public sealed class MediaAssetAccessPerformanceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly CommandCounter _commands = new();
    private DbContextOptions<LearnerDbContext> _options = default!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_commands)
            .Options;

        await using var db = new LearnerDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Theory]
    [InlineData(AccessScenario.Missing, false, 1)]
    [InlineData(AccessScenario.DeniedStandalone, false, 2)]
    [InlineData(AccessScenario.FreePreview, true, 2)]
    [InlineData(AccessScenario.VocabularyOverFreePreview, false, 2)]
    [InlineData(AccessScenario.RulebookMatchingProfession, true, 3)]
    [InlineData(AccessScenario.RulebookWrongProfession, false, 3)]
    [InlineData(AccessScenario.SpeakingSharedResource, true, 3)]
    [InlineData(AccessScenario.MaterialRootFile, true, 4)]
    [InlineData(AccessScenario.VideoThumbnail, true, 3)]
    [InlineData(AccessScenario.VideoAttachmentAllowed, true, 3)]
    [InlineData(AccessScenario.VideoAttachmentDenied, false, 3)]
    [InlineData(AccessScenario.ActiveResultTemplate, true, 3)]
    [InlineData(AccessScenario.WritingStimulus, true, 3)]
    [InlineData(AccessScenario.PaperImage, true, 3)]
    [InlineData(AccessScenario.PaperAudio, true, 3)]
    [InlineData(AccessScenario.ProtectedPaperOverFreePreview, false, 3)]
    [InlineData(AccessScenario.ReadingPaperModeDisabled, false, 3)]
    [InlineData(AccessScenario.WritingVoiceNote, true, 2)]
    [InlineData(AccessScenario.AdminStandalone, true, 1)]
    [InlineData(AccessScenario.OwnerStandalone, true, 1)]
    public async Task Authorization_matrix_preserves_decisions_and_bounds_relational_commands(
        AccessScenario scenario,
        bool expectedAccess,
        int expectedCommands)
    {
        await using var db = new LearnerDbContext(_options);
        var mediaId = $"media-{scenario}-{Guid.NewGuid():N}";
        var contentEntitlements = new MediaPerformanceContentEntitlementService();
        var readingPolicy = new MediaPerformanceReadingPolicyService();
        var videoEntitlements = new MediaPerformanceVideoEntitlementService();
        var principal = LearnerPrincipal();

        await SeedScenarioAsync(
            db,
            scenario,
            mediaId,
            contentEntitlements,
            readingPolicy,
            videoEntitlements);
        db.ChangeTracker.Clear();
        _commands.Commands.Clear();

        if (scenario == AccessScenario.AdminStandalone)
        {
            principal = Principal("admin-user", ApplicationUserRoles.Admin);
        }
        else if (scenario == AccessScenario.OwnerStandalone)
        {
            principal = LearnerPrincipal();
        }

        var service = new MediaAssetAccessService(
            db,
            contentEntitlements,
            readingPolicy,
            new MaterialAccessService(db, new MediaPerformanceEffectiveEntitlementResolver()),
            videoEntitlements);

        var actual = await service.CanAccessAsync(principal, mediaId, CancellationToken.None);

        Assert.Equal(expectedAccess, actual);
        Assert.Equal(expectedCommands, _commands.Commands.Count);

        if (scenario is AccessScenario.PaperImage or AccessScenario.PaperAudio)
        {
            Assert.Equal(1, contentEntitlements.AllowCalls);
            Assert.Contains(_commands.Commands, command =>
                command.Contains("UNION ALL", StringComparison.OrdinalIgnoreCase));
        }
        else if (scenario is AccessScenario.ProtectedPaperOverFreePreview
                 or AccessScenario.ReadingPaperModeDisabled)
        {
            Assert.Equal(0, contentEntitlements.AllowCalls);
        }
    }

    private static ClaimsPrincipal LearnerPrincipal()
        => Principal("learner-1", ApplicationUserRoles.Learner, "medicine");

    private static ClaimsPrincipal Principal(string userId, string role, string? profession = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Role, role),
        };
        if (profession is not null)
        {
            claims.Add(new Claim("prof", profession));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static async Task SeedScenarioAsync(
        LearnerDbContext db,
        AccessScenario scenario,
        string mediaId,
        MediaPerformanceContentEntitlementService contentEntitlements,
        MediaPerformanceReadingPolicyService readingPolicy,
        MediaPerformanceVideoEntitlementService videoEntitlements)
    {
        if (scenario == AccessScenario.Missing)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var media = CreateMedia(mediaId, scenario);
        db.MediaAssets.Add(media);

        switch (scenario)
        {
            case AccessScenario.FreePreview:
                AddFreePreview(db, mediaId, now);
                break;

            case AccessScenario.VocabularyOverFreePreview:
                AddFreePreview(db, mediaId, now);
                db.VocabularyTerms.Add(new VocabularyTerm
                {
                    Id = $"term-{Guid.NewGuid():N}",
                    Term = "anaemia",
                    ExamTypeCode = "oet",
                    ProfessionId = "medicine",
                    Category = "spelling",
                    AudioMediaAssetId = mediaId,
                    Status = "active",
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                break;

            case AccessScenario.RulebookMatchingProfession:
            case AccessScenario.RulebookWrongProfession:
                db.RulebookVersions.Add(new RulebookVersion
                {
                    Id = $"rulebook-{Guid.NewGuid():N}",
                    Kind = "writing",
                    Profession = scenario == AccessScenario.RulebookMatchingProfession ? "medicine" : "nursing",
                    Version = "1.0.0",
                    Status = RulebookStatus.Published,
                    ReferencePdfAssetId = mediaId,
                    CreatedAt = now,
                    UpdatedAt = now,
                    PublishedAt = now,
                });
                break;

            case AccessScenario.SpeakingSharedResource:
                db.SpeakingSharedResources.Add(new SpeakingSharedResource
                {
                    Id = $"speaking-{Guid.NewGuid():N}",
                    Kind = SpeakingSharedResourceKinds.WarmUpQuestions,
                    Title = "Warm-up questions",
                    MediaAssetId = mediaId,
                    Status = ContentStatus.Published,
                    CreatedAt = now,
                    UpdatedAt = now,
                    PublishedAt = now,
                });
                break;

            case AccessScenario.MaterialRootFile:
                db.MaterialFiles.Add(new MaterialFile
                {
                    Id = $"material-{Guid.NewGuid():N}",
                    MediaAssetId = mediaId,
                    SubtestCode = "reading",
                    Kind = "pdf",
                    Title = "Root material",
                    Status = ContentStatus.Published,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                break;

            case AccessScenario.VideoThumbnail:
                db.LibraryVideos.Add(CreateVideo(mediaId, isThumbnail: true, now));
                break;

            case AccessScenario.VideoAttachmentAllowed:
            case AccessScenario.VideoAttachmentDenied:
            {
                var video = CreateVideo(mediaId, isThumbnail: false, now);
                db.LibraryVideos.Add(video);
                db.VideoAttachments.Add(new VideoAttachment
                {
                    Id = Guid.NewGuid(),
                    VideoId = video.Id,
                    MediaAssetId = mediaId,
                    Title = "Worksheet",
                    CreatedAt = now,
                });
                videoEntitlements.Allowed = scenario == AccessScenario.VideoAttachmentAllowed;
                break;
            }

            case AccessScenario.ActiveResultTemplate:
                db.ResultTemplateAssets.Add(new ResultTemplateAsset
                {
                    Id = $"template-{Guid.NewGuid():N}",
                    TemplateKey = $"template-{Guid.NewGuid():N}",
                    Title = "Result template",
                    ProfessionId = "medicine",
                    MediaAssetId = mediaId,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                break;

            case AccessScenario.WritingStimulus:
                db.WritingScenarios.Add(CreateWritingScenario(mediaId, now));
                break;

            case AccessScenario.PaperImage:
            case AccessScenario.PaperAudio:
                AddPaper(db, mediaId, PaperAssetRole.QuestionPaper, "listening", now);
                contentEntitlements.Allowed = true;
                break;

            case AccessScenario.ProtectedPaperOverFreePreview:
                AddPaper(db, mediaId, PaperAssetRole.AnswerKey, "listening", now);
                AddFreePreview(db, mediaId, now);
                contentEntitlements.Allowed = true;
                break;

            case AccessScenario.ReadingPaperModeDisabled:
                AddPaper(db, mediaId, PaperAssetRole.QuestionPaper, "reading", now);
                AddFreePreview(db, mediaId, now);
                contentEntitlements.Allowed = true;
                readingPolicy.AllowPaperReadingMode = false;
                break;

            case AccessScenario.WritingVoiceNote:
            {
                var scenarioRow = CreateWritingScenario(null, now);
                var submission = new WritingSubmission
                {
                    Id = Guid.NewGuid(),
                    UserId = "learner-1",
                    ScenarioId = scenarioRow.Id,
                    LetterContent = "Test letter",
                    LetterContentHash = "hash",
                    SubmittedAt = now,
                    StartedAt = now.AddMinutes(-30),
                    CreatedAt = now,
                };
                db.WritingScenarios.Add(scenarioRow);
                db.WritingSubmissions.Add(submission);
                db.WritingTutorReviews.Add(new WritingTutorReview
                {
                    Id = Guid.NewGuid(),
                    SubmissionId = submission.Id,
                    TutorId = "tutor-1",
                    Status = "submitted",
                    CreatedAt = now,
                });
                db.WritingReviewVoiceNotes.Add(new WritingReviewVoiceNote
                {
                    Id = Guid.NewGuid(),
                    SubmissionId = submission.Id,
                    TutorId = "tutor-1",
                    MediaAssetId = mediaId,
                    Status = "ready",
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                break;
            }

            case AccessScenario.OwnerStandalone:
                media.UploadedBy = "learner-1";
                break;
        }

        await db.SaveChangesAsync();
    }

    private static MediaAsset CreateMedia(string mediaId, AccessScenario scenario)
    {
        var isAudio = scenario == AccessScenario.PaperAudio;
        var isImage = scenario is AccessScenario.PaperImage or AccessScenario.VideoThumbnail
            or AccessScenario.ActiveResultTemplate;
        var extension = isAudio ? "mp3" : isImage ? "jpg" : "pdf";
        var mimeType = isAudio ? "audio/mpeg" : isImage ? "image/jpeg" : "application/pdf";

        return new MediaAsset
        {
            Id = mediaId,
            OriginalFilename = $"{mediaId}.{extension}",
            MimeType = mimeType,
            Format = extension,
            MediaKind = isAudio ? "audio" : isImage ? "image" : "document",
            SizeBytes = 4,
            StoragePath = $"media/{mediaId}.{extension}",
            Status = MediaAssetStatus.Ready,
            UploadedBy = "admin-user",
            UploadedAt = DateTimeOffset.UtcNow,
        };
    }

    private static void AddFreePreview(LearnerDbContext db, string mediaId, DateTimeOffset now)
        => db.FreePreviewAssets.Add(new FreePreviewAsset
        {
            Id = $"preview-{Guid.NewGuid():N}",
            Title = "Free preview",
            PreviewType = "sample_task",
            MediaAssetId = mediaId,
            Status = ContentStatus.Published,
            CreatedAt = now,
        });

    private static void AddPaper(
        LearnerDbContext db,
        string mediaId,
        PaperAssetRole role,
        string subtestCode,
        DateTimeOffset now)
    {
        var paper = new ContentPaper
        {
            Id = $"paper-{Guid.NewGuid():N}",
            SubtestCode = subtestCode,
            Title = "Performance paper",
            Slug = $"performance-{Guid.NewGuid():N}",
            AppliesToAllProfessions = true,
            Difficulty = "standard",
            EstimatedDurationMinutes = 60,
            Status = ContentStatus.Published,
            TagsCsv = "access:premium",
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = now,
        };
        db.ContentPapers.Add(paper);
        db.ContentPaperAssets.Add(new ContentPaperAsset
        {
            Id = $"paper-asset-{Guid.NewGuid():N}",
            PaperId = paper.Id,
            Role = role,
            MediaAssetId = mediaId,
            IsPrimary = true,
            CreatedAt = now,
        });
    }

    private static LibraryVideo CreateVideo(string mediaId, bool isThumbnail, DateTimeOffset now)
        => new()
        {
            Id = $"video-{Guid.NewGuid():N}",
            Title = "Video",
            CustomThumbnailMediaAssetId = isThumbnail ? mediaId : null,
            AccessTier = "premium",
            ProfessionIdsJson = "[]",
            Status = ContentStatus.Published,
            CreatedAt = now,
            UpdatedAt = now,
        };

    private static WritingScenario CreateWritingScenario(string? mediaId, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            Title = "Writing scenario",
            LetterType = "routine_referral",
            Profession = "medicine",
            AuthorId = "admin-user",
            Status = "published",
            StimulusPdfMediaAssetId = mediaId,
            CreatedAt = now,
            UpdatedAt = now,
        };

    public enum AccessScenario
    {
        Missing,
        DeniedStandalone,
        FreePreview,
        VocabularyOverFreePreview,
        RulebookMatchingProfession,
        RulebookWrongProfession,
        SpeakingSharedResource,
        MaterialRootFile,
        VideoThumbnail,
        VideoAttachmentAllowed,
        VideoAttachmentDenied,
        ActiveResultTemplate,
        WritingStimulus,
        PaperImage,
        PaperAudio,
        ProtectedPaperOverFreePreview,
        ReadingPaperModeDisabled,
        WritingVoiceNote,
        AdminStandalone,
        OwnerStandalone,
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}

public sealed class MediaEndpointStoragePerformanceTests
{
    [Fact]
    public async Task Download_opens_once_without_exists_and_preserves_metadata()
    {
        using var factory = new StorageProbeFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        const string mediaId = "single-open-media";

        db.MediaAssets.Add(new MediaAsset
        {
            Id = mediaId,
            OriginalFilename = "sample.pdf",
            MimeType = "application/pdf",
            Format = "pdf",
            MediaKind = "document",
            SizeBytes = 4,
            StoragePath = "media/sample.pdf",
            Status = MediaAssetStatus.Ready,
            UploadedBy = "admin-user",
            UploadedAt = now,
        });
        AddPublishedPreview(db, mediaId, now);
        await db.SaveChangesAsync();

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", "learner-1");
        client.DefaultRequestHeaders.Add("X-Debug-Role", ApplicationUserRoles.Learner);
        client.DefaultRequestHeaders.Add("X-Debug-Profession", "medicine");

        using var response = await client.GetAsync($"/v1/media/{mediaId}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(4, response.Content.Headers.ContentLength);
        Assert.Equal(0, factory.Storage.ExistsCalls);
        Assert.Equal(0, factory.Storage.OpenReadCalls);
        Assert.Equal(1, factory.Storage.CombinedReadCalls);
    }

    [Fact]
    public async Task Download_maps_only_storage_file_not_found_to_404()
    {
        using var factory = new StorageProbeFactory();
        factory.Storage.ReadException = new FileNotFoundException("missing");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        const string mediaId = "missing-storage-media";

        db.MediaAssets.Add(new MediaAsset
        {
            Id = mediaId,
            OriginalFilename = "missing.pdf",
            MimeType = "application/pdf",
            Format = "pdf",
            MediaKind = "document",
            SizeBytes = 4,
            StoragePath = "media/missing.pdf",
            Status = MediaAssetStatus.Ready,
            UploadedBy = "admin-user",
            UploadedAt = now,
        });
        AddPublishedPreview(db, mediaId, now);
        await db.SaveChangesAsync();

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", "learner-1");
        client.DefaultRequestHeaders.Add("X-Debug-Role", ApplicationUserRoles.Learner);
        client.DefaultRequestHeaders.Add("X-Debug-Profession", "medicine");

        using var response = await client.GetAsync($"/v1/media/{mediaId}/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        factory.Storage.ReadException = new UnauthorizedAccessException("storage denied");
        using var forbiddenResponse = await client.GetAsync($"/v1/media/{mediaId}/content");
        Assert.Equal(HttpStatusCode.InternalServerError, forbiddenResponse.StatusCode);

        factory.Storage.ReadException = new TimeoutException("storage timeout");
        using var timeoutResponse = await client.GetAsync($"/v1/media/{mediaId}/content");
        Assert.Equal(HttpStatusCode.InternalServerError, timeoutResponse.StatusCode);

        Assert.Equal(0, factory.Storage.ExistsCalls);
        Assert.Equal(3, factory.Storage.CombinedReadCalls);
    }

    private static void AddPublishedPreview(LearnerDbContext db, string mediaId, DateTimeOffset now)
        => db.FreePreviewAssets.Add(new FreePreviewAsset
        {
            Id = $"preview-{mediaId}",
            Title = "Preview",
            PreviewType = "sample_task",
            MediaAssetId = mediaId,
            Status = ContentStatus.Published,
            CreatedAt = now,
        });

    private sealed class StorageProbeFactory : TestWebApplicationFactory
    {
        public ProbeFileStorage Storage { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFileStorage>();
                services.AddSingleton<IFileStorage>(Storage);
            });
        }
    }

    private sealed class ProbeFileStorage : IFileStorage
    {
        public int ExistsCalls { get; private set; }
        public int OpenReadCalls { get; private set; }
        public int CombinedReadCalls { get; private set; }
        public Exception? ReadException { get; set; }

        public Task<long> WriteAsync(string key, Stream source, CancellationToken ct)
            => Task.FromResult(source.Length);

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
        {
            OpenReadCalls++;
            return Task.FromResult<Stream>(new MemoryStream([1, 2, 3, 4]));
        }

        public Task<FileStorageReadResult> OpenReadWithMetadataAsync(string key, CancellationToken ct)
        {
            CombinedReadCalls++;
            if (ReadException is not null)
            {
                return Task.FromException<FileStorageReadResult>(ReadException);
            }

            return Task.FromResult(new FileStorageReadResult(new MemoryStream([1, 2, 3, 4]), 4));
        }

        public Task<Stream> OpenWriteAsync(string key, CancellationToken ct)
            => Task.FromResult<Stream>(new MemoryStream());

        public Task<bool> ExistsAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ExistsCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }

        public Task<long> LengthAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(4L);
        }

        public Task MoveAsync(string sourceKey, string destKey, bool overwrite, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(0);
        }
        public string? TryResolveLocalPath(string key) => null;
        public Uri? ResolveReadUrl(string key, TimeSpan ttl) => null;
    }
}

internal sealed class MediaPerformanceContentEntitlementService : IContentEntitlementService
{
    public bool Allowed { get; set; }
    public int AllowCalls { get; private set; }

    public Task<ContentEntitlementResult> AllowAccessAsync(
        string? userId,
        ContentPaper paper,
        CancellationToken ct)
    {
        AllowCalls++;
        return Task.FromResult(new ContentEntitlementResult(
            Allowed,
            Allowed ? "test_allowed" : "test_denied",
            null,
            null));
    }

    public Task RequireAccessAsync(string? userId, ContentPaper paper, CancellationToken ct)
        => Task.CompletedTask;

    public bool IsAdmin(ClaimsPrincipal? principal) => false;
}

internal sealed class MediaPerformanceReadingPolicyService : IReadingPolicyService
{
    public bool AllowPaperReadingMode { get; set; } = true;

    public Task<ReadingPolicy> GetGlobalAsync(CancellationToken ct)
        => Task.FromResult(new ReadingPolicy());

    public Task<ReadingUserPolicyOverride?> GetUserOverrideAsync(string userId, CancellationToken ct)
        => Task.FromResult<ReadingUserPolicyOverride?>(null);

    public Task<ReadingResolvedPolicy> ResolveForUserAsync(string? userId, CancellationToken ct)
        => Task.FromResult(new ReadingResolvedPolicy(
            AttemptsPerPaperPerUser: 3,
            AttemptCooldownMinutes: 0,
            PartATimerStrictness: "strict",
            PartATimerMinutes: 15,
            PartBCTimerMinutes: 45,
            GracePeriodSeconds: 0,
            OnExpirySubmitPolicy: "auto",
            CountdownWarnings: [],
            EnabledQuestionTypes: [],
            ShortAnswerNormalisation: "none",
            ShortAnswerAcceptSynonyms: false,
            MatchingAllowPartialCredit: false,
            UnknownTypeFallbackPolicy: "skip",
            ShowExplanationsAfterSubmit: false,
            ShowExplanationsOnlyIfWrong: false,
            ShowCorrectAnswerOnReview: false,
            SubmitRateLimitPerMinute: 10,
            AutosaveRateLimitPerMinute: 30,
            ExtraTimeEntitlementPct: 0,
            AllowMultipleConcurrentAttempts: false,
            AllowPausingAttempt: false,
            AllowResumeAfterExpiry: false,
            AllowPaperReadingMode: AllowPaperReadingMode));

    public Task<ReadingPolicy> UpsertGlobalAsync(
        ReadingPolicy next,
        string adminId,
        CancellationToken ct)
        => Task.FromResult(next);

    public Task<ReadingUserPolicyOverride> UpsertUserOverrideAsync(
        string userId,
        ReadingUserPolicyOverride next,
        string adminId,
        CancellationToken ct)
        => Task.FromResult(next);
}

internal sealed class MediaPerformanceVideoEntitlementService : IVideoEntitlementService
{
    public bool Allowed { get; set; }

    public Task<VideoEntitlementResult> AllowAccessAsync(
        string? userId,
        LibraryVideo video,
        CancellationToken ct)
        => Task.FromResult(new VideoEntitlementResult(
            Allowed,
            Allowed ? "test_allowed" : "test_denied",
            null));

    public Task RequireAccessAsync(string? userId, LibraryVideo video, CancellationToken ct)
        => Task.CompletedTask;

    public Task<VideoAccessContext> ResolveContextAsync(
        string? userId,
        bool isAdmin,
        CancellationToken ct)
        => Task.FromResult(new VideoAccessContext(
            IsAdmin: isAdmin,
            Authenticated: true,
            HasEligibleSubscription: Allowed,
            Frozen: false,
            Expired: false,
            PlanGrantsPremium: Allowed,
            AddOnGrantsPremium: false,
            CurrentTier: Allowed ? "premium" : "free"));

    public VideoEntitlementResult Evaluate(VideoAccessContext context, LibraryVideo video)
        => new(context.PlanGrantsPremium, "test", context.CurrentTier);
}

internal sealed class MediaPerformanceEffectiveEntitlementResolver : IEffectiveEntitlementResolver
{
    public Task<EffectiveEntitlementSnapshot> ResolveAsync(string? userId, CancellationToken ct)
        => Task.FromResult(new EffectiveEntitlementSnapshot(
            UserId: userId,
            HasEligibleSubscription: false,
            IsTrial: false,
            Tier: "free",
            SubscriptionId: null,
            SubscriptionStatus: null,
            PlanId: null,
            PlanVersionId: null,
            PlanCode: null,
            AiQuotaPlanCode: null,
            AiQuotaPlanCodeSource: null,
            ActiveAddOnCodes: [],
            IsFrozen: false,
            Trace: []));
}
