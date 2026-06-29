using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// Proves the zero-AI invariant for mock Speaking (2026-06-29 owner rule): a
/// <see cref="SpeakingSession"/> that belongs to a curated Mock Set (MockSetId
/// set) is human-marked — <see cref="SpeakingAiAssessmentService.RunAssessmentAsync"/>
/// must short-circuit to the human-examiner projection and NEVER call the AI
/// gateway nor write a <see cref="SpeakingAiAssessment"/> row. This holds even
/// when the session is in <see cref="SpeakingSessionMode.AiExam"/> mode (the
/// legacy AI-mock shape). The gateway is a throwing stub, so any AI-path code
/// fails the test loudly.
///
/// Uses SQLite in-memory (per repo convention).
/// </summary>
public sealed class SpeakingMockNoAiTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;

    public SpeakingMockNoAiTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new LearnerDbContext(options);
        _db.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task RunAssessment_MockSetSession_SkipsAi_ReturnsHumanReview_AndWritesNoAssessment()
    {
        const string sessionId = "sps-mock-1";
        _db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = sessionId,
            UserId = "learner-1",
            RolePlayCardId = "rpc-x",
            ExamSessionId = "spx-1",
            // Genuine mock provenance — even in AiExam mode, this must be human-marked.
            MockSetId = "sms-1",
            Mode = SpeakingSessionMode.AiExam,
            State = SpeakingSessionState.Finished,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var assessor = new SpeakingAiAssessmentService(
            _db, new ThrowingGateway(), NullLogger<SpeakingAiAssessmentService>.Instance);

        var projection = await assessor.RunAssessmentAsync(sessionId, default);

        Assert.Equal("human_examiner", projection.Provider);
        Assert.Equal("awaiting_human_review", projection.ReadinessBand);
        Assert.Empty(projection.CriterionScores);
        Assert.False(await _db.SpeakingAiAssessments.AnyAsync(a => a.SpeakingSessionId == sessionId));
    }

    /// <summary>Any gateway call means the AI path ran — fail loudly.</summary>
    private sealed class ThrowingGateway : IAiGatewayService
    {
        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
            => throw new InvalidOperationException("AI gateway must not be called for mock Speaking.");

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => throw new InvalidOperationException("AI gateway must not be called for mock Speaking.");
    }
}
