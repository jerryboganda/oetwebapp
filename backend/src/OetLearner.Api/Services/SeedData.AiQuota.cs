using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public static partial class SeedData
{
    // ────────────────────────────────────────────────────────────────────
    // AI Usage Management seed data. See docs/AI-USAGE-POLICY.md.
    // ────────────────────────────────────────────────────────────────────

    private static void SeedAiQuotaPlans(LearnerDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        db.AiQuotaPlans.AddRange(
            new AiQuotaPlan
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = "free", Name = "Free tier",
                Description = "Entry tier — tight caps, conversational features only.",
                Period = AiQuotaPeriod.Monthly,
                MonthlyTokenCap = 20_000, DailyTokenCap = 5_000,
                RolloverPolicy = AiQuotaRolloverPolicy.Expire, RolloverCapPct = 0,
                OveragePolicy = AiOveragePolicy.Deny,
                AllowedFeaturesCsv = string.Join(",",
                    AiFeatureCodes.ConversationReply,
                    AiFeatureCodes.ConversationOpening,
                    AiFeatureCodes.ConversationEvaluation,
                    AiFeatureCodes.PronunciationScore,
                    AiFeatureCodes.PronunciationLinguisticScore,
                    AiFeatureCodes.PronunciationFeedback,
                    AiFeatureCodes.VocabularyGloss,
                    AiFeatureCodes.SummarisePassage,
                    // F-135 — the companion's token allowance once a package has
                    // granted access. This list is the METER, not the gate: access
                    // itself is ModuleKeys.AiCompanion, which only the packages
                    // created for the companion enable (owner directive). Listing
                    // the codes here means a learner whose package grants the
                    // module gets this plan's 20k/month, 5k/day and then the
                    // upgrade card — it does not, on its own, let anyone in.
                    AiFeatureCodes.AiAssistantLearner,
                    AiFeatureCodes.CompanionChat,
                    AiFeatureCodes.CompanionAction),
                IsActive = true, DisplayOrder = 10,
                CreatedAt = now, UpdatedAt = now,
            },
            new AiQuotaPlan
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = "starter", Name = "Starter",
                Description = "Entry paid tier — all practice features, capped grading.",
                Period = AiQuotaPeriod.Monthly,
                MonthlyTokenCap = 200_000, DailyTokenCap = 50_000,
                RolloverPolicy = AiQuotaRolloverPolicy.Expire, RolloverCapPct = 0,
                OveragePolicy = AiOveragePolicy.Deny,
                AllowedFeaturesCsv = string.Empty, // all features
                IsActive = true, DisplayOrder = 20,
                CreatedAt = now, UpdatedAt = now,
            },
            new AiQuotaPlan
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = "pro", Name = "Pro",
                Description = "Unlimited practice + generous grading allowance.",
                Period = AiQuotaPeriod.Monthly,
                MonthlyTokenCap = 1_000_000, DailyTokenCap = 200_000,
                RolloverPolicy = AiQuotaRolloverPolicy.RolloverCapped, RolloverCapPct = 20,
                OveragePolicy = AiOveragePolicy.Deny,
                AllowedFeaturesCsv = string.Empty,
                IsActive = true, DisplayOrder = 30,
                CreatedAt = now, UpdatedAt = now,
            },
            new AiQuotaPlan
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = "enterprise", Name = "Enterprise",
                Description = "Sponsored / institutional tier, very high limits.",
                Period = AiQuotaPeriod.Monthly,
                MonthlyTokenCap = 10_000_000, DailyTokenCap = 2_000_000,
                RolloverPolicy = AiQuotaRolloverPolicy.RolloverCapped, RolloverCapPct = 50,
                OveragePolicy = AiOveragePolicy.AllowWithCharge,
                OverageRatePer1kTokens = 0.004m,
                AllowedFeaturesCsv = string.Empty,
                IsActive = true, DisplayOrder = 40,
                CreatedAt = now, UpdatedAt = now,
            }
        );
    }

    /// <summary>
    /// Companion sellable tiers as quota policy (F-136 Plus / F-137 Pro /
    /// F-138 Ultimate). Prices decided by owner delegation 2026-09-07 (£9 /
    /// £19 / £39 per month, GBP): the meter a tier-holder gets once a
    /// companion product grants the AiCompanion module. Companion feature
    /// codes mirror the free plan's meter list — the module gate, not this
    /// list, controls who gets in. Rollover expires monthly (study rhythm);
    /// overage denies (fail closed, like the other learner tiers).
    /// </summary>
    private static IReadOnlyList<AiQuotaPlan> CompanionQuotaTiers()
    {
        var now = DateTimeOffset.UtcNow;
        string Meter() => string.Join(",",
            AiFeatureCodes.ConversationReply,
            AiFeatureCodes.ConversationOpening,
            AiFeatureCodes.ConversationEvaluation,
            AiFeatureCodes.PronunciationScore,
            AiFeatureCodes.PronunciationLinguisticScore,
            AiFeatureCodes.PronunciationFeedback,
            AiFeatureCodes.VocabularyGloss,
            AiFeatureCodes.SummarisePassage,
            AiFeatureCodes.AiAssistantLearner,
            AiFeatureCodes.CompanionChat,
            AiFeatureCodes.CompanionAction);
        return new[]
        {
            new AiQuotaPlan
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = "companion-plus", Name = "Companion Plus",
                Description = "Companion tier — £9/month, 100k tokens/month.",
                Period = AiQuotaPeriod.Monthly,
                MonthlyTokenCap = 100_000, DailyTokenCap = 10_000,
                RolloverPolicy = AiQuotaRolloverPolicy.Expire, RolloverCapPct = 0,
                OveragePolicy = AiOveragePolicy.Deny,
                AllowedFeaturesCsv = Meter(),
                IsActive = true, DisplayOrder = 25,
                CreatedAt = now, UpdatedAt = now,
            },
            new AiQuotaPlan
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = "companion-pro", Name = "Companion Pro",
                Description = "Companion tier — £19/month, 300k tokens/month.",
                Period = AiQuotaPeriod.Monthly,
                MonthlyTokenCap = 300_000, DailyTokenCap = 25_000,
                RolloverPolicy = AiQuotaRolloverPolicy.Expire, RolloverCapPct = 0,
                OveragePolicy = AiOveragePolicy.Deny,
                AllowedFeaturesCsv = Meter(),
                IsActive = true, DisplayOrder = 26,
                CreatedAt = now, UpdatedAt = now,
            },
            new AiQuotaPlan
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = "companion-ultimate", Name = "Companion Ultimate",
                Description = "Companion tier — £39/month, 1M tokens/month.",
                Period = AiQuotaPeriod.Monthly,
                MonthlyTokenCap = 1_000_000, DailyTokenCap = 100_000,
                RolloverPolicy = AiQuotaRolloverPolicy.Expire, RolloverCapPct = 0,
                OveragePolicy = AiOveragePolicy.Deny,
                AllowedFeaturesCsv = Meter(),
                IsActive = true, DisplayOrder = 27,
                CreatedAt = now, UpdatedAt = now,
            },
        };
    }

    private static void SeedAiGlobalPolicy(LearnerDbContext db)
    {
        db.AiGlobalPolicies.Add(new AiGlobalPolicy
        {
            Id = "global",
            KillSwitchEnabled = false,
            KillSwitchScope = AiKillSwitchScope.PlatformKeysOnly,
            // Conservative launch default (owner directive, 2026-08-28 AI/Cloud API
            // plan, point 5): "$10 prepaid... increase later after confirming real
            // usage". AiQuotaService.TryReserveAsync's hard-kill check is a no-op
            // when this is <= 0, so leaving it at 0 meant NO platform-wide dollar
            // ceiling was enforced out of the box. Admins raise this on
            // /admin/ai-usage → Budget once real usage is confirmed.
            MonthlyBudgetUsd = OetLearner.Api.Services.AiManagement.AiQuotaService.ConservativeDefaultMonthlyBudgetUsd,
            SoftWarnPct = 80,
            HardKillPct = 100,
            AllowByokOnScoringFeatures = false,     // safe default; see §1
            AllowByokOnNonScoringFeatures = true,
            DefaultPlatformProviderId = "digitalocean-serverless",
            ByokErrorCooldownHours = 24,
            ByokTransientRetryCount = 2,
            AnomalyDetectionEnabled = true,
            AnomalyMultiplierX = 10m,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
    }

    private static void SeedAiProviderStub(LearnerDbContext db)
    {
        // Production default: DigitalOcean Serverless Inference with the
        // project-standard GLM-5 text model. The API key is
        // supplied via AI__ApiKey env var and synchronised into this row at
        // boot (DatabaseBootstrapper) — never committed to source.
        var now = DateTimeOffset.UtcNow;
        db.AiProviders.Add(new AiProvider
        {
            Id = Guid.NewGuid().ToString("N"),
            Code = "digitalocean-serverless",
            Name = "DigitalOcean Serverless Inference (GLM-5)",
            Dialect = AiProviderDialect.OpenAiCompatible,
            BaseUrl = "https://inference.do-ai.run/v1",
            EncryptedApiKey = string.Empty,  // synchronised from AI__ApiKey at boot
            ApiKeyHint = "pending",
            DefaultModel = "glm-5",
            PricePer1kPromptTokens = 0.0006m,
            PricePer1kCompletionTokens = 0.0020m,
            RetryCount = 2,
            CircuitBreakerThreshold = 5,
            CircuitBreakerWindowSeconds = 30,
            FailoverPriority = 100,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }
}
