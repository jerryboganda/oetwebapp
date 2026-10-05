using Fleet.Manager.Cli;
using Fleet.Manager.Endpoints;
using Fleet.Manager.Hosting;

// Command-line mode (owner-init, healthcheck, sync-stdin, vault-rewrap, verify-chain). Anything else starts the web host.
var cliExitCode = await FleetCli.TryRunAsync(args);
if (cliExitCode is int exitCode)
{
    return exitCode;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFleetManager(builder.Configuration);
builder.Services.AddFleetWeb();

// The listen address policy (loopback only, or the container wildcard published on the host's 127.0.0.1) is enforced by StartupChecks.
if (string.IsNullOrEmpty(builder.Configuration["urls"]) && string.IsNullOrEmpty(builder.Configuration["ASPNETCORE_URLS"]))
{
    builder.WebHost.UseUrls(builder.Configuration["Fleet:Binding:Urls"] ?? "http://127.0.0.1:8080");
}

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new { code = "internal_error" });
}));

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["Content-Security-Policy"] =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";
    headers["X-Frame-Options"] = "DENY";
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    headers["Cache-Control"] = "no-store";
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapFleetEndpoints();

await app.RunAsync();
return 0;

/// <summary>Public so WebApplicationFactory-based tests can host the real application.</summary>
public partial class Program
{
}
