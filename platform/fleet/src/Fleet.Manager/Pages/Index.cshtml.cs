using Fleet.Manager.Auth;
using Fleet.Manager.Dashboard;
using Fleet.Manager.Monitoring;
using Microsoft.AspNetCore.Mvc;

namespace Fleet.Manager.Pages;

/// <summary>Fleet overview: the primary, every helper, capacity, utilisation, latency and what is offline or waiting for the owner.</summary>
public sealed class IndexModel : FleetPageModel
{
    private static readonly SemaphoreSlim RefreshGate = new(1, 1);

    private readonly DashboardService _dashboard;
    private readonly NodeMonitor _monitor;

    public IndexModel(OwnerAccountService owner, DashboardService dashboard, NodeMonitor monitor)
        : base(owner)
    {
        _dashboard = dashboard;
        _monitor = monitor;
    }

    public FleetOverview Overview { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        LoadNotice();
        Overview = await _dashboard.GetOverviewAsync(cancellationToken);
    }

    /// <summary>The live part of the page: the browser fetches it when the server says something changed (and on a timer), and swaps it in.</summary>
    public async Task<IActionResult> OnGetFragmentAsync(CancellationToken cancellationToken) =>
        Partial("_FleetBody", await _dashboard.GetOverviewAsync(cancellationToken));

    /// <summary>Polls the OET API now instead of waiting for the next 15-second tick. Read-only towards the helpers, so no step-up.</summary>
    public async Task<IActionResult> OnPostRefreshAsync(CancellationToken cancellationToken)
    {
        if (!await RefreshGate.WaitAsync(0, cancellationToken))
        {
            return Done("/", "refresh-busy");
        }

        try
        {
            await _monitor.PollOnceAsync(cancellationToken);
        }
        finally
        {
            RefreshGate.Release();
        }

        return Done("/", "refreshed");
    }
}
