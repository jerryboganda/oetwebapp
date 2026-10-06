using System.Globalization;
using Fleet.Core.Validation;
using Fleet.Manager.Api;
using Fleet.Manager.Auth;
using Fleet.Manager.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fleet.Manager.Pages;

/// <summary>
/// The common behaviour of every console page. Reading needs the owner session (the fallback authorization policy) and nothing else.
/// Every POST needs the antiforgery token (Razor Pages validate it) and, for the privileged actions of OET-RWP/1 section 8.8, a fresh
/// authenticator code that is checked by the SAME replay-guarded step-up the JSON API uses: a code is accepted once. The application
/// services' exceptions become a status code and a message on the page; a success redirects (post/redirect/get) with a notice code
/// from a fixed table, so no text of the request is ever reflected back.
/// </summary>
public abstract class FleetPageModel : PageModel
{
    private static readonly IReadOnlyDictionary<string, string> Notices = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["added"] = "Helper added. The enrollment continues below and keeps running if you close this page.",
        ["added-key-saved"] = "Helper added and the SSH key saved (encrypted, erased after 60 minutes). Compare the host-key fingerprint below to continue.",
        ["added-key-rejected"] = "Helper added, but the SSH key could not be used. Provide it again below.",
        ["already-added"] = "That helper is already in the fleet. Showing its enrollment.",
        ["already-added-key-dropped"] = "That helper is already in the fleet, so the SSH key you pasted was not saved. Showing its enrollment: provide the key there if it asks for one.",
        ["host-key-confirmed"] = "Host key pinned. Provide the temporary SSH key to continue.",
        ["host-key-confirmed-continuing"] = "Host key pinned. The server is being prepared with the saved key.",
        ["key-saved"] = "SSH key saved. Preparing the server starts as soon as the host key is confirmed.",
        ["key-submitted"] = "SSH key accepted. The server is being prepared.",
        ["key-continued"] = "Continuing with the saved SSH key. The server is being prepared.",
        ["retried"] = "Retrying from the step that failed.",
        ["cancelled"] = "Operation cancelled. Any temporary SSH key was erased.",
        ["drain-requested"] = "Drain requested: the helper takes no new work and finishes what it is running.",
        ["resume-requested"] = "Resume requested.",
        ["disable-requested"] = "Disable requested.",
        ["rotate-requested"] = "Token rotation started: a new token is issued, installed and verified before the old one expires.",
        ["repair-requested"] = "Repair started. Provide a temporary SSH key to continue.",
        ["remove-requested"] = "Removal started: the helper is drained first, then only fleet-owned components are removed.",
        ["limits-saved"] = "Limits saved for this helper and pushed to the OET API.",
        ["limits-saved-not-pushed"] = "Limits saved here, but the OET API could not take them yet. Check the API on the Projects page; saving the global policy pushes every helper again.",
        ["limits-cleared"] = "This helper follows the global policy again.",
        ["policy-saved"] = "Policy saved. Pushed to {0} helper(s); {1} could not be updated (see the table below).",
        ["repin-started"] = "The fingerprints the helper offers now are shown below.",
        ["repin-done"] = "Host key re-pinned and the alert cleared. Resume the helper when you are ready.",
        ["release-approved"] = "Release approved and pushed to the helpers as their approved-image window.",
        ["rollout-started"] = "Rolling update started, one helper at a time.",
        ["owner-key-revoked"] = "The temporary SSH key was erased.",
        ["owner-key-none"] = "There was no stored SSH key to erase.",
        ["pull-token-discarded"] = "The registry pull token was discarded.",
        ["refreshed"] = "Fleet state refreshed from the OET API.",
        ["refresh-busy"] = "A refresh is already running.",
    };

    /// <summary>Appended to a refused key form: a secret is write-only, so a form that comes back with a problem comes back without it.</summary>
    protected const string KeyNotEchoed = " The key and its passphrase are never sent back to the browser: paste them again.";

    private readonly OwnerAccountService _owner;

    protected FleetPageModel(OwnerAccountService owner)
    {
        _owner = owner;
    }

    /// <summary>The authenticator code of a privileged action. It is cleared as soon as it has been checked and is never rendered back.</summary>
    [BindProperty]
    public string? Code { get; set; }

    /// <summary>A success notice (from the fixed table), shown after a redirect.</summary>
    public string? Notice { get; private set; }

    /// <summary>What went wrong with the last POST, in words (no secret, nothing reflected from the request).</summary>
    public string? Error { get; private set; }

    public IReadOnlyList<ValidationIssue> Issues { get; private set; } = Array.Empty<ValidationIssue>();

    protected string Actor => User.Identity?.Name ?? "owner";

    /// <summary>Reads <c>?notice=code&amp;n=3&amp;f=0</c>: only a code in the table is ever shown, and the two counters are clamped integers.</summary>
    protected void LoadNotice()
    {
        if (!Request.Query.TryGetValue("notice", out var code) || !Notices.TryGetValue(code.ToString(), out var template))
        {
            return;
        }

        Notice = string.Format(CultureInfo.InvariantCulture, template, QueryCount("n"), QueryCount("f"));
    }

    private int QueryCount(string name) =>
        int.TryParse(Request.Query[name].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? Math.Min(value, 999) : 0;

    /// <summary>Sets the response status and the message the page shows. Never pass text that came from the request.</summary>
    protected void Fail(int status, string message, IReadOnlyList<ValidationIssue>? issues = null)
    {
        Response.StatusCode = status;
        Error = message;
        Issues = issues ?? Array.Empty<ValidationIssue>();
    }

    /// <summary>The TOTP step-up (OET-RWP/1 section 8.8). False after setting a 403 message; the code is spent only when it was accepted or wrong, never reused.</summary>
    protected async Task<bool> AuthorizeAsync(CancellationToken cancellationToken)
    {
        var code = Code;
        Code = null;
        ModelState.Remove(nameof(Code));
        if (await _owner.VerifyStepUpAsync(code, cancellationToken))
        {
            return true;
        }

        Fail(
            StatusCodes.Status403Forbidden,
            "That authenticator code was not accepted. Enter the code your app shows now. Each code works once, so wait for the next one if you just used it.");
        return false;
    }

    /// <summary>Runs an application action, turning its known exceptions into a status and a message. True when it succeeded.</summary>
    protected async Task<bool> TryAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (FleetValidationException ex)
        {
            Fail(StatusCodes.Status422UnprocessableEntity, "The request is not valid.", ex.Issues);
        }
        catch (FleetOperationException ex)
        {
            Fail(StatusCodes.Status409Conflict, ex.Message);
        }
        catch (FleetNotFoundException ex)
        {
            Fail(StatusCodes.Status404NotFound, ex.Message);
        }
        catch (FleetApiException)
        {
            Fail(StatusCodes.Status502BadGateway, "The OET API refused or could not complete the request. Check the API status on the Projects page, then try again.");
        }

        return false;
    }

    /// <summary>Post/redirect/get to a local path with a notice code (and optional counters).</summary>
    protected IActionResult Done(string path, string notice, int n = 0, int f = 0)
    {
        // The path may already carry a query (for example /Operations?view=releases).
        var query = (path.Contains('?', StringComparison.Ordinal) ? "&notice=" : "?notice=") + notice;
        if (n > 0 || f > 0)
        {
            query += "&n=" + n.ToString(CultureInfo.InvariantCulture) + "&f=" + f.ToString(CultureInfo.InvariantCulture);
        }

        return LocalRedirect(path + query);
    }

    protected static string Escape(string value) => Uri.EscapeDataString(value);
}
