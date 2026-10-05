using Fleet.Core.Policy;
using Fleet.Core.Validation;
using Fleet.Manager.Auth;
using Fleet.Manager.Dashboard;
using Fleet.Manager.Operations;
using Microsoft.AspNetCore.Mvc;

namespace Fleet.Manager.Pages;

/// <summary>
/// Policies: the global default every helper follows (allocation, concurrency, budgets, pressure, polling), the fallback rules for work that no
/// helper can take, and which helpers have limits of their own. Saving needs a fresh authenticator code and is pushed to every helper at once.
/// </summary>
public sealed class PoliciesModel : FleetPageModel
{
    private readonly DashboardService _dashboard;
    private readonly PolicyService _policies;

    public PoliciesModel(OwnerAccountService owner, DashboardService dashboard, PolicyService policies)
        : base(owner)
    {
        _dashboard = dashboard;
        _policies = policies;
    }

    public PoliciesView Policies { get; private set; } = null!;

    /// <summary>The global policy form. Prefilled from the stored policy; keeps what was posted when a post is refused.</summary>
    [BindProperty]
    public PolicyInput Input { get; set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        LoadNotice();
        Policies = await _dashboard.GetPoliciesAsync(cancellationToken);
        Input = PolicyInput.From(Policies.Global, Policies.RegistryKinds);
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        Policies = await _dashboard.GetPoliciesAsync(cancellationToken);

        if (!ModelState.IsValid)
        {
            Fail(StatusCodes.Status422UnprocessableEntity, "Some of the numbers are not whole numbers. No authenticator code was used.");
            return Page();
        }

        var policy = Input.TryBuild(Policies.Global, out var missing);
        if (policy is null)
        {
            Fail(StatusCodes.Status422UnprocessableEntity, "The policy is incomplete. No authenticator code was used.", missing);
            return Page();
        }

        var ranges = PolicyValidator.Validate(policy, Policies.RegistryKnown ? Policies.RegistryKinds : null);
        if (ranges.Count > 0)
        {
            Fail(StatusCodes.Status422UnprocessableEntity, "The policy is outside the allowed ranges. No authenticator code was used.", ranges);
            return Page();
        }

        if (!await AuthorizeAsync(cancellationToken)
            || !await TryAsync(() => _policies.SetGlobalAsync(policy, Actor, cancellationToken)))
        {
            return Page();
        }

        // Stored. Now every helper gets it; one that cannot be reached is counted and shown, never hidden.
        var pushed = await _policies.PushAllAsync(Actor, cancellationToken);
        return Done("/Policies", "policy-saved", pushed.Count(result => result.Success), pushed.Count(result => !result.Success));
    }
}
