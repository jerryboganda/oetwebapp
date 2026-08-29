using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

/// <summary>
/// W8: mock Speaking is AI-graded. A MockSetId session must not short-circuit
/// to the human-examiner projection.
/// </summary>
public sealed class SpeakingMockAiGradedTests : IAsyncLifetime
{
    private LearnerDbContext _db = default!;

    public Task InitializeAsync()
    {
        _db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-mock-ai-{Guid.NewGuid():N}")
            .Options);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RunAssessment_MockSetSession_DoesNotReturnHumanExaminer()
    {
        const string sessionId = "sps-mock-ai-1";
        _db.SpeakingSessions.Add(new SpeakingSession
        {
            Id = sessionId,
            UserId = "learner-1",
            RolePlayCardId = "rpc-missing",
            ExamSessionId = "spx-1",
            MockSetId = "sms-1",
            Mode = SpeakingSessionMode.AiExam,
            State = SpeakingSessionState.Finished,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var assessor = new SpeakingAiAssessmentService(
            _db, new ThrowingGateway(), NullLogger<SpeakingAiAssessmentService>.Instance);

        var ex = await Assert.ThrowsAsync<ApiException>(() => assessor.RunAssessmentAsync(sessionId, default));
        Assert.Equal("role_play_card_not_found", ex.ErrorCode);
    }

    private sealed class ThrowingGateway : IAiGatewayService
    {
        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
            => throw new InvalidOperationException("AI gateway must not be reached before the card load.");

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => throw new InvalidOperationException("unexpected");
    }
}
