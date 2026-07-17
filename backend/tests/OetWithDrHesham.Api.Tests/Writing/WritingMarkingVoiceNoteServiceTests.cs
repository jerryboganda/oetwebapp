using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Services.Content;
using OetWithDrHesham.Api.Services.Writing;

namespace OetWithDrHesham.Api.Tests.Writing;

/// <summary>
/// Unit coverage for the submission-keyed tutor voice note (System A) used by the
/// Writing marking workspace for BOTH mock and normal writing. Verifies the
/// one-note-per-submission upsert invariant, media validation, and the learner
/// access gate (ownership + review-submitted), including the IDOR guard.
///
/// SQLite in-memory + EnsureCreated (per repo convention) so the real EF model —
/// including the unique index on SubmissionId — is exercised.
/// </summary>
public sealed class WritingMarkingVoiceNoteServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;
    private readonly WritingMarkingVoiceNoteService _service;

    public WritingMarkingVoiceNoteServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new LearnerDbContext(options);
        _db.Database.EnsureCreated();
        _service = new WritingMarkingVoiceNoteService(_db, new StubFileStorage());
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Upsert_CreatesThenReplaces_OneNotePerSubmission()
    {
        var submissionId = await SeedSubmissionAsync();
        await SeedAudioMediaAsync("media-1");
        await SeedAudioMediaAsync("media-2");

        var first = await _service.UpsertAsync(submissionId, "tutor-1", "media-1", 30, default);
        Assert.Equal("media-1", first.MediaAssetId);
        Assert.Equal("/v1/media/media-1/content", first.Url);

        var second = await _service.UpsertAsync(submissionId, "tutor-1", "media-2", 45, default);
        Assert.Equal("media-2", second.MediaAssetId);

        // The note is replaced in place — never a second row for the same submission.
        Assert.Equal(1, await _db.WritingReviewVoiceNotes.CountAsync(n => n.SubmissionId == submissionId));
    }

    [Fact]
    public async Task Upsert_RejectsNonAudioMedia()
    {
        var submissionId = await SeedSubmissionAsync();
        await SeedAudioMediaAsync("doc-1", mime: "application/pdf");

        await Assert.ThrowsAsync<ApiException>(
            () => _service.UpsertAsync(submissionId, "tutor-1", "doc-1", 10, default));
    }

    [Fact]
    public async Task Upsert_RejectsMediaUploadedByAnotherAccount()
    {
        var submissionId = await SeedSubmissionAsync();
        await SeedAudioMediaAsync("media-1", uploadedBy: "someone-else");

        await Assert.ThrowsAsync<ApiException>(
            () => _service.UpsertAsync(submissionId, "tutor-1", "media-1", 10, default));
    }

    [Fact]
    public async Task GetForLearner_ReturnsNull_WhenReviewNotSubmitted()
    {
        var submissionId = await SeedSubmissionAsync();
        await SeedAudioMediaAsync("media-1");
        await _service.UpsertAsync(submissionId, "tutor-1", "media-1", 30, default);

        // No submitted tutor review yet — the learner must not see a draft note.
        Assert.Null(await _service.GetForLearnerAsync("learner-1", submissionId, default));
    }

    [Fact]
    public async Task GetForLearner_ReturnsNote_WhenOwnedAndReviewSubmitted()
    {
        var submissionId = await SeedSubmissionAsync();
        await SeedAudioMediaAsync("media-1");
        await _service.UpsertAsync(submissionId, "tutor-1", "media-1", 30, default);
        await SeedSubmittedReviewAsync(submissionId);

        var note = await _service.GetForLearnerAsync("learner-1", submissionId, default);
        Assert.NotNull(note);
        Assert.Equal("/v1/media/media-1/content", note!.Url);
    }

    [Fact]
    public async Task GetForLearner_Throws_ForNonOwner()
    {
        var submissionId = await SeedSubmissionAsync(userId: "learner-1");
        await SeedAudioMediaAsync("media-1");
        await _service.UpsertAsync(submissionId, "tutor-1", "media-1", 30, default);
        await SeedSubmittedReviewAsync(submissionId);

        await Assert.ThrowsAsync<ApiException>(
            () => _service.GetForLearnerAsync("intruder", submissionId, default));
    }

    private async Task<Guid> SeedSubmissionAsync(string userId = "learner-1")
    {
        var id = Guid.NewGuid();
        _db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = id,
            UserId = userId,
            ScenarioId = Guid.NewGuid(),
            Mode = "mock",
            LetterContent = "Dear Dr Smith, I am writing to refer Mr Jones.",
            LetterContentHash = "hash-1",
            WordCount = 120,
            Status = "awaiting_review",
            GradingTier = "express",
            InputSource = "typed",
            StartedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
        return id;
    }

    private async Task SeedAudioMediaAsync(
        string id,
        string uploadedBy = "tutor-1",
        string mime = "audio/webm",
        MediaAssetStatus status = MediaAssetStatus.Ready)
    {
        _db.MediaAssets.Add(new MediaAsset
        {
            Id = id,
            OriginalFilename = $"{id}.webm",
            MimeType = mime,
            Format = "webm",
            SizeBytes = 1024,
            DurationSeconds = 30,
            StoragePath = $"test/{id}.webm",
            Status = status,
            MediaKind = "audio",
            UploadedBy = uploadedBy,
            UploadedAt = DateTimeOffset.UtcNow,
            ProcessedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedSubmittedReviewAsync(Guid submissionId, string tutorId = "tutor-1")
    {
        _db.WritingTutorReviews.Add(new WritingTutorReview
        {
            Id = Guid.NewGuid(),
            SubmissionId = submissionId,
            TutorId = tutorId,
            Status = "submitted",
            CreatedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    private sealed class StubFileStorage : IFileStorage
    {
        public Task<long> WriteAsync(string key, Stream source, CancellationToken ct) => Task.FromResult(0L);
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream());
        public Task<Stream> OpenWriteAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream());
        public Task<bool> ExistsAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
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
            return Task.FromResult(0L);
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
        public string? TryResolveLocalPath(string key) => key;
        public Uri? ResolveReadUrl(string key, TimeSpan ttl) => null;
    }
}
