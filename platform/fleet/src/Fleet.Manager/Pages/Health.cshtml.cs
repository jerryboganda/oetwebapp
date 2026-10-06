using Fleet.Manager.Monitoring;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fleet.Manager.Pages;

/// <summary>What the manager itself knows about its own health (the console's startup and integrity checks).</summary>
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
