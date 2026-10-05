using Fleet.Core.Policy;
using Fleet.Core.Validation;
using Fleet.Manager.Api;
using Fleet.Manager.Auth;
using Fleet.Manager.Dashboard;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace Fleet.Manager.Pages.Hosts;

/// <summary>
/// One helper: hardware, installed components, assigned workloads, limits, health history and every admin action (drain, resume, disable,
/// repair, change limits, rotate the node token, re-pin a changed host key, remove from the fleet). The read-only part refreshes live; the forms
/// never do, so what you are typing is never replaced. Every action needs a fresh authenticator code.
/// </summary>
public sealed class DetailModel : FleetPageModel
{
    private readonly DashboardService _dashboard;
    private readonly HostService _hosts;
    private readonly HostStore _hostStore;
    private readonly PolicyService _policies;
    private readonly HostSecurityService _security;

    public DetailModel(
        OwnerAccountService owner,
        DashboardService dashboard,
        HostService hosts,
        HostStore hostStore,
        PolicyService policies,
        HostSecurityService security)
        : base(owner)
    {
        _dashboard = dashboard;
        _hosts = hosts;
        _hostStore = hostStore;
        _policies = policies;
        _security = security;
    }

    public HostPageView Detail { get; private set; } = null!;

    /// <summary>The per-helper limits form. Prefilled from the effective policy; keeps what was posted when a post is refused.</summary>
    [BindProperty]
    public PolicyInput Input { get; set; } = new();

    /// <summary>Which action the action form runs: <c>drain</c>, <c>resume</c>, <c>disable</c>, <c>repair</c> or <c>rotate-token</c>.</summary>
    [BindProperty]
    public string? ActionName { get; set; }

    /// <summary>The first characters of the fingerprint, typed to re-pin a changed host key.</summary>
    [BindProperty]
    public string? Fingerprint { get; set; }

    /// <summary>Typed to confirm a removal.</summary>
    [BindProperty]
    public string? ConfirmNodeRef { get; set; }

    [BindProperty]
    public bool Force { get; set; }

    /// <summary>The fingerprints the helper offers right now, shown after "fetch" of a re-pin (display only until you confirm them).</summary>
    public IReadOnlyList<HostKeyCandidateView>? RepinCandidates { get; private set; }

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken)
    {
        LoadNotice();
        return await RenderAsync(id, cancellationToken);
    }

    public async Task<IActionResult> OnGetFragmentAsync(string id, CancellationToken cancellationToken)
    {
        var view = await _dashboard.GetHostAsync(id, cancellationToken);
        if (view is null)
        {
            return NotFound();
        }

        return Partial("_HostLive", view);
    }

    // ---- actions -----------------------------------------------------------------------------

    /// <summary>
    /// The action form: ONE chosen action and one authenticator code. A radio choice (not several submit buttons) is used so that pressing Enter in
    /// the code box can never run an action nobody picked.
    /// </summary>
    public async Task<IActionResult> OnPostRunAsync(string id, CancellationToken cancellationToken)
    {
        switch (ActionName)
        {
            case "drain":
                return await OnPostDrainAsync(id, cancellationToken);
            case "resume":
                return await OnPostResumeAsync(id, cancellationToken);
            case "disable":
                return await OnPostDisableAsync(id, cancellationToken);
            case "repair":
                return await OnPostRepairAsync(id, cancellationToken);
            case "rotate-token":
                return await OnPostRotateTokenAsync(id, cancellationToken);
            default:
                Fail(StatusCodes.Status422UnprocessableEntity, "Choose an action first. No authenticator code was used.");
                return await RenderAsync(id, cancellationToken);
        }
    }

    public Task<IActionResult> OnPostDrainAsync(string id, CancellationToken cancellationToken) =>
        ActAsync(id, () => _hosts.StartDrainAsync(id, Actor, cancellationToken), "drain-requested", cancellationToken);

    public Task<IActionResult> OnPostResumeAsync(string id, CancellationToken cancellationToken) =>
        ActAsync(id, () => _hosts.StartEnableAsync(id, Actor, cancellationToken), "resume-requested", cancellationToken);

    public Task<IActionResult> OnPostDisableAsync(string id, CancellationToken cancellationToken) =>
        ActAsync(id, () => _hosts.StartDisableAsync(id, Actor, cancellationToken), "disable-requested", cancellationToken);

    public Task<IActionResult> OnPostRotateTokenAsync(string id, CancellationToken cancellationToken) =>
        ActAsync(id, () => _hosts.StartRotateTokenAsync(id, Actor, cancellationToken), "rotate-requested", cancellationToken);

    /// <summary>Repair re-runs the root-level preparation steps and asks for a fresh temporary SSH key on the operation page.</summary>
    public async Task<IActionResult> OnPostRepairAsync(string id, CancellationToken cancellationToken)
    {
        OperationView? operation = null;
        if (!await AuthorizeAsync(cancellationToken) || !await TryAsync(async () => operation = await _hosts.StartRepairAsync(id, Actor, cancellationToken)))
        {
            return await RenderAsync(id, cancellationToken);
        }

        return Done("/Operations/Detail/" + Escape(operation!.Id), "repair-requested");
    }

    /// <summary>Remove from the fleet: drain first, wait for running jobs, then uninstall ONLY what the fleet installed. You type the node reference to confirm.</summary>
    public async Task<IActionResult> OnPostRemoveAsync(string id, CancellationToken cancellationToken)
    {
        var host = await _hostStore.GetAsync(id, cancellationToken);
        if (host is null)
        {
            return NotFound();
        }

        if (!string.Equals(ConfirmNodeRef?.Trim(), host.NodeRef, StringComparison.Ordinal))
        {
            Fail(
                StatusCodes.Status422UnprocessableEntity,
                "Type the node reference exactly as shown to confirm the removal. No authenticator code was used.",
                new[] { new ValidationIssue("confirmNodeRef", "confirmation_mismatch", "The text does not match the node reference.") });
            return await RenderAsync(id, cancellationToken);
        }

        var force = Force;
        return await ActAsync(id, () => _hosts.StartRemoveAsync(id, force, Actor, cancellationToken), "remove-requested", cancellationToken);
    }

    // ---- limits ------------------------------------------------------------------------------

    public async Task<IActionResult> OnPostLimitsAsync(string id, CancellationToken cancellationToken)
    {
        var view = await _dashboard.GetHostAsync(id, cancellationToken);
        if (view is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            Fail(StatusCodes.Status422UnprocessableEntity, "Some of the numbers are not whole numbers. No authenticator code was used.");
            return await RenderAsync(id, cancellationToken);
        }

        var policy = Input.TryBuild(view.Policy.Effective, out var missing);
        if (policy is null)
        {
            Fail(StatusCodes.Status422UnprocessableEntity, "The limits are incomplete. No authenticator code was used.", missing);
            return await RenderAsync(id, cancellationToken);
        }

        var ranges = PolicyValidator.Validate(policy);
        if (ranges.Count > 0)
        {
            Fail(StatusCodes.Status422UnprocessableEntity, "The limits are outside the allowed ranges. No authenticator code was used.", ranges);
            return await RenderAsync(id, cancellationToken);
        }

        if (!await AuthorizeAsync(cancellationToken))
        {
            return await RenderAsync(id, cancellationToken);
        }

        if (!await TryAsync(() => _policies.SetHostOverrideAsync(id, policy, Actor, cancellationToken)))
        {
            return await RenderAsync(id, cancellationToken);
        }

        // The override is stored; pushing it needs the OET API. A failure there is reported, never hidden.
        var host = await _hostStore.GetAsync(id, cancellationToken);
        if (host?.ApiNodeId is not null)
        {
            try
            {
                await _policies.PushAsync(host, cancellationToken);
            }
            catch (FleetApiException)
            {
                return Done(HostPath(id), "limits-saved-not-pushed");
            }
        }

        return Done(HostPath(id), "limits-saved");
    }

    public Task<IActionResult> OnPostClearLimitsAsync(string id, CancellationToken cancellationToken) =>
        ActAsync(
            id,
            async () =>
            {
                await _policies.ClearHostOverrideAsync(id, Actor, cancellationToken);
                var host = await _hostStore.GetAsync(id, cancellationToken);
                if (host?.ApiNodeId is not null)
                {
                    await _policies.PushAsync(host, cancellationToken);
                }
            },
            "limits-cleared",
            cancellationToken);

    // ---- host key re-pin ---------------------------------------------------------------------

    /// <summary>Step 1 of a re-pin: fetch the fingerprints the helper offers now. Display only; nothing is trusted yet, so no step-up.</summary>
    public async Task<IActionResult> OnPostRepinStartAsync(string id, CancellationToken cancellationToken)
    {
        IReadOnlyList<HostKeyCandidateView>? candidates = null;
        if (await TryAsync(async () => candidates = await _security.BeginRepinAsync(id, Actor, cancellationToken)))
        {
            RepinCandidates = candidates;
        }

        return await RenderAsync(id, cancellationToken);
    }

    /// <summary>Step 2: you compared the fingerprint with the provider console out of band and typed its first 8 characters.</summary>
    public async Task<IActionResult> OnPostRepinConfirmAsync(string id, CancellationToken cancellationToken)
    {
        var typed = (Fingerprint ?? string.Empty).Trim();
        if (typed.Length is < 8 or > 80)
        {
            Fail(StatusCodes.Status422UnprocessableEntity, "Type the first 8 characters of the fingerprint (or all of it). No authenticator code was used.");
            return await RenderAsync(id, cancellationToken);
        }

        return await ActAsync(id, () => _security.ConfirmRepinAsync(id, typed, Actor, cancellationToken), "repin-done", cancellationToken);
    }

    // ---- plumbing ----------------------------------------------------------------------------

    private static string HostPath(string id) => "/Hosts/Detail/" + Escape(id);

    private async Task<IActionResult> ActAsync(string id, Func<Task> action, string notice, CancellationToken cancellationToken)
    {
        if (await AuthorizeAsync(cancellationToken) && await TryAsync(action))
        {
            return Done(HostPath(id), notice);
        }

        return await RenderAsync(id, cancellationToken);
    }

    private async Task<IActionResult> RenderAsync(string id, CancellationToken cancellationToken)
    {
        var view = await _dashboard.GetHostAsync(id, cancellationToken);
        if (view is null)
        {
            return NotFound();
        }

        Detail = view;
        if (Input.MaxConcurrency is null && Input.Kinds.Count == 0)
        {
            Input = PolicyInput.From(view.Policy.Effective, view.RegistryKinds);
        }

        return Page();
    }
}
