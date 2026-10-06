using Fleet.Core.Validation;
using Fleet.Manager.Auth;
using Fleet.Manager.Dashboard;
using Fleet.Manager.Operations;
using Microsoft.AspNetCore.Mvc;

namespace Fleet.Manager.Pages.Operations;

/// <summary>
/// One operation, with live progress that survives closing the browser (the operation is durable on the server and this page is just a view of
/// it). It is also where the owner acts on an enrollment: checks the host key, provides or replaces the temporary SSH key, retries or cancels.
/// </summary>
public sealed class DetailModel : FleetPageModel
{
    private readonly DashboardService _dashboard;
    private readonly EnrollmentService _enrollment;

    public DetailModel(OwnerAccountService owner, DashboardService dashboard, EnrollmentService enrollment)
        : base(owner)
    {
        _dashboard = dashboard;
        _enrollment = enrollment;
    }

    public OperationPageView Detail { get; private set; } = null!;

    /// <summary>The first characters of the fingerprint, typed after comparing it with the provider console.</summary>
    [BindProperty]
    public string? Fingerprint { get; set; }

    [BindProperty]
    public string? SshUser { get; set; } = "root";

    [BindProperty]
    public string? PrivateKey { get; set; }

    [BindProperty]
    public string? Passphrase { get; set; }

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken)
    {
        LoadNotice();
        return await RenderAsync(id, cancellationToken);
    }

    public async Task<IActionResult> OnGetFragmentAsync(string id, CancellationToken cancellationToken)
    {
        var view = await _dashboard.GetOperationAsync(id, cancellationToken);
        if (view is null)
        {
            return NotFound();
        }

        return Partial("_OperationLive", view);
    }

    /// <summary>You compared the fingerprint with the provider console and typed its first 8 characters. Only then is the key pinned.</summary>
    public async Task<IActionResult> OnPostConfirmKeyAsync(string id, CancellationToken cancellationToken)
    {
        var typed = (Fingerprint ?? string.Empty).Trim();
        Fingerprint = null;
        if (typed.Length is < 8 or > 80)
        {
            Fail(StatusCodes.Status422UnprocessableEntity, "Type the first 8 characters of the fingerprint (the part after SHA256:). No authenticator code was used.");
            return await RenderAsync(id, cancellationToken);
        }

        OperationView? result = null;
        if (!await AuthorizeAsync(cancellationToken)
            || !await TryAsync(async () => result = await _enrollment.ConfirmHostKeyAsync(id, typed, Actor, cancellationToken)))
        {
            return await RenderAsync(id, cancellationToken);
        }

        // With a key saved in advance the bootstrap has already started.
        return Done(PathOf(id), result!.State == "Bootstrapping" ? "host-key-confirmed-continuing" : "host-key-confirmed");
    }

    /// <summary>Provides (or replaces) the temporary SSH key. Before the host-key check it is saved for later; when the operation is waiting for it, it starts the bootstrap.</summary>
    public async Task<IActionResult> OnPostKeyAsync(string id, CancellationToken cancellationToken)
    {
        var key = PrivateKey;
        var passphrase = Passphrase;
        PrivateKey = null;
        Passphrase = null;
        var user = string.IsNullOrWhiteSpace(SshUser) ? "root" : SshUser.Trim();
        var hasPassphrase = !string.IsNullOrEmpty(passphrase);

        var issues = new List<ValidationIssue>();
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
            return await RenderAsync(id, cancellationToken);
        }

        OperationView? result = null;
        if (!await AuthorizeAsync(cancellationToken)
            || !await TryAsync(async () => result = await _enrollment.SetOwnerCredentialAsync(id, user, key, passphrase, Actor, cancellationToken)))
        {
            return await RenderAsync(id, cancellationToken);
        }

        var waiting = result!.State is "Created" or "HostKeyPending";
        return Done(PathOf(id), waiting ? "key-saved" : "key-submitted");
    }

    /// <summary>Starts bootstrapping with the key that was saved in advance.</summary>
    public async Task<IActionResult> OnPostContinueAsync(string id, CancellationToken cancellationToken)
    {
        if (!await AuthorizeAsync(cancellationToken)
            || !await TryAsync(() => _enrollment.ContinueWithSavedCredentialAsync(id, Actor, cancellationToken)))
        {
            return await RenderAsync(id, cancellationToken);
        }

        return Done(PathOf(id), "key-continued");
    }

    /// <summary>Retry from the step that failed (never from the start). Not a privileged action in OET-RWP/1, so no step-up.</summary>
    public async Task<IActionResult> OnPostRetryAsync(string id, CancellationToken cancellationToken)
    {
        if (!await TryAsync(() => _enrollment.RetryAsync(id, Actor, cancellationToken)))
        {
            return await RenderAsync(id, cancellationToken);
        }

        return Done(PathOf(id), "retried");
    }

    /// <summary>Abandons the operation; any stored temporary key is erased.</summary>
    public async Task<IActionResult> OnPostCancelAsync(string id, CancellationToken cancellationToken)
    {
        if (!await TryAsync(() => _enrollment.CancelAsync(id, Actor, cancellationToken)))
        {
            return await RenderAsync(id, cancellationToken);
        }

        return Done(PathOf(id), "cancelled");
    }

    private static string PathOf(string id) => "/Operations/Detail/" + Escape(id);

    private async Task<IActionResult> RenderAsync(string id, CancellationToken cancellationToken)
    {
        var view = await _dashboard.GetOperationAsync(id, cancellationToken);
        if (view is null)
        {
            return NotFound();
        }

        Detail = view;
        return Page();
    }
}
