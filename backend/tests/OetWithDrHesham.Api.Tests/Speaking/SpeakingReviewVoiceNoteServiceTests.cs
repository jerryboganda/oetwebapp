using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Services.Speaking;

namespace OetWithDrHesham.Api.Tests.Speaking;

public sealed class SpeakingReviewVoiceNoteServiceTests : IDisposable
{
    private readonly LearnerDbContext _db;
    private readonly StubFileStorage _storage = new();
    private readonly SpeakingReviewVoiceNoteService _service;

    public SpeakingReviewVoiceNoteServiceTests()
    {
        var opts = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-voice-notes-{Guid.NewGuid():N}")
            .Options;
        _db = new LearnerDbContext(opts);
        _service = new SpeakingReviewVoiceNoteService(_db, _storage);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CreateAsync_RejectsUnassignedExpert()
    {
        var reviewId = await SeedAssignedReviewAsync("expert-owner");
        var mediaId = await SeedReadyAudioAsync("expert-stranger");

        var ex = await Assert.ThrowsAsync<ApiException>(() => _service.CreateAsync(
            reviewId,
            "expert-stranger",
            mediaId,
            durationSeconds: 12,
            writtenNotes: "note",
            rubricJson: "{}",
            CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, ex.StatusCode);
        Assert.Equal("review_not_owned", ex.ErrorCode);
        Assert.Empty(await _db.SpeakingReviewVoiceNotes.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_RejectsMediaUploadedByAnotherExpert()
    {
        var reviewId = await SeedAssignedReviewAsync("expert-owner");
        var mediaId = await SeedReadyAudioAsync("expert-stranger");

        var ex = await Assert.ThrowsAsync<ApiException>(() => _service.CreateAsync(
            reviewId,
            "expert-owner",
            mediaId,
            durationSeconds: 12,
            writtenNotes: "note",
            rubricJson: "{}",
            CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, ex.StatusCode);
        Assert.Equal("voice_note_forbidden", ex.ErrorCode);
        Assert.Empty(await _db.SpeakingReviewVoiceNotes.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CreateAndListAsync_RequireAssignedSpeakingReviewAndReadyAudio()
    {
        var reviewId = await SeedAssignedReviewAsync("expert-owner");
        var mediaId = await SeedReadyAudioAsync("expert-owner");

        var note = await _service.CreateAsync(
            reviewId,
            "expert-owner",
            mediaId,
            durationSeconds: 12,
            writtenNotes: " note ",
            rubricJson: "{}",
            CancellationToken.None);

        Assert.Equal("expert-owner", note.ExpertUserId);
        Assert.Equal("note", note.WrittenNotes);

        var visible = await _service.ListForReviewAsync(reviewId, "expert-owner", CancellationToken.None);
        var listed = Assert.Single(visible);
        Assert.Equal(note.Id, listed.Id);

        var ex = await Assert.ThrowsAsync<ApiException>(() =>
            _service.ListForReviewAsync(reviewId, "expert-stranger", CancellationToken.None));
        Assert.Equal(StatusCodes.Status403Forbidden, ex.StatusCode);
        Assert.Equal("review_not_owned", ex.ErrorCode);
    }

    private async Task<string> SeedAssignedReviewAsync(string expertId)
    {
        _db.ExpertUsers.Add(new ExpertUser
        {
            Id = expertId,
            DisplayName = expertId,
            Email = $"{expertId}@example.test",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var reviewId = $"rr-{Guid.NewGuid():N}";
        _db.ReviewRequests.Add(new ReviewRequest
        {
            Id = reviewId,
            AttemptId = $"att-{Guid.NewGuid():N}",
            SubtestCode = "speaking",
            State = ReviewRequestState.InReview,
            TurnaroundOption = "standard",
            PaymentSource = "test",
            PriceSnapshot = 0,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.ExpertReviewAssignments.Add(new ExpertReviewAssignment
        {
            Id = $"era-{Guid.NewGuid():N}",
            ReviewRequestId = reviewId,
            AssignedReviewerId = expertId,
            AssignedAt = DateTimeOffset.UtcNow,
            ClaimState = ExpertAssignmentState.Claimed,
        });
        await _db.SaveChangesAsync();
        return reviewId;
    }

    private async Task<string> SeedReadyAudioAsync(string uploadedBy)
    {
        var mediaId = $"ma-{Guid.NewGuid():N}";
        var key = $"voice-notes/{mediaId}.webm";
        _storage.AddBlob(key, [1, 2, 3]);
        _db.MediaAssets.Add(new MediaAsset
        {
            Id = mediaId,
            OriginalFilename = "voice.webm",
            MimeType = "audio/webm",
            Format = "webm",
            SizeBytes = 3,
            StoragePath = key,
            Status = MediaAssetStatus.Ready,
            UploadedBy = uploadedBy,
            UploadedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
        return mediaId;
    }
}
