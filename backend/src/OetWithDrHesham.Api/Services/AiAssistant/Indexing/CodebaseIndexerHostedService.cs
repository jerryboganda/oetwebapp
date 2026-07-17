namespace OetWithDrHesham.Api.Services.AiAssistant.Indexing;

/// <summary>
/// Background hosted service that triggers codebase indexing.
/// - Runs full index on startup (after 30 second delay)
/// - Re-indexes every 6 hours
/// - Can be triggered manually via admin endpoint
/// - Respects cancellation on shutdown
/// </summary>
public sealed class CodebaseIndexerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CodebaseIndexerHostedService> _logger;

    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReindexInterval = TimeSpan.FromHours(6);

    /// <summary>
    /// Raised when a manual re-index is requested. The hosted service
    /// listens on this to trigger early re-indexing.
    /// </summary>
    private static readonly SemaphoreSlim ManualTrigger = new(0, 1);

    public CodebaseIndexerHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<CodebaseIndexerHostedService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Trigger a manual re-index. Safe to call from admin endpoints.
    /// </summary>
    public static void TriggerReindex()
    {
        // Release the semaphore if it's not already signaled
        if (ManualTrigger.CurrentCount == 0)
        {
            try { ManualTrigger.Release(); } catch (SemaphoreFullException) { }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CodebaseIndexerHostedService started. Waiting {Delay} before first indexing run.", StartupDelay);

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunIndexingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in codebase indexing cycle.");
            }

            // Wait for either the re-index interval or a manual trigger
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var delayTask = Task.Delay(ReindexInterval, cts.Token);
                var triggerTask = ManualTrigger.WaitAsync(cts.Token);

                await Task.WhenAny(delayTask, triggerTask);
                cts.Cancel(); // Cancel whichever didn't complete
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("CodebaseIndexerHostedService stopping.");
    }

    private async Task RunIndexingAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting codebase indexing cycle.");

        await using var scope = _scopeFactory.CreateAsyncScope();
        var indexer = scope.ServiceProvider.GetRequiredService<ICodebaseIndexer>();

        await indexer.IndexFullAsync(ct);

        _logger.LogInformation("Codebase indexing cycle completed.");
    }
}
