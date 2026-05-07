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

/// <summary>
/// Mocks V2 Wave 6 — regression guard for the learner-scoped MockBooking
/// projection. The interlocutor card content (background, cue prompts,
/// patient profile), the assigned tutor / interlocutor identifiers, the Zoom
/// start URL and any tutor-only field MUST never be exposed to the learner
/// via <c>GET /v1/mock-bookings/{id}</c>. The full content is gated behind
/// the admin and expert routes.
/// </summary>
public class MockBookingLearnerDtoTests : IClassFixture<TestWebApplicationFactory>
{
    private const string LearnerId = "mock-booking-dto-learner";
    private const string TutorId = "mock-booking-dto-tutor";
    private const string BackgroundParagraph =
        "Patient has been managing type 2 diabetes for eight years and has missed the last two follow-up appointments.";
    private static readonly string[] CuePrompts =
    {
        "Open by acknowledging the missed appointments without blame.",
        "Probe the patient's understanding of HbA1c trends.",
        "Negotiate a realistic medication-review schedule.",
    };

    private readonly TestWebApplicationFactory _factory;

    public MockBookingLearnerDtoTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task LearnerBookingDto_DoesNotLeakInterlocutorCardOrTutorFields()
    {
        var bookingId = await SeedSpeakingBookingAsync();

        using var learnerClient = await CreateLearnerClientAsync(LearnerId);
        var response = await learnerClient.GetAsync($"/v1/mock-bookings/{bookingId}");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        // Sanity — booking is the right one.
        Assert.Equal(bookingId, root.GetProperty("id").GetString());

        // Mission-critical: tutor-only fields must be absent from learner DTO.
        Assert.False(root.TryGetProperty("interlocutorCard", out _),
            "Learner DTO must not embed the interlocutor card content.");
        Assert.False(root.TryGetProperty("speakingContent", out _),
            "Learner DTO must not embed authored speaking content (which contains the interlocutor card).");
        Assert.False(root.TryGetProperty("speakingPaperId", out _),
            "Learner DTO must not expose the bound speaking paper id.");
        Assert.False(root.TryGetProperty("cuePrompts", out _),
            "Learner DTO must not surface interlocutor cue prompts.");
        Assert.False(root.TryGetProperty("tutorNotes", out _),
            "Learner DTO must not surface tutor-only notes.");
        Assert.False(root.TryGetProperty("assignedTutorId", out _),
            "Learner DTO must not reveal the assigned tutor identity.");
        Assert.False(root.TryGetProperty("assignedInterlocutorId", out _),
            "Learner DTO must not reveal the assigned interlocutor identity.");
        Assert.False(root.TryGetProperty("zoomStartUrl", out _),
            "Learner DTO must not expose the Zoom start URL (host-only).");

        // Belt-and-braces: even raw substring search must miss the interlocutor
        // background paragraph and any cue prompt.
        Assert.DoesNotContain("missed the last two follow-up appointments", body);
        foreach (var prompt in CuePrompts)
        {
            Assert.DoesNotContain(prompt, body);
        }

        // The boolean visibility flag is intentionally exposed and must report
        // the interlocutor card as hidden for learners.
        Assert.True(root.TryGetProperty("interlocutorCardVisible", out var visible));
        Assert.False(visible.GetBoolean());
    }

    [Fact]
    public async Task ExpertBookingDto_ExposesInterlocutorCardForAssignedTutor()
    {
        var bookingId = await SeedSpeakingBookingAsync();

        using var expertClient = CreateExpertClient(TutorId);
        var response = await expertClient.GetAsync($"/v1/expert/mocks/bookings/{bookingId}");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        Assert.Equal(bookingId, root.GetProperty("id").GetString());
        Assert.Equal(TutorId, root.GetProperty("assignedTutorId").GetString());
        Assert.True(root.TryGetProperty("speakingContent", out var speaking));
        Assert.True(speaking.TryGetProperty("interlocutorCard", out var interlocutor));
        Assert.True(interlocutor.TryGetProperty("background", out var background));
        Assert.Contains("missed the last two follow-up appointments", background.GetString() ?? string.Empty);
        Assert.True(interlocutor.TryGetProperty("cuePrompts", out var cuePrompts));
        Assert.Equal(JsonValueKind.Array, cuePrompts.ValueKind);
        Assert.Equal(CuePrompts.Length, cuePrompts.GetArrayLength());
    }

    [Fact]
    public async Task ExpertBookingDto_RefusesUnassignedExpert()
    {
        var bookingId = await SeedSpeakingBookingAsync();

        using var expertClient = CreateExpertClient("mock-booking-dto-other-tutor");
        var response = await expertClient.GetAsync($"/v1/expert/mocks/bookings/{bookingId}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<string> SeedSpeakingBookingAsync()
    {
        await _factory.EnsureLearnerProfileAsync(LearnerId, $"{LearnerId}@example.test", LearnerId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        var paperId = "mock-booking-dto-speaking-paper";
        var bundleId = "mock-booking-dto-bundle";
        var sectionId = $"{bundleId}-section-speaking";
        var bookingId = "mock-booking-dto-booking";

        // Speaking paper with structured interlocutor card (background +
        // cuePrompts) — what the learner must NOT see.
        var speakingStructure = new Dictionary<string, object?>
        {
            ["candidateCard"] = new Dictionary<string, object?>
            {
                ["candidateRole"] = "Practice nurse",
                ["setting"] = "Community clinic",
                ["patientRole"] = "Patient",
                ["task"] = "Discuss missed appointments and agree a follow-up plan.",
                ["background"] = "You are a practice nurse.",
                ["tasks"] = new[] { "Acknowledge missed visits", "Agree on next steps" },
            },
            ["interlocutorCard"] = new Dictionary<string, object?>
            {
                ["background"] = BackgroundParagraph,
                ["patientProfile"] = "Frustrated, slightly defensive after the missed visits.",
                ["cuePrompts"] = CuePrompts,
            },
            ["warmUpQuestions"] = new[] { "How long have you been with this clinic?" },
            ["prepTimeSeconds"] = 180,
            ["roleplayTimeSeconds"] = 300,
            ["patientEmotion"] = "anxious",
            ["communicationGoal"] = "Re-engage the patient with their care plan.",
            ["clinicalTopic"] = "diabetes_followup",
            ["criteriaFocus"] = new[] { "relationship_building", "information_gathering" },
        };
        var extractedJson = JsonSupport.Serialize(new Dictionary<string, object?>
        {
            ["speakingStructure"] = speakingStructure,
        });

        var existingPaper = await db.ContentPapers.FirstOrDefaultAsync(p => p.Id == paperId);
        if (existingPaper is null)
        {
            db.ContentPapers.Add(new ContentPaper
            {
                Id = paperId,
                SubtestCode = "speaking",
                Title = "Speaking role-play paper",
                Slug = paperId,
                AppliesToAllProfessions = true,
                ProfessionId = "medicine",
                Difficulty = "standard",
                EstimatedDurationMinutes = 20,
                Status = ContentStatus.Published,
                SourceProvenance = "Admin-authored test paper.",
                ExtractedTextJson = extractedJson,
                CreatedAt = now,
                UpdatedAt = now,
                PublishedAt = now,
            });
        }
        else
        {
            existingPaper.Status = ContentStatus.Published;
            existingPaper.ExtractedTextJson = extractedJson;
            existingPaper.UpdatedAt = now;
        }

        var existingBundle = await db.MockBundles.Include(x => x.Sections)
            .FirstOrDefaultAsync(x => x.Id == bundleId);
        if (existingBundle is null)
        {
            db.MockBundles.Add(new MockBundle
            {
                Id = bundleId,
                Title = "Mock booking DTO speaking bundle",
                Slug = bundleId,
                ExamFamilyCode = "oet",
                ExamTypeCode = "oet",
                MockType = MockTypes.FinalReadiness,
                AppliesToAllProfessions = true,
                Status = ContentStatus.Published,
                EstimatedDurationMinutes = 20,
                ReleasePolicy = MockReleasePolicies.AfterTeacherMarking,
                SourceStatus = MockSourceStatuses.Original,
                QualityStatus = MockQualityStatuses.Approved,
                SourceProvenance = "Admin-authored test bundle.",
                CreatedAt = now,
                UpdatedAt = now,
                PublishedAt = now,
                Sections = new List<MockBundleSection>
                {
                    new()
                    {
                        Id = sectionId,
                        MockBundleId = bundleId,
                        SectionOrder = 1,
                        SubtestCode = "speaking",
                        ContentPaperId = paperId,
                        TimeLimitMinutes = 20,
                        ReviewEligible = true,
                        IsRequired = true,
                        CreatedAt = now,
                    },
                },
            });
        }

        var existingBooking = await db.MockBookings.FirstOrDefaultAsync(x => x.Id == bookingId);
        if (existingBooking is null)
        {
            db.MockBookings.Add(new MockBooking
            {
                Id = bookingId,
                UserId = LearnerId,
                MockBundleId = bundleId,
                ScheduledStartAt = now.AddDays(2),
                TimezoneIana = "Asia/Karachi",
                Status = MockBookingStatuses.Scheduled,
                DeliveryMode = MockDeliveryModes.OetHome,
                ConsentToRecording = true,
                LearnerNotes = "Final readiness check.",
                LiveRoomState = MockLiveRoomStates.Waiting,
                AssignedTutorId = TutorId,
                AssignedInterlocutorId = TutorId,
                ZoomStartUrl = "https://zoom.example.test/start/secret",
                ZoomJoinUrl = "https://zoom.example.test/join/public",
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else
        {
            existingBooking.UserId = LearnerId;
            existingBooking.MockBundleId = bundleId;
            existingBooking.AssignedTutorId = TutorId;
            existingBooking.AssignedInterlocutorId = TutorId;
            existingBooking.ZoomStartUrl = "https://zoom.example.test/start/secret";
            existingBooking.ZoomJoinUrl = "https://zoom.example.test/join/public";
            existingBooking.UpdatedAt = now;
        }

        await db.SaveChangesAsync();
        return bookingId;
    }

    private async Task<HttpClient> CreateLearnerClientAsync(string userId)
    {
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }

    private HttpClient CreateExpertClient(string expertId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", expertId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{expertId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", expertId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "expert");
        return client;
    }
}
