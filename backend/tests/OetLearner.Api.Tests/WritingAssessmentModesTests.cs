using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

[Collection("AuthFlows")]
public class WritingAssessmentModesTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public WritingAssessmentModesTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ExpertCanAttachVoiceNote_AndLearnerCanReadCompletedResult()
    {
        await SeedWritingReviewAsync("voice-flow", includePaperAsset: false, includeVoiceNote: false);
        using var expert = _factory.CreateAuthenticatedClient(SeedData.ExpertEmail, SeedData.LocalSeedPassword, expectedRole: "expert");
        using var learner = _factory.CreateAuthenticatedClient(SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");

        var response = await expert.PostAsJsonAsync("/v1/expert/reviews/review-voice-flow/writing/voice-notes", new
        {
            mediaAssetId = "media-voice-flow-audio",
            durationSeconds = 92,
            transcriptText = "Purpose and content need tighter selection.",
            writtenNotes = "Voice note summary for the learner.",
            rubricScores = new Dictionary<string, int>
            {
                ["purpose"] = 2,
                ["content"] = 5,
                ["conciseness"] = 5,
                ["genre"] = 5,
                ["organization"] = 5,
                ["language"] = 5
            }
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();

        using (var created = JsonDocument.Parse(payload))
        {
            var item = created.RootElement.GetProperty("item");
            Assert.Equal("review-voice-flow", item.GetProperty("reviewRequestId").GetString());
            Assert.Equal("media-voice-flow-audio", item.GetProperty("mediaAssetId").GetString());
            Assert.Contains("Voice note summary", item.GetProperty("writtenNotes").GetString());
        }

        var draftVoiceNotes = await learner.GetAsync("/v1/reviews/requests/review-voice-flow/voice-notes");
        draftVoiceNotes.EnsureSuccessStatusCode();
        using (var draftPayload = JsonDocument.Parse(await draftVoiceNotes.Content.ReadAsStringAsync()))
        {
            Assert.Empty(draftPayload.RootElement.GetProperty("items").EnumerateArray());
        }

        var draftResult = await learner.GetAsync("/v1/reviews/requests/review-voice-flow/result");
        Assert.Equal(HttpStatusCode.NotFound, draftResult.StatusCode);

        var blockedMedia = await learner.GetAsync("/v1/media/media-voice-flow-audio/content");
        Assert.Equal(HttpStatusCode.NotFound, blockedMedia.StatusCode);

        var submitResponse = await expert.PostAsJsonAsync("/v1/expert/reviews/review-voice-flow/writing/submit", new
        {
            scores = new Dictionary<string, int>
            {
                ["purpose"] = 2,
                ["content"] = 5,
                ["conciseness"] = 5,
                ["genre"] = 5,
                ["organization"] = 5,
                ["language"] = 5
            },
            criterionComments = new Dictionary<string, string>
            {
                ["purpose"] = "Purpose is mostly clear, but the opening request needs sharper prioritisation.",
                ["content"] = "Relevant content is selected and sequenced for the referral."
            },
            finalComment = "Completed Dr. Ahmed review with a returned voice note and structured rubric.",
            version = (int?)null
        });
        submitResponse.EnsureSuccessStatusCode();

        var learnerResponse = await learner.GetAsync("/v1/reviews/requests/review-voice-flow/voice-notes");
        var learnerPayload = await learnerResponse.Content.ReadAsStringAsync();
        learnerResponse.EnsureSuccessStatusCode();

        using (var readable = JsonDocument.Parse(learnerPayload))
        {
            var items = readable.RootElement.GetProperty("items");
            Assert.Single(items.EnumerateArray());
            Assert.Equal("media-voice-flow-audio", items[0].GetProperty("mediaAssetId").GetString());
        }

        var readableMedia = await learner.GetAsync("/v1/media/media-voice-flow-audio/content");
        readableMedia.EnsureSuccessStatusCode();

        var resultResponse = await learner.GetAsync("/v1/reviews/requests/review-voice-flow/result");
        var resultPayload = await resultResponse.Content.ReadAsStringAsync();
        resultResponse.EnsureSuccessStatusCode();

        using var result = JsonDocument.Parse(resultPayload);
        Assert.Equal("Completed Dr. Ahmed review with a returned voice note and structured rubric.", result.RootElement.GetProperty("finalComment").GetString());
        Assert.Equal("completed", result.RootElement.GetProperty("state").GetString());
        Assert.Equal("Dr. Ahmed rubric 27/38", result.RootElement.GetProperty("scoreLabel").GetString());
        Assert.Equal(6, result.RootElement.GetProperty("criteria").GetArrayLength());
    }

    [Fact]
    public async Task AttachWritingVoiceNote_WithUnreadyMedia_IsRejected()
    {
        await SeedWritingReviewAsync("voice-not-ready", includePaperAsset: false, includeVoiceNote: false);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var media = await db.MediaAssets.SingleAsync(asset => asset.Id == "media-voice-not-ready-audio");
            media.Status = MediaAssetStatus.Processing;
            await db.SaveChangesAsync();
        }

        using var expert = _factory.CreateAuthenticatedClient(SeedData.ExpertEmail, SeedData.LocalSeedPassword, expectedRole: "expert");

        var response = await expert.PostAsJsonAsync("/v1/expert/reviews/review-voice-not-ready/writing/voice-notes", new
        {
            mediaAssetId = "media-voice-not-ready-audio",
            durationSeconds = 30,
            transcriptText = "Processing media should not attach yet.",
            writtenNotes = "Wait for media processing.",
            rubricScores = new Dictionary<string, int> { ["purpose"] = 2 }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("voice_note_media_not_ready", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SubmitWritingReview_WithMissingVoiceNoteFile_IsRejected()
    {
        await SeedWritingReviewAsync("missing-voice-file", includePaperAsset: false, includeVoiceNote: true);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            Assert.True(storage.Delete("test/voice-missing-voice-file.webm"));
        }

        using var expert = _factory.CreateAuthenticatedClient(SeedData.ExpertEmail, SeedData.LocalSeedPassword, expectedRole: "expert");

        var response = await expert.PostAsJsonAsync("/v1/expert/reviews/review-missing-voice-file/writing/submit", new
        {
            scores = new Dictionary<string, int>
            {
                ["purpose"] = 2,
                ["content"] = 5,
                ["conciseness"] = 5,
                ["genre"] = 5,
                ["organization"] = 5,
                ["language"] = 5
            },
            criterionComments = new Dictionary<string, string>(),
            finalComment = "This review has a stale voice note row but no streamable media.",
            version = (int?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("voice_note_required", await response.Content.ReadAsStringAsync());

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var review = await db.ReviewRequests.SingleAsync(item => item.Id == "review-missing-voice-file");
        Assert.NotEqual(ReviewRequestState.Completed, review.State);
    }

    [Fact]
    public async Task AttachWritingPaperAsset_WithUnsupportedImageType_IsRejected()
    {
        await SeedWritingPaperAttemptAsync("webp-reject", paperMimeType: "image/webp", extractionState: null, extractedText: null);
        using var learner = _factory.CreateAuthenticatedClient(SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");

        var response = await learner.PostAsJsonAsync("/v1/writing/attempts/attempt-webp-reject/paper-assets", new
        {
            mediaAssetIds = new[] { "media-webp-reject-paper" },
            replaceExisting = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("invalid_paper_asset_type", payload);
        Assert.Contains("JPG, PNG, or PDF", payload);
    }

    [Fact]
    public async Task SubmitPaperWritingAttempt_WithIncompleteOcr_IsRejected()
    {
        await SeedWritingPaperAttemptAsync("ocr-incomplete", paperMimeType: "image/png", extractionState: "processing", extractedText: "");
        using var learner = _factory.CreateAuthenticatedClient(SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");

        var response = await learner.PostAsJsonAsync("/v1/writing/attempts/attempt-ocr-incomplete/submit", new
        {
            content = (string?)null,
            idempotencyKey = (string?)null,
            examMode = "paper",
            assessorType = "ai",
            paperAssetIds = Array.Empty<string>(),
            turnaroundOption = (string?)null,
            focusAreas = Array.Empty<string>(),
            learnerNotes = (string?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("paper_ocr_incomplete", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SubmitPaperWritingAttempt_WithEmptyOcr_IsRejected()
    {
        await SeedWritingPaperAttemptAsync("ocr-empty", paperMimeType: "application/pdf", extractionState: "completed", extractedText: "   ");
        using var learner = _factory.CreateAuthenticatedClient(SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");

        var response = await learner.PostAsJsonAsync("/v1/writing/attempts/attempt-ocr-empty/submit", new
        {
            content = (string?)null,
            idempotencyKey = (string?)null,
            examMode = "paper",
            assessorType = "instructor",
            paperAssetIds = Array.Empty<string>(),
            turnaroundOption = "standard",
            focusAreas = Array.Empty<string>(),
            learnerNotes = "Please review this handwritten attempt."
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("paper_ocr_required", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task WritingReviewBundle_IncludesPaperAssetsAndVoiceNotes()
    {
        await SeedWritingReviewAsync("paper-flow", includePaperAsset: true, includeVoiceNote: true);
        using var expert = _factory.CreateAuthenticatedClient(SeedData.ExpertEmail, SeedData.LocalSeedPassword, expectedRole: "expert");

        var response = await expert.GetAsync("/v1/expert/reviews/review-paper-flow/writing");
        var payload = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(payload);
        Assert.Single(json.RootElement.GetProperty("paperAssets").EnumerateArray());
        Assert.Single(json.RootElement.GetProperty("voiceNotes").EnumerateArray());
        Assert.Equal("completed", json.RootElement.GetProperty("artifactStatus").GetProperty("paperAssets").GetProperty("state").GetString());
        Assert.Equal("completed", json.RootElement.GetProperty("artifactStatus").GetProperty("voiceNotes").GetProperty("state").GetString());
    }

    [Fact]
    public async Task SubmitWritingReview_WithoutVoiceNote_IsRejected()
    {
        await SeedWritingReviewAsync("no-voice-submit", includePaperAsset: false, includeVoiceNote: false);
        using var expert = _factory.CreateAuthenticatedClient(SeedData.ExpertEmail, SeedData.LocalSeedPassword, expectedRole: "expert");

        var response = await expert.PostAsJsonAsync("/v1/expert/reviews/review-no-voice-submit/writing/submit", new
        {
            scores = new Dictionary<string, int>
            {
                ["purpose"] = 2,
                ["content"] = 5,
                ["conciseness"] = 5,
                ["genre"] = 5,
                ["organization"] = 5,
                ["language"] = 5
            },
            criterionComments = new Dictionary<string, string>(),
            finalComment = "This review has rubric feedback but no voice note yet.",
            version = (int?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("voice_note_required", payload);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var review = await db.ReviewRequests.SingleAsync(item => item.Id == "review-no-voice-submit");
        Assert.NotEqual(ReviewRequestState.Completed, review.State);
    }

    private async Task SeedWritingReviewAsync(string suffix, bool includePaperAsset, bool includeVoiceNote)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var attemptId = $"attempt-{suffix}";
        var reviewId = $"review-{suffix}";
        var now = DateTimeOffset.UtcNow;

        if (await db.ReviewRequests.AnyAsync(review => review.Id == reviewId))
        {
            return;
        }

        db.Attempts.Add(new Attempt
        {
            Id = attemptId,
            UserId = "mock-user-001",
            ContentId = "wt-001",
            SubtestCode = "writing",
            Context = "practice",
            Mode = "exam",
            State = AttemptState.Completed,
            StartedAt = now.AddHours(-2),
            SubmittedAt = now.AddHours(-1),
            CompletedAt = now.AddMinutes(-45),
            ElapsedSeconds = 2400,
            DraftContent = "Dear Doctor,\n\nPlease review this paper-mode writing submission for targeted feedback.\n\nYours sincerely,\nCandidate",
            AnalysisJson = JsonSerializer.Serialize(new
            {
                writingSubmission = new
                {
                    examMode = includePaperAsset ? "paper" : "computer",
                    assessorType = "instructor",
                    paperAssetIds = includePaperAsset ? new[] { $"media-{suffix}-paper" } : Array.Empty<string>(),
                    extractedCharCount = 112
                }
            })
        });
        db.ReviewRequests.Add(new ReviewRequest
        {
            Id = reviewId,
            AttemptId = attemptId,
            SubtestCode = "writing",
            State = ReviewRequestState.InReview,
            TurnaroundOption = "standard",
            FocusAreasJson = "[]",
            LearnerNotes = "Assess the uploaded response.",
            PaymentSource = "credits",
            PriceSnapshot = 1m,
            CreatedAt = now.AddMinutes(-50),
        });
        db.ExpertReviewAssignments.Add(new ExpertReviewAssignment
        {
            Id = $"assignment-{suffix}",
            ReviewRequestId = reviewId,
            AssignedReviewerId = "expert-001",
            AssignedAt = now.AddMinutes(-48),
            ClaimState = ExpertAssignmentState.Claimed,
        });
        db.MediaAssets.Add(new MediaAsset
        {
            Id = $"media-{suffix}-audio",
            OriginalFilename = $"voice-{suffix}.webm",
            MimeType = "audio/webm",
            Format = "webm",
            SizeBytes = 4096,
            DurationSeconds = 92,
            StoragePath = $"test/voice-{suffix}.webm",
            Status = MediaAssetStatus.Ready,
            MediaKind = "audio",
            UploadedBy = "expert-001",
            UploadedAt = now.AddMinutes(-40),
            ProcessedAt = now.AddMinutes(-39),
        });
        await storage.WriteAsync($"test/voice-{suffix}.webm", new MemoryStream([1, 2, 3, 4, 5]), CancellationToken.None);

        if (includePaperAsset)
        {
            db.MediaAssets.Add(new MediaAsset
            {
                Id = $"media-{suffix}-paper",
                OriginalFilename = $"paper-{suffix}.pdf",
                MimeType = "application/pdf",
                Format = "pdf",
                SizeBytes = 8192,
                StoragePath = $"test/paper-{suffix}.pdf",
                Status = MediaAssetStatus.Ready,
                MediaKind = "document",
                UploadedBy = "mock-user-001",
                UploadedAt = now.AddMinutes(-55),
                ProcessedAt = now.AddMinutes(-54),
            });
            db.WritingAttemptAssets.Add(new WritingAttemptAsset
            {
                Id = $"waa-{suffix}",
                AttemptId = attemptId,
                UserId = "mock-user-001",
                MediaAssetId = $"media-{suffix}-paper",
                AssetKind = "document",
                PageNumber = 1,
                ExtractionState = "completed",
                ExtractedText = "Extracted paper writing text with enough content to review.",
                ExtractionProvider = "test",
                CreatedAt = now.AddMinutes(-54),
                UpdatedAt = now.AddMinutes(-53),
                ExtractedAt = now.AddMinutes(-53),
            });
            await storage.WriteAsync($"test/paper-{suffix}.pdf", new MemoryStream([37, 80, 68, 70, 45]), CancellationToken.None);
        }

        if (includeVoiceNote)
        {
            db.ReviewVoiceNotes.Add(new ReviewVoiceNote
            {
                Id = $"rvn-{suffix}",
                ReviewRequestId = reviewId,
                UploadedByReviewerId = "expert-001",
                MediaAssetId = $"media-{suffix}-audio",
                DurationSeconds = 92,
                TranscriptText = "Recorded feedback transcript.",
                WrittenNotes = "Recorded feedback summary.",
                RubricJson = JsonSerializer.Serialize(new Dictionary<string, int> { ["purpose"] = 2 }),
                Status = "ready",
                CreatedAt = now.AddMinutes(-20),
                UpdatedAt = now.AddMinutes(-20),
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task SeedWritingPaperAttemptAsync(string suffix, string paperMimeType, string? extractionState, string? extractedText)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var attemptId = $"attempt-{suffix}";
        var mediaId = $"media-{suffix}-paper";
        var now = DateTimeOffset.UtcNow;

        if (await db.Attempts.AnyAsync(attempt => attempt.Id == attemptId))
        {
            return;
        }

        var format = paperMimeType switch
        {
            "application/pdf" => "pdf",
            "image/png" => "png",
            "image/jpeg" => "jpg",
            "image/webp" => "webp",
            _ => "bin"
        };

        db.Attempts.Add(new Attempt
        {
            Id = attemptId,
            UserId = "mock-user-001",
            ContentId = "wt-001",
            SubtestCode = "writing",
            Context = "practice",
            Mode = "exam",
            State = AttemptState.InProgress,
            StartedAt = now.AddMinutes(-10),
            DraftContent = string.Empty,
        });
        db.MediaAssets.Add(new MediaAsset
        {
            Id = mediaId,
            OriginalFilename = $"paper-{suffix}.{format}",
            MimeType = paperMimeType,
            Format = format,
            SizeBytes = 2048,
            StoragePath = $"test/paper-{suffix}.{format}",
            Status = MediaAssetStatus.Ready,
            MediaKind = paperMimeType == "application/pdf" ? "document" : "image",
            UploadedBy = "mock-user-001",
            UploadedAt = now.AddMinutes(-8),
            ProcessedAt = now.AddMinutes(-7),
        });

        if (extractionState is not null)
        {
            db.WritingAttemptAssets.Add(new WritingAttemptAsset
            {
                Id = $"waa-{suffix}",
                AttemptId = attemptId,
                UserId = "mock-user-001",
                MediaAssetId = mediaId,
                AssetKind = paperMimeType == "application/pdf" ? "document" : "image",
                PageNumber = 1,
                ExtractionState = extractionState,
                ExtractedText = extractedText ?? string.Empty,
                ExtractionProvider = "test",
                CreatedAt = now.AddMinutes(-7),
                UpdatedAt = now.AddMinutes(-6),
                ExtractedAt = string.Equals(extractionState, "completed", StringComparison.OrdinalIgnoreCase) ? now.AddMinutes(-6) : null,
            });
        }

        await db.SaveChangesAsync();
    }
}
