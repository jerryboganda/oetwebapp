using System.Net.NetworkInformation;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Fleet.Core.Crypto;
using Fleet.Core.Placement;
using Fleet.Core.Validation;
using Fleet.Manager.Api;
using Fleet.Manager.Auth;
using Fleet.Manager.Configuration;
using Fleet.Manager.Dashboard;
using Fleet.Manager.Infrastructure;
using Fleet.Manager.Monitoring;
using Fleet.Manager.Operations;
using Fleet.Manager.Persistence;
using Fleet.Manager.Provisioning;
using Fleet.Manager.Vault;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Hosting;

public static class ServiceRegistration
{
    public const string SessionStartClaim = "fleet:session-start";
    public const string CsrfHeaderName = "X-Fleet-Csrf";

    /// <summary>
    /// Registers everything except the web layer. Tests build a plain <see cref="ServiceCollection"/> with this
    /// and then replace the provisioner, API client, clock and delay with fakes; production calls it from Program.
    /// Nothing here reads configuration eagerly: every value is resolved through <c>IOptions</c> when first used.
    /// </summary>
    public static IServiceCollection AddFleetManager(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FleetOptions>().Bind(configuration.GetSection(FleetOptions.SectionName));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDelay, SystemDelay>();
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<OperationSignal>();
        services.AddSingleton<FleetState>();
        services.AddSingleton<IPrimaryPressureSource, ProcPressureSource>();

        // Vault: the master key is read when first needed and StartupChecks resolves it first, so a missing or short key stops the process.
        services.AddSingleton(sp => MasterKeyRing.LoadFromDirectory(sp.GetRequiredService<IOptions<FleetOptions>>().Value.Secrets.Directory));
        services.AddSingleton<VaultCipher>();

        services.AddDbContextFactory<FleetDbContext>((sp, builder) =>
        {
            var data = sp.GetRequiredService<IOptions<FleetOptions>>().Value.Data;
            Directory.CreateDirectory(data.Directory);
            var connection = new SqliteConnectionStringBuilder
            {
                DataSource = data.DatabasePath,
                ForeignKeys = true,
                Pooling = true,
                DefaultTimeout = 30,
            };
            builder.UseSqlite(connection.ToString()).AddInterceptors(new SqlitePragmaInterceptor());
        });
        services.AddSingleton<SchemaManager>();
        services.AddSingleton<IAuditService, AuditService>();
        services.AddSingleton<HostStore>();
        services.AddSingleton<OperationStore>();
        services.AddSingleton<CredentialStore>();

        services.AddSingleton<IHostResolver, DnsHostResolver>();
        services.AddSingleton(sp =>
        {
            var inventory = sp.GetRequiredService<IOptions<FleetOptions>>().Value.Inventory;
            var own = new List<string>(inventory.OwnAddresses);
            try
            {
                own.AddRange(NetworkInterface.GetAllNetworkInterfaces()
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Select(a => a.Address.ToString()));
            }
            catch (NetworkInformationException)
            {
                // Interface enumeration is best effort; the configured OwnAddresses still apply.
            }

            return new AddressGuard(sp.GetRequiredService<IHostResolver>(), own, inventory.ForbiddenAddresses);
        });

        services.AddSingleton<IApiCredentialProvider, FileApiCredentialProvider>();
        services.AddHttpClient<IFleetApi, HttpFleetApi>((sp, client) =>
            {
                var api = sp.GetRequiredService<IOptions<FleetOptions>>().Value.Api;
                client.BaseAddress = new Uri(api.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(Math.Max(5, api.TimeoutSeconds));
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // A redirect could carry the fleet-service credential to another host: never follow one.
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                ConnectTimeout = TimeSpan.FromSeconds(10),
            });

        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IProvisioner, AnsibleProvisioner>();
        services.AddSingleton<ISshKeyTool, SshKeygenTool>();

        services.AddSingleton<RolloutTokenHolder>();
        services.AddSingleton<ReleaseService>();
        services.AddSingleton<PolicyService>();
        services.AddSingleton<HostAccess>();
        services.AddSingleton<HostSecurityService>();
        services.AddSingleton<StepExecutor>();
        services.AddSingleton<OperationRunner>();
        services.AddSingleton<EnrollmentService>();
        services.AddSingleton<HostService>();

        services.AddSingleton(sp => new CapacityReservations(
            sp.GetRequiredService<TimeProvider>(),
            TimeSpan.FromSeconds(Math.Max(5, sp.GetRequiredService<IOptions<FleetOptions>>().Value.Timing.PlacementReservationSeconds))));
        services.AddSingleton(sp => new PlacementEngine(sp.GetRequiredService<CapacityReservations>()));
        services.AddSingleton<PlacementService>();

        services.AddSingleton<MetricsService>();
        services.AddSingleton<HealthReporter>();

        // The owner console's read side and its two credential actions (the Razor Pages in Pages/ call these and the services above).
        services.AddSingleton<DashboardService>();
        services.AddSingleton<CredentialService>();
        services.AddSingleton<LoginThrottle>();
        services.AddSingleton<OwnerAccountService>();

        // Startup checks first: hosted services start in registration order, and nothing else may touch the database before them.
        services.AddHostedService<StartupChecks>();
        services.AddHostedService<OperationWorker>();
        services.AddSingleton<NodeMonitor>();
        services.AddHostedService(sp => sp.GetRequiredService<NodeMonitor>());
        return services;
    }

    /// <summary>Cookie auth, antiforgery, rate limiting and Razor Pages: the owner-facing web layer.</summary>
    public static IServiceCollection AddFleetWeb(this IServiceCollection services)
    {
        // Sessions and antiforgery tokens die with the process: a restart signs the owner out (sessions are 20 minutes anyway),
        // and no key material is ever written to disk.
        services.AddDataProtection().UseEphemeralDataProtectionProvider();

        // Enums go over the wire by name ("Remote", "Draining"), never as numbers a client would have to decode.
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

        services.AddAntiforgery(options =>
        {
            options.HeaderName = CsrfHeaderName;
            options.Cookie.Name = "fleet.csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;

            // Antiforgery stamps "X-Frame-Options: SAMEORIGIN" on every response that emits a token and so overwrites the DENY the security-header
            // middleware set (OET-RWP/1 section 8.8). The middleware owns that header; framing is refused outright, same origin included.
            options.SuppressXFrameOptionsHeader = true;
        });

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<IOptions<FleetOptions>>((cookie, fleet) =>
            {
                var auth = fleet.Value.Auth;
                cookie.Cookie.Name = "fleet.session";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                cookie.Cookie.SameSite = SameSiteMode.Strict;
                cookie.Cookie.Path = "/";
                cookie.ExpireTimeSpan = TimeSpan.FromMinutes(Math.Max(1, auth.SessionIdleMinutes));
                cookie.SlidingExpiration = true;
                cookie.LoginPath = "/Login";
                cookie.AccessDeniedPath = "/Login";
                cookie.Events = new CookieAuthenticationEvents
                {
                    OnValidatePrincipal = async context =>
                    {
                        // Sliding renewal resets IssuedUtc, so the ABSOLUTE lifetime is carried in a claim set at sign-in.
                        var time = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
                        var started = context.Principal?.FindFirstValue(SessionStartClaim);
                        if (!long.TryParse(started, out var startedAt)
                            || time.GetUtcNow().ToUnixTimeSeconds() - startedAt > TimeSpan.FromMinutes(Math.Max(1, auth.SessionAbsoluteMinutes)).TotalSeconds)
                        {
                            context.RejectPrincipal();
                            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        }
                    },
                    OnRedirectToLogin = context =>
                    {
                        if (IsApiPath(context.Request.Path))
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        }
                        else
                        {
                            context.Response.Redirect(context.RedirectUri);
                        }

                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        });

        services.AddRateLimiter();
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<FleetOptions>>((limiter, fleet) =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, fleet.Value.Auth.LoginRatePerMinute),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
            limiter.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 600,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });

        services.AddRazorPages(options => options.Conventions.AllowAnonymousToPage("/Login"));
        return services;
    }

    public static bool IsApiPath(PathString path) =>
        path.StartsWithSegments("/api") || path.StartsWithSegments("/metrics") || path.StartsWithSegments("/internal");

    private static string PartitionKey(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
