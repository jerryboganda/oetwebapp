using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public class LearnerStreak
{
    [Key]
    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
    public DateOnly LastActiveDate { get; set; }
    public int StreakFreezeCount { get; set; } = 1;        // Free freezes available
    public int StreakFreezeUsedCount { get; set; }
    public DateOnly? LastFreezeUsedDate { get; set; }
}

public class LearnerXP
{
    [Key]
    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    public long TotalXP { get; set; }
    public long WeeklyXP { get; set; }
    public long MonthlyXP { get; set; }
    public int Level { get; set; } = 1;
    public DateOnly WeekStartDate { get; set; }
    public DateOnly MonthStartDate { get; set; }
}

public class Achievement
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string Code { get; set; } = default!;

    [MaxLength(128)]
    public string Label { get; set; } = default!;

    [MaxLength(512)]
    public string Description { get; set; } = default!;

    [MaxLength(32)]
    public string Category { get; set; } = default!;      // "practice", "streak", "milestone", "mastery", "social"

    [MaxLength(256)]
    public string? IconUrl { get; set; }

    public int XPReward { get; set; }
    public string CriteriaJson { get; set; } = "{}";      // Unlock conditions
    public int SortOrder { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "active";
}

public class LearnerAchievement
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string AchievementId { get; set; } = default!;

    public DateTimeOffset UnlockedAt { get; set; }
    public bool Notified { get; set; }
}

public class LeaderboardEntry
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(128)]
    public string DisplayName { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(16)]
    public string Period { get; set; } = default!;        // "weekly", "monthly", "alltime"

    public DateOnly PeriodStart { get; set; }
    public long XP { get; set; }
    public int Rank { get; set; }
    public bool OptedIn { get; set; } = true;
}
