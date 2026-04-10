using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

public class ForumCategory
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(16)]
    public string? ExamTypeCode { get; set; }              // null = general

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    [MaxLength(512)]
    public string Description { get; set; } = default!;

    public int SortOrder { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "active";
}

public class ForumThread
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string CategoryId { get; set; } = default!;

    [MaxLength(64)]
    public string AuthorUserId { get; set; } = default!;

    [MaxLength(128)]
    public string AuthorDisplayName { get; set; } = default!;

    [MaxLength(32)]
    public string AuthorRole { get; set; } = default!;    // "learner", "expert", "admin"

    [MaxLength(256)]
    public string Title { get; set; } = default!;

    public string Body { get; set; } = default!;

    public bool IsPinned { get; set; }
    public bool IsLocked { get; set; }
    public int ReplyCount { get; set; }
    public int ViewCount { get; set; }
    public int LikeCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }
}

public class ForumReply
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string ThreadId { get; set; } = default!;

    [MaxLength(64)]
    public string AuthorUserId { get; set; } = default!;

    [MaxLength(128)]
    public string AuthorDisplayName { get; set; } = default!;

    [MaxLength(32)]
    public string AuthorRole { get; set; } = default!;

    public string Body { get; set; } = default!;

    public bool IsExpertVerified { get; set; }            // Expert-verified answer badge
    public int LikeCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
}

public class StudyGroup
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(128)]
    public string Name { get; set; } = default!;

    [MaxLength(512)]
    public string Description { get; set; } = default!;

    [MaxLength(16)]
    public string ExamTypeCode { get; set; } = default!;

    [MaxLength(64)]
    public string CreatorUserId { get; set; } = default!;

    public int MaxMembers { get; set; } = 20;
    public int MemberCount { get; set; }
    public bool IsPublic { get; set; } = true;

    [MaxLength(16)]
    public string Status { get; set; } = "active";

    public DateTimeOffset CreatedAt { get; set; }
}

public class StudyGroupMember
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string GroupId { get; set; } = default!;

    [MaxLength(64)]
    public string UserId { get; set; } = default!;

    [MaxLength(32)]
    public string Role { get; set; } = "member";         // "owner", "moderator", "member"

    public DateTimeOffset JoinedAt { get; set; }
}

public class PeerReviewRequest
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string SubmitterUserId { get; set; } = default!;

    [MaxLength(64)]
    public string? ReviewerUserId { get; set; }

    [MaxLength(64)]
    public string AttemptId { get; set; } = default!;

    [MaxLength(32)]
    public string SubtestCode { get; set; } = default!;           // writing, speaking

    [MaxLength(32)]
    public string Status { get; set; } = "open";                  // open, claimed, completed, expired

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class PeerReviewFeedback
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [MaxLength(64)]
    public string PeerReviewRequestId { get; set; } = default!;

    [MaxLength(64)]
    public string ReviewerUserId { get; set; } = default!;

    public int OverallRating { get; set; }                        // 1-5 stars

    [MaxLength(2000)]
    public string Comments { get; set; } = default!;

    [MaxLength(1000)]
    public string? StrengthNotes { get; set; }

    [MaxLength(1000)]
    public string? ImprovementNotes { get; set; }

    public int HelpfulnessRating { get; set; }                    // 0 = unrated, 1-5 set by submitter

    public DateTimeOffset CreatedAt { get; set; }
}
