using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.AiPipeline;

/// <summary>
/// The owner-saved live voice order (stage <c>speaking.live_voice</c>) as a process-wide snapshot, so the synchronous
/// candidate list and the mint routes can read it without a database call. <see cref="LiveVoiceRoutingRefresher"/>
/// reloads it every few seconds in every process (API slots and worker), so a change reaches NEW sessions within
/// seconds and never interrupts a session that is already running. Until the first successful load the legacy
/// environment order is used; once a saved order has been loaded it is authoritative, and a failed reload keeps the
/// last loaded one.
/// </summary>
public static class LiveVoiceRouting
{
    public sealed record Snapshot(IReadOnlyList<string> Order, IReadOnlySet<string> Enabled);

    private static volatile Snapshot? _current;

    public static Snapshot? Current => _current;

    public static void Set(IReadOnlyList<AiPipelineHop> hops)
    {
        var order = hops.Select(h => LiveVoiceOptions.NormalizeProvider(h.Provider))
            .Where(p => p.Length > 0).Distinct().ToList();
        var enabled = hops.Where(h => h.Enabled).Select(h => LiveVoiceOptions.NormalizeProvider(h.Provider))
            .Where(p => p.Length > 0).ToHashSet();
        _current = new Snapshot(order, enabled);
    }

    /// <summary>Saved order restricted to enabled providers; null before the first load (use the legacy order).</summary>
    public static IReadOnlyList<string>? EnabledOrder()
        => _current is { } s ? s.Order.Where(s.Enabled.Contains).ToList() : null;

    /// <summary>False only when a saved order exists and switches this provider off.</summary>
    public static bool IsEnabled(string provider)
        => _current is not { } s || s.Enabled.Contains(LiveVoiceOptions.NormalizeProvider(provider));
}

public sealed class LiveVoiceRoutingRefresher(IServiceScopeFactory scopes, ILogger<LiveVoiceRoutingRefresher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IAiPipelineStore>();
                var config = await store.ReadAsync(AiPipelineStageKeys.LiveVoice, stoppingToken);
                // A never-saved stage (initial default) must not override the environment order.
                if (config.Source == AiPipelineSource.Saved) LiveVoiceRouting.Set(config.Hops);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Live voice routing reload failed ({Type}); keeping the last loaded order.", ex.GetType().Name);
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
