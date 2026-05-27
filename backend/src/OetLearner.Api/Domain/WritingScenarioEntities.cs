using System.ComponentModel.DataAnnotations;
using Pgvector;

namespace OetLearner.Api.Domain;

public class WritingScenario
{
    public Guid Id { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = default!;

    [MaxLength(8)]
    public string LetterType { get; set; } = default!;

    [MaxLength(64)]
    public string Profession { get; set; } = default!;

    [MaxLength(64)]
    public string? SubDiscipline { get; set; }

    public string TopicsJson { get; set; } = "[]";

    public int Difficulty { get; set; }

    public string CaseNotesMarkdown { get; set; } = default!;

    public string? CaseNotesStructuredJson { get; set; }

    public int EstimatedReadingMinutes { get; set; } = 5;

    public bool IsDiagnostic { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "draft";

    public int Version { get; set; } = 1;

    public Guid? PreviousVersionId { get; set; }

    [MaxLength(64)]
    public string AuthorId { get; set; } = default!;

    [MaxLength(64)]
    public string? ApprovedById { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public class WritingScenarioStructuredSentence
{
    public Guid Id { get; set; }

    public Guid ScenarioId { get; set; }

    public int Ordinal { get; set; }

    public string SentenceText { get; set; } = default!;

    [MaxLength(16)]
    public string RelevanceLabel { get; set; } = "relevant";

    [MaxLength(512)]
    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public class WritingScenarioEmbedding
{
    public Guid Id { get; set; }

    public Guid ScenarioId { get; set; }

    [MaxLength(64)]
    public string ModelId { get; set; } = "text-embedding-3-small";

    public int Dimensions { get; set; } = 1536;

    /// <summary>
    /// JSON-encoded <c>float[1536]</c> array. Retained as the source of truth
    /// for backward compatibility with the pre-pgvector C# cosine-similarity
    /// path. <see cref="Embedding"/> is the pgvector mirror — populated lazily
    /// at write time, and backfilled by
    /// <c>WritingExemplarEmbeddingService.BackfillFromJsonAsync</c>.
    /// </summary>
    public string EmbeddingJson { get; set; } = "[]";

    /// <summary>
    /// Native pgvector column (<c>vector(1536)</c>). Nullable so legacy rows
    /// can be backfilled without blocking inserts.
    /// </summary>
    public Vector? Embedding { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
