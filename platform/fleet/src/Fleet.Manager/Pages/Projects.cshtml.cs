using Fleet.Manager.Auth;
using Fleet.Manager.Dashboard;
using Microsoft.AspNetCore.Mvc;

namespace Fleet.Manager.Pages;

/// <summary>Project overview: how much of the primary the console can see, what runs on it, and the state of the OET integration.</summary>
public sealed class ProjectsModel : FleetPageModel
{
    private readonly DashboardService _dashboard;

    public ProjectsModel(OwnerAccountService owner, DashboardService dashboard)
        : base(owner)
    {
        _dashboard = dashboard;
    }

    public ProjectsView Projects { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Projects = await _dashboard.GetProjectsAsync(cancellationToken);

    public async Task<IActionResult> OnGetFragmentAsync(CancellationToken cancellationToken) =>
        Partial("_ProjectsBody", await _dashboard.GetProjectsAsync(cancellationToken));
}
