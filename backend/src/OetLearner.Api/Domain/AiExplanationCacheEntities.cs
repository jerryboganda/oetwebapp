using System.ComponentModel.DataAnnotations;

namespace OetLearner.Api.Domain;

/// <summary>
/// Reusable, cross-learner cache of AI-generated post-submit advisory
/// explanations (owner directive 2026-08-28 AI/Cloud API plan, point 8:
/// "if the same explanation... already exists, reuse it instead of creating
/// another AI call").
///
/// <para>
/// Reading/Listening explanations are deterministic FUNCTIONS of the
/// author-curated evidence they are grounded on — the question, the correct
/// answer, the learner's selected wrong option, and the author-approved
/// rationale/source sentence — not of who the learner is. Two different
/// learners who make the identical mistake on the identical question,
/// against the identical approved evidence, want (and should get) the exact
/// same explanation, so the SECOND request never needs a provider call at
/// all.
/// </para>
///
/// <para>
/// <see cref="CacheKey"/> is a SHA-256 digest of every input that could
/// change the generated text (module, question id + version, the learner's
/// normalized selected answer, language, and the evidence content itself),
/// so an author editing the approved rationale/source sentence naturally
/// invalidates the cache (a new digest, not a stale hit) without any
/// explicit invalidation logic. The digest is computed the same way
/// <see cref="Services.Ai.AiIdempotencyKeyBuilder"/> and
/// <see cref="Services.Ai.CoordinatedAiGatewayService.BuildRequestHash"/>
/// already do elsewhere in the control plane.
/// </para>
///
/// <para>
/// Unlike a request-hash idempotency key, the VALUE stored here (the
/// generated explanation) is deliberately learner-facing content, not a
/// pointer — reuse across different learners is the entire point, so no
/// per-user scoping applies.
/// </para>
/// </summary>
public class AiExplanationCacheEntry
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary><c>"reading"</c> or <c>"listening"</c>.</summary>
    [MaxLength(16)]
    public string Module { get; set; } = default!;

    /// <summary>Denormalized for admin lookups/cache-busting by question — the
    /// uniqueness guarantee lives on <see cref="CacheKey"/>, not this column.</summary>
    [MaxLength(64)]
    public string QuestionId { get; set; } = default!;

    [MaxLength(8)]
    public string Language { get; set; } = default!;

    /// <summary>SHA-256 hex digest of (module, questionId, questionVersion,
    /// normalized selected answer, language, rationale, source sentence,
    /// passage/transcript evidence). Never a raw prompt or raw learner
    /// content — see the class doc comment.</summary>
    [MaxLength(64)]
    public string CacheKey { get; set; } = default!;

    /// <summary>Serialized explanation payload (the same shape returned to the
    /// caller — see <c>ExplanationDto</c>/<c>ListeningExplanationDto</c>).</summary>
    public string ExplanationJson { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastServedAt { get; set; }

    /// <summary>How many times this cached explanation was served instead of
    /// a fresh provider call — the whole point, made visible for admin
    /// reporting on the cost savings.</summary>
    public int ServedCount { get; set; }
}
