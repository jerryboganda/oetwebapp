using System.Text.RegularExpressions;
using Fleet.Core.Validation;
using Fleet.Manager.Auth;
using Fleet.Manager.Dashboard;
using Fleet.Manager.Operations;
using Microsoft.AspNetCore.Mvc;

namespace Fleet.Manager.Pages.Operations;

/// <summary>
/// Operations: the history of provisioning, repair, deploy (image rollouts) and recovery, the agent releases with their approval and rollout
/// controls, and the tamper-evident audit trail. Approving a release and starting a rollout need a fresh authenticator code.
/// </summary>
public sealed class IndexModel : FleetPageModel
{
    private static readonly Regex ReleaseIdPattern = new(@"\Arel_[0-9a-f]{32}\z", RegexOptions.CultureInvariant);

    private readonly DashboardService _dashboard;
    private readonly ReleaseService _releases;
    private readonly PolicyService _policies;
    private readonly HostService _hosts;

    public IndexModel(
        OwnerAccountService owner,
        DashboardService dashboard,
        ReleaseService releases,
        PolicyService policies,
        HostService hosts)
        : base(owner)
    {
        _dashboard = dashboard;
        _releases = releases;
        _policies = policies;
        _hosts = hosts;
    }

    /// <summary><c>operations</c> (default), <c>releases</c> or <c>audit</c>.</summary>
    public string Tab { get; private set; } = "operations";

    public OperationsPageView? OperationsData { get; private set; }

    public ReleasesView? ReleasesData { get; private set; }

    public AuditPageView? AuditData { get; private set; }

    public async Task OnGetAsync(string? view, string? kind, string? state, bool verify, CancellationToken cancellationToken)
    {
        LoadNotice();
        await LoadAsync(view, kind, state, verify, cancellationToken);
    }

    /// <summary>The live part of the operations tab.</summary>
    public async Task<IActionResult> OnGetFragmentAsync(string? kind, string? state, CancellationToken cancellationToken) =>
        Partial("_OperationsTable", await _dashboard.GetOperationsAsync(kind, state, cancellationToken));

    /// <summary>Approve an agent image release so nodes may run it, and push the new approved-image window to every helper.</summary>
    public async Task<IActionResult> OnPostApproveAsync(string? releaseId, CancellationToken cancellationToken)
    {
        if (releaseId is null || !ReleaseIdPattern.IsMatch(releaseId))
        {
            Fail(StatusCodes.Status422UnprocessableEntity, "That is not a release. No authenticator code was used.");
            return await ReloadReleasesAsync(cancellationToken);
        }

        if (!await AuthorizeAsync(cancellationToken)
            || !await TryAsync(async () =>
            {
                await _releases.ApproveAsync(releaseId, Actor, cancellationToken);
                await _policies.PushAllAsync(Actor, cancellationToken);
            }))
        {
            return await ReloadReleasesAsync(cancellationToken);
        }

        return Done("/Operations?view=releases", "release-approved");
    }

    /// <summary>Roll an APPROVED digest out to the active helpers, one at a time. A failure halts it and leaves the rest untouched.</summary>
    public async Task<IActionResult> OnPostRolloutAsync(string? digest, CancellationToken cancellationToken)
    {
        if (!InputValidator.IsValidImageDigest(digest))
        {
            Fail(StatusCodes.Status422UnprocessableEntity, "That is not an image digest. No authenticator code was used.");
            return await ReloadReleasesAsync(cancellationToken);
        }

        OperationView? operation = null;
        if (!await AuthorizeAsync(cancellationToken)
            || !await TryAsync(async () => operation = await _hosts.StartRolloutAsync(digest!, Actor, cancellationToken)))
        {
            return await ReloadReleasesAsync(cancellationToken);
        }

        return Done("/Operations/Detail/" + Escape(operation!.Id), "rollout-started");
    }

    private async Task LoadAsync(string? view, string? kind, string? state, bool verify, CancellationToken cancellationToken)
    {
        Tab = view is "releases" or "audit" ? view : "operations";
        switch (Tab)
        {
            case "releases":
                ReleasesData = await _dashboard.GetReleasesAsync(cancellationToken);
                break;
            case "audit":
                AuditData = await _dashboard.GetAuditAsync(200, verify, cancellationToken);
                break;
            default:
                OperationsData = await _dashboard.GetOperationsAsync(kind, state, cancellationToken);
                break;
        }
    }

    private async Task<IActionResult> ReloadReleasesAsync(CancellationToken cancellationToken)
    {
        await LoadAsync("releases", null, null, false, cancellationToken);
        return Page();
    }
}
