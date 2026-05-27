using Microsoft.Extensions.Options;
using OetLearner.Api.Services.Writing.Configuration;

namespace OetLearner.Api.Services.Writing.Crons;

/// <summary>
/// Shared base for Writing Module V2 hosted services. Honours the
/// <c>Writing:CronsEnabled</c> kill switch (so tests / dev runs can disable
/// schedulers without injecting timers) and provides a deterministic
/// daily-window helper for crons that must only fire once per day.
/// </summary>
public abstract class WritingCronBase(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<WritingV2Options> options,
    ILogger logger) : BackgroundService
{
    protected IServiceScopeFactory ScopeFactory => scopeFactory;
    protected TimeProvider Clock => clock;
    protected WritingV2Options Options => options.Value;
    protected ILogger Logger => logger;

    protected abstract TimeSpan Interval { get; }
    protected abstract Task RunOnceAsync(CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(5, 60)), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!Options.CronsEnabled)
            {
                try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
                catch (OperationCanceledException) { return; }
                continue;
            }
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{Cron} tick failed.", GetType().Name);
            }
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
