using Fleet.Core.Validation;
using Fleet.Manager.Auth;
using Fleet.Manager.Dashboard;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace Fleet.Manager.Pages.Hosts;

/// <summary>
/// Add Helper VPS. One form takes the address (an IP or a host name), an optional name, the SSH user (default root), the port (default 22),
/// optional region and provider, and the temporary SSH key (pasted or read from a file in the browser) with its optional passphrase.
/// Everything is validated BEFORE the authenticator code is checked, so a typo never burns a code. The key is saved encrypted and is used
/// only after the owner has pinned the host key, which is the next step on the enrollment page.
/// </summary>
public sealed class AddModel : FleetPageModel
{
    private readonly EnrollmentService _enrollment;
    private readonly HostStore _hostStore;
    private readonly AddressGuard _guard;

    public AddModel(OwnerAccountService owner, EnrollmentService enrollment, HostStore hostStore, AddressGuard guard)
        : base(owner)
    {
        _enrollment = enrollment;
        _hostStore = hostStore;
        _guard = guard;
    }

    [BindProperty]
    public string? Address { get; set; }

    [BindProperty]
    public string? Name { get; set; }

    [BindProperty]
    public string? SshUser { get; set; } = "root";

    [BindProperty]
    public int? Port { get; set; } = 22;

    [BindProperty]
    public string? Region { get; set; }

    [BindProperty]
    public string? Provider { get; set; }

    /// <summary>Advanced: a stable reference of your own. Left blank, one is derived from the name or the address.</summary>
    [BindProperty]
    public string? NodeRef { get; set; }

    [BindProperty]
    public string? PrivateKey { get; set; }

    [BindProperty]
    public string? Passphrase { get; set; }

    public void OnGet() => LoadNotice();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        // The secrets leave the bound properties at once: whatever happens next, nothing of them can be rendered back.
        var key = PrivateKey;
        var passphrase = Passphrase;
        PrivateKey = null;
        Passphrase = null;

        var address = (Address ?? string.Empty).Trim();
        var name = NullIfBlank(Name);
        var user = string.IsNullOrWhiteSpace(SshUser) ? "root" : SshUser.Trim();
        var port = Port ?? 22;
        var region = NullIfBlank(Region);
        var provider = NullIfBlank(Provider);
        var requestedRef = NullIfBlank(NodeRef);
        var hasKey = !string.IsNullOrWhiteSpace(key);
        var hasPassphrase = !string.IsNullOrEmpty(passphrase);

        // 1. Everything the services would refuse anyway, checked first, so it never costs an authenticator code.
        var issues = new List<ValidationIssue>();
        AddIssue(issues, InputValidator.ValidateSshUser(user));
        AddIssue(issues, InputValidator.ValidatePort(port));
        if (ModelState.TryGetValue(nameof(Port), out var portState) && portState.Errors.Count > 0)
        {
            // Not a number at all: without this the default of 22 would be used silently.
            issues.Add(new ValidationIssue("sshPort", "port_invalid", "sshPort must be a whole number between 1 and 65535."));
        }

        AddIssue(issues, InputValidator.ValidateOptionalLabel("region", region));
        AddIssue(issues, InputValidator.ValidateOptionalLabel("provider", provider));
        if (name is not null)
        {
            AddIssue(issues, InputValidator.ValidateDisplayName(name));
        }

        if (requestedRef is not null)
        {
            AddIssue(issues, InputValidator.ValidateNodeRef(requestedRef));
        }

        var check = await _guard.CheckAsync(address, cancellationToken);
        if (!check.Allowed)
        {
            issues.Add(new ValidationIssue("address", check.Code ?? "address_invalid", check.Message ?? "address is not allowed."));
        }

        if (hasKey && !OwnerKeyText.TryNormalize(key, out _, allowEncrypted: hasPassphrase))
        {
            issues.Add(new ValidationIssue(
                "privateKey",
                "owner_key_invalid",
                hasPassphrase ? "Paste an OpenSSH or PEM private key." : "Paste an unencrypted OpenSSH or PEM private key (or enter its passphrase below)."));
        }

        if (hasPassphrase && !hasKey)
        {
            issues.Add(new ValidationIssue("passphrase", "passphrase_without_key", "A passphrase belongs to a key: paste the key too, or leave the passphrase empty."));
        }
        else if (hasPassphrase && !OwnerKeyText.IsAcceptablePassphrase(passphrase))
        {
            issues.Add(new ValidationIssue("passphrase", "passphrase_invalid", "The passphrase must be a single line of at most 256 characters."));
        }

        if (issues.Count > 0)
        {
            Fail(StatusCodes.Status422UnprocessableEntity, "The form has problems. Correct them and submit again; no authenticator code was used.", issues);
            return Page();
        }

        // 2. The step-up (OET-RWP/1 section 8.8: enrolling is a privileged action).
        if (!await AuthorizeAsync(cancellationToken))
        {
            return Page();
        }

        // 3. A node reference: yours, or one derived from the name or the address that no other helper uses.
        var nodeRef = requestedRef;
        if (nodeRef is null)
        {
            var first = NodeRefs.Candidate(name, check.Normalized);
            for (var attempt = 1; attempt <= 30 && nodeRef is null; attempt++)
            {
                var candidate = NodeRefs.WithSuffix(first, attempt);
                var existing = await _hostStore.FindByNodeRefAsync(candidate, cancellationToken);
                var sameHost = existing is not null
                    && existing.Lifecycle != "Removed"
                    && existing.SshPort == port
                    && string.Equals(existing.Address, check.Normalized, StringComparison.Ordinal);
                if (existing is null || sameHost)
                {
                    nodeRef = candidate;
                }
            }

            if (nodeRef is null)
            {
                Fail(StatusCodes.Status409Conflict, "No free node reference could be derived from that name. Choose another name or type a node reference under Advanced.");
                return Page();
            }
        }

        AddHostResult? result = null;
        var added = await TryAsync(async () =>
        {
            result = await _enrollment.AddHostAsync(
                new AddHostRequest(nodeRef, name ?? nodeRef, address, port, region, provider),
                Actor,
                HttpContext.TraceIdentifier,
                cancellationToken);
        });
        if (!added || result is null)
        {
            return Page();
        }

        var path = "/Operations/Detail/" + Escape(result.Operation.Id);
        if (result.AlreadyExisted)
        {
            return Done(path, "already-added");
        }

        if (!hasKey)
        {
            return Done(path, "added");
        }

        // 4. Save the key for the moment the host key is pinned. The helper is added either way; a key that cannot be used is asked for again.
        var saved = false;
        try
        {
            await _enrollment.StageOwnerCredentialAsync(result.Operation.Id, user, key, passphrase, Actor, cancellationToken);
            saved = true;
        }
        catch (FleetValidationException)
        {
            // The enrollment page explains and asks for the key again.
        }
        catch (FleetOperationException)
        {
            // The operation moved on while the key was being checked: the enrollment page shows where it stands.
        }

        return Done(path, saved ? "added-key-saved" : "added-key-rejected");
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void AddIssue(List<ValidationIssue> issues, ValidationIssue? issue)
    {
        if (issue is not null)
        {
            issues.Add(issue);
        }
    }
}
