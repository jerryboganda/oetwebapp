using System.ComponentModel.DataAnnotations;
using Pgvector;

namespace OetLearner.Api.Domain;

public class WritingExemplar
{
    public Guid Id { get; set; }

    public Guid? ScenarioId { get; set; }

    [MaxLength(8)]
    public string LetterType { get; set; } = default!;

    [MaxLength(64)]
    public string Profession { get; set; } = default!;

    public string LetterContent { get; set; } = default!;

    public string AnnotationsJson { get; set; } = "[]";

    [MaxLength(8)]
    public string TargetBand { get; set; } = "A";

    [MaxLength(16)]
    public string Status { get; set; } = "draft";

    [MaxLength(64)]
    public string AuthorId { get; set; } = default!;

    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public class WritingExemplarAnnotation
{
    public Guid Id { get; set; }

    public Guid ExemplarId { get; set; }

    public int Ordinal { get; set; }

    public int? CharStart { get; set; }

    public int? CharEnd { get; set; }

    [MaxLength(64)]
    public string AnnotationType { get; set; } = "rule";

    [MaxLength(16)]
    public string? RuleId { get; set; }

    [MaxLength(1000)]
    public string Note { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
}

public class WritingExemplarEmbedding
{
    public Guid Id { get; set; }

    public Guid ExemplarId { get; set; }

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
    /// Native pgvector column (<c>vector(1536)</c>) used by the HNSW cosine
    /// index for nearest-neighbour queries. Nullable so legacy rows can be
    /// backfilled lazily without blocking inserts. New writes through
    /// <c>WritingExemplarEmbeddingService</c> populate both this column and
    /// <see cref="EmbeddingJson"/>; queries prefer the Vector column when
    /// populated, falling back to JSON cosine otherwise.
    /// </summary>
    public Vector? Embedding { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
