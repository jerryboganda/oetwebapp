using Fleet.Core.Validation;
using Fleet.Manager.Auth;
using Fleet.Manager.Dashboard;
using Fleet.Manager.Operations;
using Microsoft.AspNetCore.Mvc;

namespace Fleet.Manager.Pages;

/// <summary>
/// Credentials, write-only: what is stored (a fingerprint hint and timestamps, never a value) and the actions on it. Add or replace the temporary
/// owner SSH key of an enrollment or repair, erase it early, rotate a node token, discard the registry pull token. The long-lived service secrets
/// are files mounted into the container: this page says whether each is present and never reads one out.
/// </summary>
public sealed class CredentialsModel : FleetPageModel
{
    private readonly DashboardService _dashboard;
    private readonly CredentialService _credentials;
    private readonly EnrollmentService _enrollment;
    private readonly HostService _hosts;

    public CredentialsModel(
        OwnerAccountService owner,
        DashboardService dashboard,
        CredentialService credentials,
        EnrollmentService enrollment,
        HostService hosts)
        : base(owner)
    {
        _dashboard = dashboard;
        _credentials = credentials;
        _enrollment = enrollment;
        _hosts = hosts;
    }

    public CredentialsView Data { get; private set; } = null!;

    /// <summary>The enrollment or repair the key is for.</summary>
    [BindProperty]
    public string? OperationId { get; set; }

    [BindProperty]
    public string? SshUser { get; set; } = "root";

    [BindProperty]
    public string? PrivateKey { get; set; }

    [BindProperty]
    public string? Passphrase { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        LoadNotice();
        Data = await _dashboard.GetCredentialsAsync(cancellationToken);
    }

    /// <summary>Add (or replace) the temporary owner SSH key of an operation that needs one.</summary>
    public async Task<IActionResult> OnPostAddKeyAsync(CancellationToken cancellationToken)
    {
        var key = PrivateKey;
        var passphrase = Passphrase;
        PrivateKey = null;
        Passphrase = null;
        var operationId = OperationId?.Trim();
        var user = string.IsNullOrWhiteSpace(SshUser) ? "root" : SshUser.Trim();
        var hasPassphrase = !string.IsNullOrEmpty(passphrase);

        var issues = new List<ValidationIssue>();
        if (string.IsNullOrEmpty(operationId))
        {
            issues.Add(new ValidationIssue("operationId", "operation_required", "Choose the operation the key is for."));
        }

        var userIssue = InputValidator.ValidateSshUser(user);
        if (userIssue is not null)
        {
            issues.Add(userIssue);
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            issues.Add(new ValidationIssue("privateKey", "owner_key_invalid", "Paste the private key (or choose its file)."));
        }
        else if (!OwnerKeyText.TryNormalize(key, out _, allowEncrypted: hasPassphrase))
        {
            issues.Add(new ValidationIssue(
                "privateKey",
                "owner_key_invalid",
                hasPassphrase ? "Paste an OpenSSH or PEM private key." : "Paste an unencrypted OpenSSH or PEM private key (or enter its passphrase)."));
        }

        if (hasPassphrase && !OwnerKeyText.IsAcceptablePassphrase(passphrase))
        {
            issues.Add(new ValidationIssue("passphrase", "passphrase_invalid", "The passphrase must be a single line of at most 256 characters."));
        }

        if (issues.Count > 0)
        {
            Fail(StatusCodes.Status422UnprocessableEntity, "The key form has problems. No authenticator code was used." + (string.IsNullOrWhiteSpace(key) ? string.Empty : KeyNotEchoed), issues);
            return await RenderAsync(cancellationToken);
        }

        OperationView? result = null;
        if (!await AuthorizeAsync(cancellationToken)
            || !await TryAsync(async () => result = await _enrollment.SetOwnerCredentialAsync(operationId!, user, key, passphrase, Actor, cancellationToken)))
        {
            return await RenderAsync(cancellationToken);
        }

        var waiting = result!.State is "Created" or "HostKeyPending";
        return Done("/Operations/Detail/" + Escape(result.Id), waiting ? "key-saved" : "key-submitted");
    }

    /// <summary>Erase a stored temporary owner key now (it is erased after 60 minutes, at the end of the bootstrap and on cancel anyway).</summary>
    public async Task<IActionResult> OnPostRevokeKeyAsync(string? hostId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(hostId, out _))
        {
            Fail(StatusCodes.Status422UnprocessableEntity, "That is not a helper. No authenticator code was used.");
            return await RenderAsync(cancellationToken);
        }

        var removed = false;
        if (!await AuthorizeAsync(cancellationToken)
            || !await TryAsync(async () => removed = await _credentials.RevokeOwnerKeyAsync(hostId!, Actor, cancellationToken)))
        {
            return await RenderAsync(cancellationToken);
        }

        return Done("/Credentials", removed ? "owner-key-revoked" : "owner-key-none");
    }

    /// <summary>Issue, install and verify a new node token (the old one stays valid for a grace period, then expires).</summary>
    public async Task<IActionResult> OnPostRotateTokenAsync(string? hostId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(hostId, out _))
        {
            Fail(StatusCodes.Status422UnprocessableEntity, "That is not a helper. No authenticator code was used.");
            return await RenderAsync(cancellationToken);
        }

        OperationView? operation = null;
        if (!await AuthorizeAsync(cancellationToken)
            || !await TryAsync(async () => operation = await _hosts.StartRotateTokenAsync(hostId!, Actor, cancellationToken)))
        {
            return await RenderAsync(cancellationToken);
        }

        return Done("/Operations/Detail/" + Escape(operation!.Id), "rotate-requested");
    }

    /// <summary>Forget the registry pull token held in memory. The next CI sync supplies a new one.</summary>
    public async Task<IActionResult> OnPostDiscardPullTokenAsync(CancellationToken cancellationToken)
    {
        if (!await AuthorizeAsync(cancellationToken)
            || !await TryAsync(() => _credentials.DiscardPullTokenAsync(Actor, cancellationToken)))
        {
            return await RenderAsync(cancellationToken);
        }

        return Done("/Credentials", "pull-token-discarded");
    }

    private async Task<IActionResult> RenderAsync(CancellationToken cancellationToken)
    {
        Data = await _dashboard.GetCredentialsAsync(cancellationToken);
        return Page();
    }
}
