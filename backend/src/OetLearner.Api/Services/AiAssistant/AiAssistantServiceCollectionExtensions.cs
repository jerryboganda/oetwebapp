using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Services.AiAssistant.Orchestration;
using OetLearner.Api.Services.AiAssistant.Providers;

namespace OetLearner.Api.Services.AiAssistant;

public static class AiAssistantServiceCollectionExtensions
{
    /// <summary>
    /// Registers the AI Assistant orchestrator, providers, settings, and
    /// turn registry. Call from Program.cs:
    ///   <c>builder.Services.AddAiAssistant();</c>
    /// Endpoints + hub are mapped separately:
    ///   <c>app.MapAiAssistantChat();</c>
    ///   <c>app.MapAiAssistantAdmin();</c>
    ///   <c>app.MapHub&lt;AiAssistantHub&gt;(AiAssistantHub.HubPath);</c>
    /// </summary>
    public static IServiceCollection AddAiAssistant(this IServiceCollection services)
    {
        services.AddHttpClient("AiAssistant.OpenAi");

        services.AddSingleton<IAiAssistantSettingsService, AiAssistantSettingsService>();
        services.AddSingleton<AiAssistantSettingsService>(sp =>
            (AiAssistantSettingsService)sp.GetRequiredService<IAiAssistantSettingsService>());
        services.AddSingleton<AiAssistantTurnRegistry>();

        services.AddSingleton<ILlmProvider, OpenAiProvider>();
        services.AddSingleton<LlmProviderRegistry>();

        services.AddScoped<IAgentOrchestrator, SupervisorAgent>();

        return services;
    }
}
