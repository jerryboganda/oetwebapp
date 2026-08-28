using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OetLearner.Api.Domain;

/// <summary>
/// W2 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// effective-dated provider/model rate card. Supersedes the flat,
/// point-in-time <see cref="AiProvider.PricePer1kPromptTokens"/> /
/// <see cref="AiProvider.PricePer1kCompletionTokens"/> pair for any call
/// site that needs cache-aware, historically-accurate cost math (the
/// existing gateway cost estimate in <c>AiGatewayService.ComputeCostEstimateAsync</c>
/// is left untouched in this wave — see <c>Services/Ai/AiPricingResolver.cs</c>).
///
/// <para>
/// One row per (<see cref="ProviderId"/>, <see cref="Model"/>) rate change,
/// dated with <see cref="EffectiveFrom"/>/<see cref="EffectiveTo"/> so a rate
/// change never rewrites the calculated cost of historical
/// <see cref="AiUsageRecord"/> rows — the resolver always looks up the rate
/// that was in force at the instant of the call, not "whatever the rate is
/// today". <see cref="PricingVersion"/> is the human-readable tag stamped
/// onto <see cref="AiUsageRecord.PricingVersion"/> / <see cref="AiUsageRecord.CalculatedCostUsd"/>.
/// </para>
/// </summary>
[Index(nameof(ProviderId), nameof(Model), nameof(EffectiveFrom), IsUnique = true, Name = "UX_AiModelPrices_Provider_Model_EffectiveFrom")]
public class AiModelPrice
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Provider registry code, e.g. <c>anthropic</c>, <c>openai</c>.
    /// Matches <see cref="AiProvider.Code"/> — not an FK, so a rate can be
    /// pre-loaded before the provider row exists.</summary>
    [MaxLength(64)]
    public string ProviderId { get; set; } = default!;

    /// <summary>Model identifier, e.g. <c>claude-sonnet-5</c>, <c>gpt-4o</c>.</summary>
    [MaxLength(128)]
    public string Model { get; set; } = default!;

    public DateTimeOffset EffectiveFrom { get; set; }

    /// <summary>Null means "still the current rate for this provider/model".</summary>
    public DateTimeOffset? EffectiveTo { get; set; }

    /// <summary>Human-readable rate-card tag, e.g. <c>2026-11-01</c>. Stamped
    /// onto every <see cref="AiUsageRecord"/> this rate priced.</summary>
    [MaxLength(32)]
    public string PricingVersion { get; set; } = default!;

    /// <summary>USD per 1,000 non-cached input/prompt tokens.</summary>
    public decimal InputPer1k { get; set; }

    /// <summary>USD per 1,000 non-cached output/completion tokens.</summary>
    public decimal OutputPer1k { get; set; }

    /// <summary>USD per 1,000 prompt-cache-write tokens (Anthropic
    /// <c>cache_creation_input_tokens</c>). Null when the provider does not
    /// bill a separate cache-write tier (e.g. OpenAI's automatic caching).</summary>
    public decimal? CacheWritePer1k { get; set; }

    /// <summary>USD per 1,000 prompt-cache-read tokens (Anthropic
    /// <c>cache_read_input_tokens</c>; OpenAI cached-input tokens).</summary>
    public decimal? CacheReadPer1k { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
