using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Tests.Speaking;

public sealed class SpeakingSimulationV11EvidenceCaptureServiceTests
{
    [Fact]
    public void Malformed_source_transcript_produces_no_fabricated_turns()
    {
        var turns = SpeakingSimulationV11EvidenceCaptureService.ParseSourceTurns(
            "{ \"not\": \"a transcript\" }");

        Assert.Empty(turns);
    }

    [Fact]
    public void Source_turn_parser_preserves_word_confidence_and_interruption()
    {
        var turns = SpeakingSimulationV11EvidenceCaptureService.ParseSourceTurns(
            """[
                {
                  "speaker": "candidate",
                  "startMs": 10,
                  "endMs": 1200,
                  "text": "I am concerned.",
                  "confidence": 0.8,
                  "interrupted": true,
                  "words": [{"word":"concerned","confidence":0.74}]
                }
            ]""");

        var turn = Assert.Single(turns);
        Assert.Equal("candidate", turn.Speaker);
        Assert.True(turn.Interrupted);
        Assert.Contains("confidence", turn.WordConfidenceJson);
        Assert.Equal(10, turn.StartMs);
        Assert.Equal(1200, turn.EndMs);
    }

    [Fact]
    public async Task Capture_requires_a_source_snapshot_and_flags_missing_original_audio()
    {
        await using var db = CreateDb();
        var card = CreateCard();
        var session = CreateSession(card.Id);
        db.RolePlayCards.Add(card);
        db.SpeakingSessions.Add(session);
        db.SpeakingSimulationV11PersonaRuntimeSnapshots.Add(new SpeakingSimulationV11PersonaRuntimeSnapshot
        {
            Id = "persona-1",
            SpeakingSessionId = session.Id,
            RolePlayCardId = card.Id,
            CardSlot = "standalone",
            MemoryScopeKey = "session-1",
            SpecVersion = "speaking-simulation-v1.1",
            CardVersion = "card-version",
            PersonaJson = "{}",
        });
        await db.SaveChangesAsync();

        var service = new SpeakingSimulationV11EvidenceCaptureService(
            db,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SpeakingSimulationV11EvidenceCaptureService>.Instance);

        var result = await service.CaptureAsync(session.Id, CancellationToken.None);

        Assert.False(result.SourceTranscriptAvailable);
        Assert.Equal(SpeakingSimulationV11AudioQualityStatus.NeedsReview, result.AudioQualityStatus);
        Assert.Equal("original_audio_missing", result.AudioQualityIssueCode);
        Assert.Empty(await db.SpeakingSimulationV11TurnEvidenceRows.ToListAsync());
        Assert.Single(await db.SpeakingSimulationV11AudioQualityChecks.ToListAsync());
    }

    [Fact]
    public async Task Capture_records_overlap_fillers_jargon_and_source_timestamps()
    {
        await using var db = CreateDb();
        var card = CreateCard();
        var session = CreateSession(card.Id);
        db.RolePlayCards.Add(card);
        db.SpeakingSessions.Add(session);
        db.InterlocutorScripts.Add(new InterlocutorScript
        {
            Id = "script-1",
            RolePlayCardId = card.Id,
            OpeningResponse = "I am worried.",
            LayLanguageTriggersJson = "[\"hypertension\"]",
        });
        db.SpeakingSimulationV11PersonaRuntimeSnapshots.Add(new SpeakingSimulationV11PersonaRuntimeSnapshot
        {
            Id = "persona-1",
            SpeakingSessionId = session.Id,
            RolePlayCardId = card.Id,
            CardSlot = "standalone",
            MemoryScopeKey = "session-1",
            SpecVersion = "speaking-simulation-v1.1",
            CardVersion = "card-version",
            PersonaJson = "{}",
        });
        db.SpeakingTranscripts.Add(new SpeakingTranscript
        {
            Id = "transcript-1",
            SpeakingSessionId = session.Id,
            Provider = "source-asr",
            SegmentsJson = """[
                {"speaker":"candidate","startMs":0,"endMs":5000,"text":"Um hypertension","interrupted":true},
                {"speaker":"patient","startMs":4000,"endMs":6000,"text":"I am worried."}
            ]""",
            IsLatest = true,
            GeneratedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var service = new SpeakingSimulationV11EvidenceCaptureService(
            db,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SpeakingSimulationV11EvidenceCaptureService>.Instance);

        var result = await service.CaptureAsync(session.Id, CancellationToken.None);
        var rows = await db.SpeakingSimulationV11TurnEvidenceRows
            .OrderBy(x => x.TurnNumber)
            .ToListAsync();

        Assert.Equal(2, result.TurnCount);
        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0].FillerCount);
        Assert.Equal(1, rows[0].JargonCount);
        Assert.True(rows[0].IsInterrupted);
        Assert.True(rows[1].IsOverlap);
        Assert.Equal(4000, rows[1].StartMs);
        Assert.Equal("source-asr", rows[0].AsrProvider);
    }

    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"speaking-evidence-v11-{Guid.NewGuid():N}")
            .Options;
        return new LearnerDbContext(options);
    }

    private static RolePlayCard CreateCard() => new()
    {
        Id = "card-1",
        ContentItemId = "content-1",
        ProfessionId = "medicine",
        ScenarioTitle = "Scenario",
        Setting = "Clinic",
        CandidateRole = "Doctor",
        InterlocutorRole = "Patient",
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static SpeakingSession CreateSession(string cardId) => new()
    {
        Id = "session-1",
        UserId = "user-1",
        RolePlayCardId = cardId,
        Mode = SpeakingSessionMode.AiExam,
        State = SpeakingSessionState.Finished,
        PrepStartedAt = DateTimeOffset.UtcNow.AddMinutes(-8),
        RolePlayStartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        EndedAt = DateTimeOffset.UtcNow,
        ElapsedSeconds = 300,
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-8),
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
