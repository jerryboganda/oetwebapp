using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Data;

public partial class LearnerDbContext
{
    // ── Phase 1 ──
    public DbSet<RegionPricing> RegionPricings => Set<RegionPricing>();
    public DbSet<GatewayRoutingConfig> GatewayRoutingConfigs => Set<GatewayRoutingConfig>();

    // ── Phase 3 ──
    public DbSet<TaxRule> TaxRules => Set<TaxRule>();

    // ── Phase 4 ──
    public DbSet<ManualPaymentRequest> ManualPaymentRequests => Set<ManualPaymentRequest>();
    public DbSet<BankAccountConfig> BankAccountConfigs => Set<BankAccountConfig>();

    // ── Phase 5 ──
    public DbSet<DunningCampaign> DunningCampaigns => Set<DunningCampaign>();
    public DbSet<PaymentMethodUpdateLink> PaymentMethodUpdateLinks => Set<PaymentMethodUpdateLink>();

    // ── Wave A5 — smart-retry dunning attempts (T+24h / T+72h / T+168h) ──
    public DbSet<DunningAttempt> DunningAttempts => Set<DunningAttempt>();

    // ── Phase 6 ──
    public DbSet<CancellationIntent> CancellationIntents => Set<CancellationIntent>();
    public DbSet<DeflectionRule> DeflectionRules => Set<DeflectionRule>();

    // ── Phase 7 ──
    public DbSet<Scholarship> Scholarships => Set<Scholarship>();

    // ── Phase 8 ──
    public DbSet<Affiliate> Affiliates => Set<Affiliate>();
    public DbSet<AffiliateAttribution> AffiliateAttributions => Set<AffiliateAttribution>();
    public DbSet<AffiliateCommission> AffiliateCommissions => Set<AffiliateCommission>();

    // ── Phase 9 ──
    public DbSet<BillingNotificationTemplate> BillingNotificationTemplates => Set<BillingNotificationTemplate>();
    public DbSet<BillingNotificationDispatchLog> BillingNotificationDispatchLogs => Set<BillingNotificationDispatchLog>();

    // ── Phase 10 ──
    public DbSet<BillingMetricDaily> BillingMetricDailies => Set<BillingMetricDaily>();

    partial void OnModelCreatingBillingRegion(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RegionPricing>(e =>
        {
            e.Property(x => x.PriceAmount).HasColumnType("numeric(12,2)");
        });

        modelBuilder.Entity<TaxRule>(e =>
        {
            e.Property(x => x.RatePercent).HasColumnType("numeric(6,3)");
        });

        modelBuilder.Entity<ManualPaymentRequest>(e =>
        {
            e.Property(x => x.AmountAmount).HasColumnType("numeric(12,2)");
        });

        modelBuilder.Entity<Affiliate>(e =>
        {
            e.Property(x => x.CommissionPercent).HasColumnType("numeric(6,3)");
            e.Property(x => x.PayoutThresholdAmount).HasColumnType("numeric(12,2)");
        });

        modelBuilder.Entity<AffiliateCommission>(e =>
        {
            e.Property(x => x.AmountAmount).HasColumnType("numeric(12,2)");
        });

        modelBuilder.Entity<BillingMetricDaily>(e =>
        {
            e.Property(x => x.Value).HasColumnType("numeric(18,4)");
        });
    }
}
