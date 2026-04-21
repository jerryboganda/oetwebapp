using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

[Index(nameof(Status), nameof(MockType))]
[Index(nameof(ProfessionId), nameof(MockType))]
[Index(nameof(Slug), IsUnique = true)]
public class MockBundle
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(200)]
    public string Slug { get; set; } = default!;

    [MaxLength(16)]
    public string ExamFamilyCode { get; set; } = "oet";

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = "oet";

    [MaxLength(16)]
    public string MockType { get; set; } = "full";

    [MaxLength(32)]
    public string? SubtestCode { get; set; }

    [MaxLength(32)]
    public string? ProfessionId { get; set; }

    public bool AppliesToAllProfessions { get; set; } = true;

    public ContentStatus Status { get; set; } = ContentStatus.Draft;

    public int EstimatedDurationMinutes { get; set; }

    public int Priority { get; set; }

    [MaxLength(512)]
    public string TagsCsv { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? SourceProvenance { get; set; }

    [MaxLength(64)]
    public string? CreatedByAdminId { get; set; }

    [MaxLength(64)]
    public string? UpdatedByAdminId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }

    public ICollection<MockBundleSection> Sections { get; set; } = new List<MockBundleSection>();
}

[Index(nameof(MockBundleId), nameof(SectionOrder), IsUnique = true)]
[Index(nameof(MockBundleId), nameof(SubtestCode))]
[Index(nameof(ContentPaperId))]
public class MockBundleSection
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string MockBundleId { get; set; } = default!;

    public int SectionOrder { get; set; }

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    [MaxLength(64)]
    public string ContentPaperId { get; set; } = default!;

    public int TimeLimitMinutes { get; set; }

    public bool ReviewEligible { get; set; }

    public bool IsRequired { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public MockBundle? MockBundle { get; set; }
    public ContentPaper? ContentPaper { get; set; }
}

[Index(nameof(MockAttemptId), nameof(MockBundleSectionId), IsUnique = true)]
[Index(nameof(MockAttemptId), nameof(SubtestCode))]
[Index(nameof(ContentAttemptId))]
public class MockSectionAttempt
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string MockAttemptId { get; set; } = default!;

    [MaxLength(64)]
    public string MockBundleSectionId { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;

    public AttemptState State { get; set; } = AttemptState.NotStarted;

    [MaxLength(64)]
    public string ContentPaperId { get; set; } = default!;

    [MaxLength(512)]
    public string LaunchRoute { get; set; } = default!;

    [MaxLength(64)]
    public string? ContentAttemptId { get; set; }

    public int? RawScore { get; set; }
    public int? RawScoreMax { get; set; }
    public int? ScaledScore { get; set; }

    [MaxLength(8)]
    public string? Grade { get; set; }

    public string FeedbackJson { get; set; } = "{}";

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? DeadlineAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public MockAttempt? MockAttempt { get; set; }
    public MockBundleSection? MockBundleSection { get; set; }
}

[Index(nameof(UserId), nameof(State))]
[Index(nameof(MockAttemptId), IsUnique = true)]
public class MockReviewReservation
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(64)]
    public string MockAttemptId { get; set; } = default!;

    [MaxLength(64)]
    public string WalletId { get; set; } = default!;

    public MockReviewReservationState State { get; set; } = MockReviewReservationState.Reserved;

    public int ReservedCredits { get; set; }
    public int ConsumedCredits { get; set; }
    public int ReleasedCredits { get; set; }

    [MaxLength(32)]
    public string Selection { get; set; } = "none";

    public DateTimeOffset ReservedAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    public Guid? DebitTransactionId { get; set; }
    public Guid? ReleaseTransactionId { get; set; }

    public MockAttempt? MockAttempt { get; set; }
}
