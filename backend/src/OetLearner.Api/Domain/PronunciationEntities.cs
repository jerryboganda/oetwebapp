using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public class PronunciationAssessment
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string? AttemptId { get; set; }                 // Speaking attempt or conversation session

    [MaxLength(64)]
    public string? ConversationSessionId { get; set; }

    public double AccuracyScore { get; set; }              // 0-100
    public double FluencyScore { get; set; }               // 0-100
    public double CompletenessScore { get; set; }          // 0-100
    public double ProsodyScore { get; set; }               // 0-100
    public double OverallScore { get; set; }               // 0-100

    public string WordScoresJson { get; set; } = "[]";     // Per-word scores with phoneme breakdown
    public string ProblematicPhonemesJson { get; set; } = "[]";
    public string FluencyMarkersJson { get; set; } = "{}"; // Pause locations, speech rate

    public DateTimeOffset CreatedAt { get; set; }
}

public class PronunciationDrill
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(32)]
    public string TargetPhoneme { get; set; } = default!;  // IPA symbol

    [MaxLength(128)]
    public string Label { get; set; } = default!;          // "th (as in 'think')"

    public string ExampleWordsJson { get; set; } = "[]";   // Words containing this phoneme
    public string MinimalPairsJson { get; set; } = "[]";   // Minimal pair exercises (ship/sheep)
    public string SentencesJson { get; set; } = "[]";      // Practice sentences

    [MaxLength(256)]
    public string? AudioModelUrl { get; set; }             // Model pronunciation audio

    [MaxLength(512)]
    public string TipsHtml { get; set; } = default!;       // Articulation tips

    [MaxLength(16)]
    public string Difficulty { get; set; } = "medium";

    [MaxLength(16)]
    public string Status { get; set; } = "active";
}

public class LearnerPronunciationProgress
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(32)]
    public string PhonemeCode { get; set; } = default!;

    public double AverageScore { get; set; }               // Rolling average
    public int AttemptCount { get; set; }
    public string ScoreHistoryJson { get; set; } = "[]";   // Last 20 scores
    public DateTimeOffset LastPracticedAt { get; set; }
}
