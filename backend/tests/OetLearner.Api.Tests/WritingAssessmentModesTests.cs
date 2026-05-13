using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
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
}
