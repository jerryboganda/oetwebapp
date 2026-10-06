using Fleet.Manager.Auth;
using Fleet.Manager.Dashboard;
using Microsoft.AspNetCore.Mvc;

namespace Fleet.Manager.Pages;

/// <summary>
/// Workloads: per job kind the queue depth, active jobs, how the jobs ended, the helpers that could take the next one and where it would go
/// (and why). The queue figures come from the OET API; the placement is the manager's own engine run as a what-if (nothing is reserved).
/// </summary>
public sealed class WorkloadsModel : FleetPageModel
{
    private readonly DashboardService _dashboard;

    public WorkloadsModel(OwnerAccountService owner, DashboardService dashboard)
        : base(owner)
    {
        _dashboard = dashboard;
    }

    public WorkloadsView Workloads { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Workloads = await _dashboard.GetWorkloadsAsync(cancellationToken);

    public async Task<IActionResult> OnGetFragmentAsync(CancellationToken cancellationToken) =>
        Partial("_WorkloadsBody", await _dashboard.GetWorkloadsAsync(cancellationToken));
}
