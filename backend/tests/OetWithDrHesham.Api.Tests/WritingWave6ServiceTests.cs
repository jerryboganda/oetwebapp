using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Services.Ai;
using OetWithDrHesham.Api.Services.Content;
using OetWithDrHesham.Api.Services.Rulebook;
using OetWithDrHesham.Api.Services.Writing;
using OetWithDrHesham.Api.Services.Writing.Configuration;

namespace OetWithDrHesham.Api.Tests;

public class WritingWave6ServiceTests
{
    private const string UserId = "learner-writing-wave6";

    [Fact]
    public async Task CoachHints_NormalizeUntrustedAiShape()
    {
        var db = BuildDb();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var gateway = new StubAiGateway("""
            {
              "hints": [
                { "category": "invented", "text": "  Keep the purpose explicit.  ", "ruleId": "R12<script>", "charStart": 40, "charEnd": 2 }
              ]
            }
            """);
        var service = new WritingCoachServiceV2(
            db,
            gateway,
            cache,
            TestRuntimeSettingsProvider.FromWritingOptions(new WritingV2Options { CoachMinSecondsBetweenHints = 0 }),
            new FixedClock(),
            NullLogger<WritingCoachServiceV2>.Instance);

        var hints = await service.RequestHintsAsync(UserId, new WritingCoachHintRequest(
            SessionId: "coach-session",
            ScenarioId: Guid.NewGuid(),
            LetterContent: "Dear Doctor, this is my current draft.",
            WordCount: 8,
            LetterType: "LT-RR",
            Profession: "medicine"), CancellationToken.None);

        var hint = Assert.Single(hints);
        Assert.Equal("style", hint.Category);
        Assert.Equal("Keep the purpose explicit.", hint.Text);
        Assert.Null(hint.RuleId);
        Assert.Null(hint.CharStart);
        Assert.Null(hint.CharEnd);
        Assert.Equal(AiFeatureCodes.WritingCoachV1, gateway.LastRequest?.FeatureCode);
        Assert.Equal(RuleKind.Writing, gateway.LastContext?.Kind);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task OcrUpload_PersistsBytesAndManualRequiredIsTerminalWhenDisabled()
    {
        var db = BuildDb();
        var storage = new InMemoryFileStorage();
        var service = new WritingOcrService(
            db,
            new StubHttpClientFactory(),
            storage,
            new FixedClock(),
            Options.Create(new WritingV2Options { OcrEnabled = false }),
            TestRuntimeSettingsProvider.FromWritingOptions(new WritingV2Options { OcrEnabled = false }),
            new StubOcrService(),
            new StubProviderRegistry(),
            NullLogger<WritingOcrService>.Instance);
        await using var image = new MemoryStream([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4]);
        var file = new FormFile(image, 0, image.Length, "images", "letter.png");
        file.Headers = new HeaderDictionary();
        file.ContentType = "image/png";

        var queued = await service.QueueOcrJobAsync(UserId, new[] { file }, null, CancellationToken.None);
        var completed = await service.GetJobAsync(UserId, queued.Id, CancellationToken.None);

        Assert.Single(storage.Keys);
        Assert.StartsWith("storage:///writing/ocr/", Assert.Single(queued.ImageUrls), StringComparison.Ordinal);
        Assert.NotNull(completed);
        Assert.Equal("manual_required", completed.Status);
        Assert.Contains("transcribe", completed.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task TutorReview_RequiresClaimAndCreatesLinkedAdjustedGrade()
    {
        var db = BuildDb();
        var clock = new FixedClock();
        var scenarioId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        db.WritingScenarios.Add(Scenario(scenarioId));
        db.WritingSubmissions.Add(Submission(submissionId, UserId, scenarioId, clock.GetUtcNow()));
        var originalGradeId = Guid.NewGuid();
        db.WritingGrades.Add(Grade(originalGradeId, submissionId, 30, clock.GetUtcNow()));
        db.WritingTutorReviewAssignments.Add(new WritingTutorReviewAssignment
        {
            Id = Guid.NewGuid(),
            SubmissionId = submissionId,
            TutorId = string.Empty,
            ClaimedAt = clock.GetUtcNow(),
            DueAt = clock.GetUtcNow().AddHours(24),
            Status = "pending",
        });
        await db.SaveChangesAsync();
        var service = new WritingTutorReviewService(
            db,
            clock,
            TestRuntimeSettingsProvider.FromWritingOptions(new WritingV2Options()),
            new WritingModerationService(db, NullLogger<WritingModerationService>.Instance),
            NullLogger<WritingTutorReviewService>.Instance);

        await service.ClaimAsync("tutor-1", submissionId, CancellationToken.None);
        await Assert.ThrowsAsync<ApiException>(() => service.SubmitReviewAsync(
            "tutor-2",
            submissionId,
            new WritingTutorReviewInternalSubmitRequest("feedback", null, null),
            CancellationToken.None));

        await service.SubmitReviewAsync(
            "tutor-1",
            submissionId,
            new WritingTutorReviewInternalSubmitRequest(
                "feedback",
                null,
                new Dictionary<string, int> { ["c1"] = 3, ["c2"] = 7, ["c3"] = 7, ["c4"] = 7, ["c5"] = 7, ["c6"] = 7 }),
            CancellationToken.None);

        var adjusted = Assert.Single(await db.WritingGrades.AsNoTracking().Where(g => g.SubmissionId == submissionId).ToListAsync());
        Assert.Equal(originalGradeId, adjusted.Id);
        Assert.Equal(38, adjusted.RawTotal);
        Assert.NotNull(adjusted.TutorReviewId);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task MarkingSurfaceSubmit_RequiresClaimedAssignment()
    {
        var db = BuildDb();
        var clock = new FixedClock();
        var scenarioId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        db.WritingScenarios.Add(Scenario(scenarioId));
        db.WritingSubmissions.Add(Submission(submissionId, UserId, scenarioId, clock.GetUtcNow()));
        db.WritingGrades.Add(Grade(Guid.NewGuid(), submissionId, 30, clock.GetUtcNow()));
        db.WritingTutorReviewAssignments.Add(new WritingTutorReviewAssignment
        {
            Id = Guid.NewGuid(),
            SubmissionId = submissionId,
            TutorId = "tutor-1",
            ClaimedAt = clock.GetUtcNow(),
            DueAt = clock.GetUtcNow().AddHours(24),
            Status = "claimed",
        });
        await db.SaveChangesAsync();
        var service = new WritingTutorReviewService(
            db,
            clock,
            TestRuntimeSettingsProvider.FromWritingOptions(new WritingV2Options()),
            new WritingModerationService(db, NullLogger<WritingModerationService>.Instance),
            NullLogger<WritingTutorReviewService>.Instance);

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.SubmitMarkingReviewAsync(
            submissionId,
            "tutor-2",
            new WritingTutorReviewSubmitInput(
                "feedback",
                null,
                new WritingCriteriaScores(3, 7, 7, 7, 7, 7),
                null,
                "first",
                false),
            CancellationToken.None));

        Assert.Equal("writing_tutor_assignment_forbidden", ex.ErrorCode);
        Assert.Empty(await db.WritingTutorReviews.AsNoTracking().ToListAsync());
        Assert.Single(await db.WritingGrades.AsNoTracking().Where(g => g.SubmissionId == submissionId).ToListAsync());

        await db.DisposeAsync();
    }

    [Fact]
    public async Task MarkingSurfaceSubmit_ClaimedTutorCreatesLinkedAdjustedGrade()
    {
        var db = BuildDb();
        var clock = new FixedClock();
        var scenarioId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        db.WritingScenarios.Add(Scenario(scenarioId));
        db.WritingSubmissions.Add(Submission(submissionId, UserId, scenarioId, clock.GetUtcNow()));
        var originalGradeId = Guid.NewGuid();
        db.WritingGrades.Add(Grade(originalGradeId, submissionId, 30, clock.GetUtcNow()));
        db.WritingTutorReviewAssignments.Add(new WritingTutorReviewAssignment
        {
            Id = Guid.NewGuid(),
            SubmissionId = submissionId,
            TutorId = "tutor-1",
            ClaimedAt = clock.GetUtcNow(),
            DueAt = clock.GetUtcNow().AddHours(24),
            Status = "claimed",
        });
        await db.SaveChangesAsync();
        var service = new WritingTutorReviewService(
            db,
            clock,
            TestRuntimeSettingsProvider.FromWritingOptions(new WritingV2Options()),
            new WritingModerationService(db, NullLogger<WritingModerationService>.Instance),
            NullLogger<WritingTutorReviewService>.Instance);

        await service.SubmitMarkingReviewAsync(
            submissionId,
            "tutor-1",
            new WritingTutorReviewSubmitInput(
                "feedback",
                null,
                new WritingCriteriaScores(3, 7, 7, 7, 7, 7),
                null,
                "first",
                false),
            CancellationToken.None);

        var review = Assert.Single(await db.WritingTutorReviews.AsNoTracking().ToListAsync());
        Assert.Equal("tutor-1", review.TutorId);
        var assignment = Assert.Single(await db.WritingTutorReviewAssignments.AsNoTracking().ToListAsync());
        Assert.Equal("submitted", assignment.Status);
        Assert.NotNull(assignment.ReleasedAt);
        var adjusted = Assert.Single(await db.WritingGrades.AsNoTracking()
            .Where(g => g.SubmissionId == submissionId)
            .ToListAsync());
        Assert.Equal(originalGradeId, adjusted.Id);
        Assert.Equal(review.Id, adjusted.TutorReviewId);
        Assert.Equal(38, adjusted.RawTotal);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task Showcase_RequiresOptInAndAGradeBeforeModeration()
    {
        var db = BuildDb();
        var clock = new FixedClock();
        var scenarioId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        db.WritingScenarios.Add(Scenario(scenarioId));
        db.WritingSubmissions.Add(Submission(submissionId, UserId, scenarioId, clock.GetUtcNow()));
        db.WritingGrades.Add(Grade(Guid.NewGuid(), submissionId, 32, clock.GetUtcNow()));
        await db.SaveChangesAsync();
        var service = new WritingShowcaseService(db, clock);

        var optIn = await Assert.ThrowsAsync<ApiException>(() => service.SubmitForModerationAsync(UserId, submissionId, CancellationToken.None));
        Assert.Equal("writing_showcase_opt_in_required", optIn.ErrorCode);

        db.LearnerWritingProfiles.Add(new LearnerWritingProfile
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            OptInCommunity = true,
            UpdatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync();

        var gradeGate = await Assert.ThrowsAsync<ApiException>(() => service.SubmitForModerationAsync(UserId, submissionId, CancellationToken.None));
        Assert.Equal("writing_showcase_a_grade_required", gradeGate.ErrorCode);

        db.WritingGrades.Add(Grade(Guid.NewGuid(), submissionId, 38, clock.GetUtcNow().AddMinutes(1)));
        await db.SaveChangesAsync();
        var post = await service.SubmitForModerationAsync(UserId, submissionId, CancellationToken.None);

        Assert.Equal("pending", post.Status);
        Assert.DoesNotContain("Dr Smith", post.AnonymizedLetterContent, StringComparison.Ordinal);

        await db.DisposeAsync();
    }

    private static LearnerDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static WritingScenario Scenario(Guid id)
        => new()
        {
            Id = id,
            Title = "Referral scenario",
            LetterType = "LT-RR",
            Profession = "medicine",
            Difficulty = 3,
            IsDiagnostic = false,
            Status = "published",
            AuthorId = "admin",
            PublishedAt = new DateTimeOffset(2026, 5, 27, 8, 0, 0, TimeSpan.Zero),
            CreatedAt = new DateTimeOffset(2026, 5, 27, 8, 0, 0, TimeSpan.Zero),
        };

    private static WritingSubmission Submission(Guid id, string userId, Guid scenarioId, DateTimeOffset submittedAt)
        => new()
        {
            Id = id,
            UserId = userId,
            ScenarioId = scenarioId,
            Mode = "practice",
            LetterContent = "Dear Dr Smith, please review Ms Jones after her appointment.",
            LetterContentHash = id.ToString("N"),
            WordCount = 180,
            TimeSpentSeconds = 2100,
            StartedAt = submittedAt.AddMinutes(-35),
            SubmittedAt = submittedAt,
            Status = "graded",
            GradingTier = "express",
            InputSource = "typed",
            CreatedAt = submittedAt,
        };

    private static WritingGrade Grade(Guid id, Guid submissionId, short rawTotal, DateTimeOffset gradedAt)
        => new()
        {
            Id = id,
            SubmissionId = submissionId,
            C1Purpose = 3,
            C2Content = 5,
            C3Conciseness = 5,
            C4Genre = 5,
            C5Organisation = 6,
            C6Language = 6,
            RawTotal = rawTotal,
            EstimatedBand = rawTotal,
            BandLabel = rawTotal >= 38 ? "A" : rawTotal >= 30 ? "B" : "C",
            ModelUsed = "test",
            CanonVersion = "v1",
            GradedAt = gradedAt,
            CreatedAt = gradedAt,
        };

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset now = new(2026, 5, 27, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubAiGateway(string completion) : IAiGatewayService
    {
        public AiGroundingContext? LastContext { get; private set; }
        public AiGatewayRequest? LastRequest { get; private set; }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
        {
            LastContext = context;
            return new AiGroundedPrompt
            {
                SystemPrompt = "grounded",
                TaskInstruction = "coach",
                Metadata = new AiGroundedPromptMetadata
                {
                    RulebookKind = context.Kind,
                    RulebookVersion = "test",
                    AppliedRulesCount = 1,
                    AppliedRuleIds = new[] { "R1" },
                },
            };
        }

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(new AiGatewayResult
            {
                Completion = completion,
                Metadata = request.Prompt.Metadata,
                AppliedRuleIds = request.Prompt.Metadata.AppliedRuleIds,
            });
        }
    }

    private sealed class StubOcrService : IOcrService
    {
        public Task<string> OcrToMarkdownAsync(byte[] documentBytes, string mimeType, string featureCode, string? userId, CancellationToken ct)
            => throw new InvalidOperationException("OCR not expected in this test (OcrEnabled=false).");
    }

    private sealed class StubProviderRegistry : IAiProviderRegistry
    {
        public Task<AiProvider?> FindByCodeAsync(string code, CancellationToken ct) => Task.FromResult<AiProvider?>(null);
        public Task<IReadOnlyList<AiProvider>> ListActiveAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AiProvider>>(Array.Empty<AiProvider>());
        public Task<IReadOnlyList<AiProvider>> ListByCategoryAsync(AiProviderCategory category, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AiProvider>>(Array.Empty<AiProvider>());
        public Task<string?> GetPlatformKeyAsync(string providerCode, CancellationToken ct) => Task.FromResult<string?>(null);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class InMemoryFileStorage : IFileStorage
    {
        private readonly Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        public IReadOnlyCollection<string> Keys => files.Keys.ToList();

        public async Task<long> WriteAsync(string key, Stream source, CancellationToken ct)
        {
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, ct);
            files[key] = buffer.ToArray();
            return files[key].LongLength;
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
            => Task.FromResult<Stream>(new MemoryStream(files[key], writable: false));

        public Task<Stream> OpenWriteAsync(string key, CancellationToken ct)
        {
            var stream = new MemoryStream();
            return Task.FromResult<Stream>(new CommitOnDisposeStream(stream, bytes => files[key] = bytes));
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(files.ContainsKey(key));
        }

        public Task<bool> DeleteAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(files.Remove(key));
        }

        public Task<long> LengthAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(files[key].LongLength);
        }

        public Task MoveAsync(string sourceKey, string destKey, bool overwrite, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!files.TryGetValue(sourceKey, out var bytes)) return Task.CompletedTask;
            if (!overwrite && files.ContainsKey(destKey)) return Task.CompletedTask;
            files[destKey] = bytes;
            files.Remove(sourceKey);
            return Task.CompletedTask;
        }

        public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var keys = files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            foreach (var key in keys) files.Remove(key);
            return Task.FromResult(keys.Count);
        }
        public string? TryResolveLocalPath(string key) => null;
        public Uri? ResolveReadUrl(string key, TimeSpan ttl) => new($"storage:///{key}");
    }

    private sealed class CommitOnDisposeStream(MemoryStream inner, Action<byte[]> onDispose) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        protected override void Dispose(bool disposing)
        {
            if (disposing) onDispose(inner.ToArray());
            base.Dispose(disposing);
        }
    }
}
