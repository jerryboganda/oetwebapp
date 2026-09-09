using System.ComponentModel.DataAnnotations;
using Pgvector;

namespace OetLearner.Api.Domain;

/// <summary>
/// Authority classes for companion knowledge. The source specification is
/// explicit that these are <b>separate classes, not one flat corpus</b>: an
/// official exam fact and a Dr Hesham teaching rule must never be blended, and
/// when they disagree the conflict is surfaced rather than averaged away.
/// See docs/ai-learning-companion/AI_RAG_MEMORY_AND_ROUTING.md §2 and §5.
/// </summary>
public enum CompanionAuthorityClass
{
    /// <summary>Verified current official exam/regulator fact. Wins official-fact questions.</summary>
    OfficialCurrentFact = 0,

    /// <summary>Approved Dr Hesham Rule Book / methodology. Wins teaching-strategy questions.</summary>
    DrHeshamApprovedMethod = 1,

    /// <summary>Profession-specific approved rule. Overrides the generic method when both apply.</summary>
    ProfessionApprovedMethod = 2,

    /// <summary>Package/entitlement-gated course material. Never retrievable without entitlement.</summary>
    CourseMaterial = 3,

    /// <summary>Product navigation and support knowledge. Not academic authority.</summary>
    PlatformSupport = 4,

    /// <summary>Candidate-specific evidence (scores, attempts, notes). Never shared across users.</summary>
    CandidateEvidence = 5,

    /// <summary>Versioned, approved authoritative correction. Highest precedence within its scope.</summary>
    AdminOverride = 6,
}

/// <summary>Lifecycle of a knowledge source. Only <c>Approved</c> is retrievable.</summary>
public enum CompanionSourceState
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    Retired = 3,
    Superseded = 4,
}

/// <summary>
/// A registered, approvable unit of companion knowledge (a rulebook, a support
/// article, a platform destination, later a transcript or PDF).
///
/// <para>
/// <b>Entitlement lives here, not on the chunk.</b> The retrieval pipeline
/// filters the candidate <i>source</i> universe before any vector or lexical
/// search runs, so a learner who lacks the scope never reaches the chunks at
/// all. That ordering is the F-154 requirement and is a zero-tolerance rule.
/// </para>
/// </summary>
public class CompanionSource
{
    public Guid Id { get; set; }

    /// <summary>Stable external identifier, e.g. <c>rulebook:writing:medicine:v1</c>.</summary>
    [MaxLength(256)]
    public string SourceKey { get; set; } = default!;

    [MaxLength(64)]
    public string SourceType { get; set; } = default!;

    [MaxLength(512)]
    public string Title { get; set; } = default!;

    public CompanionAuthorityClass AuthorityClass { get; set; }

    public CompanionSourceState State { get; set; } = CompanionSourceState.Draft;

    /// <summary>Exam family/type this source applies to, e.g. <c>OET</c>. Null = all exams.</summary>
    [MaxLength(32)]
    public string? ExamTypeCode { get; set; }

    /// <summary>Source version label. Newer approved versions win over obsolete ones.</summary>
    [MaxLength(64)]
    public string Version { get; set; } = "v1";

    /// <summary>When this version becomes authoritative. Resolved against the learner's exam date.</summary>
    public DateTimeOffset? EffectiveFrom { get; set; }

    public DateTimeOffset? EffectiveTo { get; set; }

    /// <summary>Profession scope, e.g. <c>medicine</c>. Null = applies to every profession.</summary>
    [MaxLength(64)]
    public string? ProfessionId { get; set; }

    /// <summary>Subtest scope: <c>writing</c>, <c>speaking</c>, <c>reading</c>, <c>listening</c>. Null = all.</summary>
    [MaxLength(32)]
    public string? SubtestCode { get; set; }

    /// <summary>
    /// Entitlement scope required to retrieve this source. Null means freely
    /// retrievable (public/official/platform knowledge). Any non-null value is
    /// checked against the learner's <c>EffectiveEntitlementSnapshot</c>
    /// <b>before</b> retrieval.
    /// </summary>
    [MaxLength(128)]
    public string? RequiredEntitlementScope { get; set; }

    /// <summary>
    /// Package isolation, in the same vocabulary as <c>LibraryVideo.VisibilityScope</c>
    /// (<see cref="VideoVisibilityScopes"/>). Null or <c>SHARED</c> means every
    /// entitled learner; a <c>FULL_*</c> or <c>CRASH</c> value means only learners
    /// whose packages resolve that scope.
    ///
    /// <para>
    /// Deliberately a second axis rather than a reuse of
    /// <see cref="RequiredEntitlementScope"/>: a learner holds a <i>set</i> of
    /// package scopes, so a single required-scope string cannot express "Full
    /// Medicine or Crash", and collapsing the two would deny a Crash learner the
    /// method they paid for. Profession, package and entitlement each gate a
    /// different thing (Manifest 1.B "content from one package must not be
    /// silently mixed into another").
    /// </para>
    /// </summary>
    [MaxLength(32)]
    public string? PackageScope { get; set; }

    /// <summary>Where an official fact was verified from. Required for <see cref="CompanionAuthorityClass.OfficialCurrentFact"/>.</summary>
    [MaxLength(1024)]
    public string? SourceUrl { get; set; }

    /// <summary>Who checked this against the official source, and when. An unverified official fact must not be Approved.</summary>
    [MaxLength(64)]
    public string? VerifiedByUserId { get; set; }

    public DateTimeOffset? VerifiedAt { get; set; }

    /// <summary>
    /// True when the source is proprietary teaching material subject to the
    /// verbatim-span and retrieval-volume caps. Even an entitled learner must
    /// not be able to use the companion as a bulk export channel.
    /// </summary>
    public bool IsProprietary { get; set; }

    /// <summary>
    /// Optional canary/watermark marker. Emitting it verbatim is a security
    /// event, not a formatting bug.
    /// </summary>
    [MaxLength(128)]
    public string? CanaryTag { get; set; }

    /// <summary>Content checksum, so a re-ingest can detect an unchanged source.</summary>
    [MaxLength(128)]
    public string? Checksum { get; set; }

    [MaxLength(1024)]
    public string? StorageLocator { get; set; }

    [MaxLength(64)]
    public string? ApprovedByUserId { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>Set when a newer version replaces this one. History stays auditable.</summary>
    public Guid? SupersededBySourceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A retrievable fragment of a <see cref="CompanionSource"/>, chunked by
/// pedagogical meaning (a rule, a section, a destination) rather than by a
/// fixed character count, and always carrying its exact source location so a
/// citation can point at a page, slide or timestamp.
/// </summary>
public class CompanionChunk
{
    public Guid Id { get; set; }

    public Guid SourceId { get; set; }

    public int Ordinal { get; set; }

    /// <summary>Human-readable heading used in citations, e.g. the rule id or section title.</summary>
    [MaxLength(512)]
    public string? Heading { get; set; }

    public string Text { get; set; } = string.Empty;

    /// <summary>Page or slide number where available.</summary>
    public int? PageNumber { get; set; }

    /// <summary>Video/audio offset in seconds where available. Enables timestamp deep links.</summary>
    public int? TimestampSeconds { get; set; }

    /// <summary>SHA-256 of <see cref="Text"/>, so an unchanged chunk is not re-embedded.</summary>
    [MaxLength(128)]
    public string ContentHash { get; set; } = default!;

    [MaxLength(64)]
    public string EmbeddingModelId { get; set; } = "text-embedding-3-small";

    /// <summary>
    /// Native pgvector column (<c>vector(1536)</c>), mirroring
    /// <c>WritingScenarioEmbedding.Embedding</c>. Nullable so a chunk can be
    /// registered before the embedding pass runs, and so keyword-only retrieval
    /// still works when no embedding provider is configured.
    /// </summary>
    public Vector? Embedding { get; set; }

    /// <summary>Knowledge release that produced this chunk; lets a stale index be invalidated.</summary>
    public Guid? ReleaseId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A published, versioned snapshot of the knowledge index.
///
/// <para>
/// This exists so knowledge can be rolled back <b>without an application
/// deploy</b> — the source treats those as independent recovery controls. A
/// release records what went in, who approved it, and which release to fall
/// back to.
/// </para>
/// </summary>
public class CompanionKnowledgeRelease
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public string ReleaseVersion { get; set; } = default!;

    [MaxLength(64)]
    public string Status { get; set; } = "draft";

    public int SourceCount { get; set; }

    public int ChunkCount { get; set; }

    /// <summary>Checksum over the included source versions, for reproducibility.</summary>
    [MaxLength(128)]
    public string? IndexChecksum { get; set; }

    /// <summary>
    /// Reference to the evaluation run that gated this release. The source
    /// forbids publishing before golden/retrieval regression passes and no
    /// critical leak remains.
    /// </summary>
    [MaxLength(256)]
    public string? EvaluationReportRef { get; set; }

    [MaxLength(64)]
    public string? ApprovedByUserId { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>Release to roll back to. Null on the first release.</summary>
    public Guid? RollbackTargetReleaseId { get; set; }

    [MaxLength(2048)]
    public string? Changelog { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
