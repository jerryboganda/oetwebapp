using System.Security.Claims;
using Fleet.Manager.Auth;
using Fleet.Manager.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Fleet.Manager.Pages;

/// <summary>
/// Password plus TOTP in one step. The failure message never says which factor was wrong, the page
/// never echoes what was typed, and the endpoint is rate limited per source on top of the account lockout.
/// </summary>
[EnableRateLimiting("login")]
public sealed class LoginModel : PageModel
{
    private readonly OwnerAccountService _owner;
    private readonly TimeProvider _time;

    public LoginModel(OwnerAccountService owner, TimeProvider time)
    {
        _owner = owner;
        _time = time;
    }

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty]
    public string? Code { get; set; }

    public string? Error { get; private set; }

    public IActionResult OnGet() =>
        User.Identity?.IsAuthenticated == true ? RedirectToPage("/Health") : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var source = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await _owner.LoginAsync(Password, Code, source, cancellationToken);
        Password = null;
        Code = null;
        ModelState.Clear();

        if (!result.Success)
        {
            Error = result.Locked ? "Too many attempts. Try again later." : "Sign-in failed.";
            Response.StatusCode = result.Locked ? StatusCodes.Status429TooManyRequests : StatusCodes.Status401Unauthorized;
            return Page();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "owner"),
            new(ServiceRegistration.SessionStartClaim, _time.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("fleet:sid", Guid.NewGuid().ToString("N")),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
        return RedirectToPage("/Health");
    }
}
