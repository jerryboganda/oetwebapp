using Microsoft.Extensions.DependencyInjection.Extensions;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Listening;
using OetLearner.Api.Services.Speaking;
using OetLearner.Api.Services.Writing.Crons;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// W4 — single registration point for cost-bearing AI hosted services so
/// API mode can drain them without hunting <c>Program.cs</c>, and tests can
/// assert the set without booting Kestrel.
/// </summary>
public static class AiCostBearingHostedServiceRegistration
{
    public static readonly Type[] CostBearingWorkerTypes =
    [
        typeof(ListeningPartAAiScoringWorker),
        typeof(ListeningTtsJobWorker),
        typeof(SpeakingExamAutoAdvanceWorker),
        typeof(AiCreditRenewalWorker),
        typeof(AiAccountQuotaResetWorker),
        typeof(WritingDailyPlanCron),
        typeof(WritingReadinessCron),
        typeof(WritingBatchGradingCron),
        typeof(WritingAnalyticsAggregationCron),
        typeof(WritingTutorQueueAlertCron),
        typeof(WritingDraftCleanupCron),
        typeof(WritingContentAuditCron),
    ];

    public static void Add(
        IServiceCollection services,
        IConfiguration configuration,
        bool enableCostBearing,
        bool isWorker)
    {
        if (enableCostBearing)
        {
            if (configuration.GetValue<bool>("Listening:PartAAiScoring:Enabled"))
            {
                services.AddHostedService<ListeningPartAAiScoringWorker>();
            }

            services.AddHostedService<ListeningTtsJobWorker>();
            services.AddHostedService<SpeakingExamAutoAdvanceWorker>();
            services.AddHostedService<AiCreditRenewalWorker>();
            services.AddHostedService<AiAccountQuotaResetWorker>();
            services.AddHostedService<WritingDailyPlanCron>();
            services.AddHostedService<WritingReadinessCron>();
            services.AddHostedService<WritingBatchGradingCron>();
            services.AddHostedService<WritingAnalyticsAggregationCron>();
            services.AddHostedService<WritingTutorQueueAlertCron>();
            services.AddHostedService<WritingDraftCleanupCron>();
            services.AddHostedService<WritingContentAuditCron>();
        }

        if (isWorker)
        {
            services.TryAddSingleton<IAiOperationLeaseClaimer, AiOperationLeaseClaimer>();
            services.TryAddSingleton<IAiLeasedOperationHandler, AiLeasedOperationHandler>();
            services.AddHostedService<AiOperationWorker>();
        }
    }
}
