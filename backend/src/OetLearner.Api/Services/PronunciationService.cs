using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Pronunciation analysis and drill management service.
/// Mock implementation returns simulated assessment data.
/// Production: swap for Azure Speech SDK pronunciation assessment API.
/// </summary>
public class PronunciationService(LearnerDbContext db)
{
    public async Task<object> GetProfileAsync(string userId, CancellationToken ct)
    {
        var progress = await db.LearnerPronunciationProgress
            .Where(p => p.UserId == userId)
            .ToListAsync(ct);

        var assessments = await db.PronunciationAssessments
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(10)
            .ToListAsync(ct);

        var overallScore = assessments.Count > 0
            ? Math.Round(assessments.Average(a => a.OverallScore), 1)
            : 0.0;

        var weakPhonemes = progress
            .Where(p => p.AverageScore < 60)
            .OrderBy(p => p.AverageScore)
            .Take(5)
            .Select(p => new
            {
                phonemeCode = p.PhonemeCode,
                averageScore = Math.Round(p.AverageScore, 1),
                attemptCount = p.AttemptCount,
                lastPracticedAt = p.LastPracticedAt
            })
            .ToList();

        return new
        {
            overallScore,
            totalAssessments = assessments.Count,
            weakPhonemes,
            progressOverTime = assessments.Select(a => new
            {
                date = a.CreatedAt,
                overall = Math.Round(a.OverallScore, 1),
                accuracy = Math.Round(a.AccuracyScore, 1),
                fluency = Math.Round(a.FluencyScore, 1)
            }).ToList(),
            phonemeProgress = progress.Select(p => new
            {
                phonemeCode = p.PhonemeCode,
                averageScore = Math.Round(p.AverageScore, 1),
                attemptCount = p.AttemptCount,
                lastPracticedAt = p.LastPracticedAt
            }).ToList()
        };
    }

    public async Task<object> GetDrillsAsync(string? examTypeCode, CancellationToken ct)
    {
        var drills = await db.PronunciationDrills
            .Where(d => d.Status == "active")
            .OrderBy(d => d.Difficulty == "easy" ? 0 : d.Difficulty == "medium" ? 1 : 2)
            .ThenBy(d => d.Label)
            .ToListAsync(ct);

        return drills.Select(d => new
        {
            id = d.Id,
            examTypeCode = examTypeCode ?? "oet",
            phoneme = d.TargetPhoneme,
            title = d.Label,
            description = d.TipsHtml,
            difficultyLevel = d.Difficulty,
            audioExampleUrl = d.AudioModelUrl,
            exampleWordsJson = d.ExampleWordsJson,
            minimalPairsJson = d.MinimalPairsJson,
            sentencesJson = d.SentencesJson
        }).ToList();
    }

    public async Task<object> GetDrillAsync(string drillId, CancellationToken ct)
    {
        var drill = await db.PronunciationDrills.FindAsync([drillId], ct)
            ?? throw ApiException.NotFound("DRILL_NOT_FOUND", "Pronunciation drill not found.");

        return new
        {
            id = drill.Id,
            phoneme = drill.TargetPhoneme,
            title = drill.Label,
            description = drill.TipsHtml,
            difficultyLevel = drill.Difficulty,
            audioExampleUrl = drill.AudioModelUrl,
            exampleWordsJson = drill.ExampleWordsJson,
            minimalPairsJson = drill.MinimalPairsJson,
            sentencesJson = drill.SentencesJson
        };
    }

    public async Task<object> SubmitDrillAttemptAsync(string userId, string drillId, string? audioUrl, CancellationToken ct)
    {
        var drill = await db.PronunciationDrills.FindAsync([drillId], ct)
            ?? throw ApiException.NotFound("DRILL_NOT_FOUND", "Pronunciation drill not found.");

        var assessmentId = $"pa-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;

        // Create assessment record with simulated scores
        var accuracy = 65 + Random.Shared.NextDouble() * 30;
        var fluency = 60 + Random.Shared.NextDouble() * 35;
        var completeness = 70 + Random.Shared.NextDouble() * 28;
        var prosody = 55 + Random.Shared.NextDouble() * 40;
        var overall = (accuracy + fluency + completeness + prosody) / 4.0;

        var assessment = new PronunciationAssessment
        {
            Id = assessmentId,
            UserId = userId,
            AccuracyScore = Math.Round(accuracy, 1),
            FluencyScore = Math.Round(fluency, 1),
            CompletenessScore = Math.Round(completeness, 1),
            ProsodyScore = Math.Round(prosody, 1),
            OverallScore = Math.Round(overall, 1),
            WordScoresJson = JsonSupport.Serialize(new[]
            {
                new { word = "patient", accuracyScore = 92.0, errorType = "None" },
                new { word = "assessment", accuracyScore = 78.5, errorType = "Mispronunciation" },
                new { word = "rehabilitation", accuracyScore = 65.0, errorType = "Mispronunciation" }
            }),
            ProblematicPhonemesJson = JsonSupport.Serialize(new[]
            {
                new { phoneme = drill.TargetPhoneme, score = Math.Round(accuracy * 0.85, 1), occurrences = 3 }
            }),
            FluencyMarkersJson = JsonSupport.Serialize(new
            {
                speechRate = 120 + Random.Shared.Next(0, 40),
                pauseCount = Random.Shared.Next(1, 5),
                averagePauseDurationMs = 300 + Random.Shared.Next(0, 400)
            }),
            CreatedAt = now
        };
        db.PronunciationAssessments.Add(assessment);

        // Update phoneme progress
        var progress = await db.LearnerPronunciationProgress
            .FirstOrDefaultAsync(p => p.UserId == userId && p.PhonemeCode == drill.TargetPhoneme, ct);

        if (progress == null)
        {
            progress = new LearnerPronunciationProgress
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PhonemeCode = drill.TargetPhoneme,
                AverageScore = overall,
                AttemptCount = 1,
                ScoreHistoryJson = JsonSupport.Serialize(new[] { Math.Round(overall, 1) }),
                LastPracticedAt = now
            };
            db.LearnerPronunciationProgress.Add(progress);
        }
        else
        {
            progress.AttemptCount++;
            progress.AverageScore = (progress.AverageScore * (progress.AttemptCount - 1) + overall) / progress.AttemptCount;
            progress.LastPracticedAt = now;

            var history = JsonSupport.Deserialize(progress.ScoreHistoryJson, new List<double>());
            history.Add(Math.Round(overall, 1));
            if (history.Count > 20) history.RemoveRange(0, history.Count - 20);
            progress.ScoreHistoryJson = JsonSupport.Serialize(history);
        }

        await db.SaveChangesAsync(ct);

        return new
        {
            assessmentId = assessment.Id,
            drillId,
            phoneme = drill.TargetPhoneme,
            accuracy = assessment.AccuracyScore,
            fluency = assessment.FluencyScore,
            completeness = assessment.CompletenessScore,
            prosody = assessment.ProsodyScore,
            overall = assessment.OverallScore,
            wordScoresJson = assessment.WordScoresJson,
            problematicPhonemesJson = assessment.ProblematicPhonemesJson,
            fluencyMarkersJson = assessment.FluencyMarkersJson,
            createdAt = assessment.CreatedAt
        };
    }

    public async Task<object> GetAssessmentAsync(string userId, string assessmentId, CancellationToken ct)
    {
        var assessment = await db.PronunciationAssessments
            .FirstOrDefaultAsync(a => a.Id == assessmentId && a.UserId == userId, ct)
            ?? throw ApiException.NotFound("ASSESSMENT_NOT_FOUND", "Pronunciation assessment not found.");

        return new
        {
            id = assessment.Id,
            accuracy = assessment.AccuracyScore,
            fluency = assessment.FluencyScore,
            completeness = assessment.CompletenessScore,
            prosody = assessment.ProsodyScore,
            overall = assessment.OverallScore,
            wordScoresJson = assessment.WordScoresJson,
            problematicPhonemesJson = assessment.ProblematicPhonemesJson,
            fluencyMarkersJson = assessment.FluencyMarkersJson,
            createdAt = assessment.CreatedAt
        };
    }
}
