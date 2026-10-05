using Fleet.Manager.Monitoring;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fleet.Manager.Pages;

/// <summary>The only screen of this track besides sign-in: what the manager itself knows about its own health. The dashboard is built on top of the JSON API later.</summary>
public sealed class HealthModel : PageModel
{
    private readonly HealthReporter _health;

    public HealthModel(HealthReporter health)
    {
        _health = health;
    }

    public HealthReport Report { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Report = await _health.GetAsync(cancellationToken);
    }
}
