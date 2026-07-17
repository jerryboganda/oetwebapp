using System.Text;
using System.Threading.RateLimiting;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Endpoints;
using OetWithDrHesham.Api.Hubs;
using OetWithDrHesham.Api.Middleware;
using OetWithDrHesham.Api.Security;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Services.LiveClasses;
using OetWithDrHesham.Api.Observability;

var builder = WebApplication.CreateBuilder(args);
// H10: wire Sentry early so host-level startup exceptions are captured. No-op unless Sentry:Dsn is set.
builder.AddSentryIfConfigured();
const string HybridDevelopmentAuthScheme = "DevelopmentOrJwt";
var authOptions = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
var authTokenOptions = builder.Configuration.GetSection(AuthTokenOptions.SectionName).Get<AuthTokenOptions>() ?? new AuthTokenOptions();
var brevoOptions = builder.Configuration.GetSection(BrevoOptions.SectionName).Get<BrevoOptions>() ?? new BrevoOptions();
var smtpOptions = builder.Configuration.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>() ?? new SmtpOptions();
var bootstrapOptions = builder.Configuration.GetSection("Bootstrap").Get<BootstrapOptions>() ?? new BootstrapOptions();
var storageOptions = builder.Configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();
var platformOptions = builder.Configuration.GetSection("Platform").Get<PlatformOptions>() ?? new PlatformOptions();
var billingOptions = builder.Configuration.GetSection("Billing").Get<BillingOptions>() ?? new BillingOptions();
var externalAuthOptions = builder.Configuration.GetSection(ExternalAuthOptions.SectionName).Get<ExternalAuthOptions>() ?? new ExternalAuthOptions();
var aiProviderOptions = builder.Configuration.GetSection(AiProviderOptions.SectionName).Get<AiProviderOptions>() ?? new AiProviderOptions();
var useDevelopmentAuth = authOptions.UseDevelopmentAuth && builder.Environment.IsDevelopment();
var enableSwagger = builder.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Features:EnableSwagger");
var trustForwardHeaders = builder.Configuration.GetValue<bool?>("Proxy:TrustForwardHeaders") ?? !builder.Environment.IsDevelopment();
var enforceHttps = builder.Configuration.GetValue<bool?>("Proxy:EnforceHttps") ?? !builder.Environment.IsDevelopment();
var externalAuthEnabled = externalAuthOptions.Google.Enabled || externalAuthOptions.Facebook.Enabled || externalAuthOptions.LinkedIn.Enabled;
var corsOrigins = (builder.Configuration["Cors:AllowedOriginsCsv"]
                   ?? (builder.Environment.IsDevelopment()
                       ? "http://localhost:3000,https://localhost:3000,http://127.0.0.1:3000,https://127.0.0.1:3000,http://localhost:3001,https://localhost:3001,http://127.0.0.1:3001,https://127.0.0.1:3001,http://localhost:3002,https://localhost:3002,http://127.0.0.1:3002,https://127.0.0.1:3002,http://localhost:3007,https://localhost:3007,http://127.0.0.1:3007,https://127.0.0.1:3007"
                       : string.Empty))
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

if (!useDevelopmentAuth)
{
    if (string.IsNullOrWhiteSpace(authTokenOptions.Issuer)
        || string.IsNullOrWhiteSpace(authTokenOptions.Audience)
        || string.IsNullOrWhiteSpace(authTokenOptions.AccessTokenSigningKey)
        || authTokenOptions.AccessTokenSigningKey.Length < AuthTokenOptions.MinimumSigningKeyLength
        || string.IsNullOrWhiteSpace(authTokenOptions.RefreshTokenSigningKey)
        || authTokenOptions.RefreshTokenSigningKey.Length < AuthTokenOptions.MinimumSigningKeyLength
        || authTokenOptions.AccessTokenLifetime <= TimeSpan.Zero
        || authTokenOptions.RefreshTokenLifetime <= TimeSpan.Zero
        || authTokenOptions.OtpLifetime <= TimeSpan.Zero
        || string.IsNullOrWhiteSpace(authTokenOptions.AuthenticatorIssuer))
    {
        throw new InvalidOperationException(
            "Configure AuthTokens:Issuer/AuthTokens:Audience/AuthTokens:AccessTokenSigningKey/AuthTokens:RefreshTokenSigningKey " +
            "(32+ chars), AuthTokens:AccessTokenLifetime/AuthTokens:RefreshTokenLifetime/AuthTokens:OtpLifetime, and AuthTokens:AuthenticatorIssuer for first-party auth.");
    }
}

if (!builder.Environment.IsDevelopment())
{
    ProductionProviderSafetyValidator.Validate(builder.Configuration, builder.Environment.IsDevelopment());

    if (brevoOptions.Enabled)
    {
        // Brevo ApiKey/from/template values are runtime-rotatable. They are
        // validated by BrevoEmailSender after IRuntimeSettingsProvider merges
        // DB overrides with env/appsettings fallbacks.
    }
    else if (!smtpOptions.Enabled
        || string.IsNullOrWhiteSpace(smtpOptions.Host)
        || smtpOptions.Port <= 0
        || !smtpOptions.EnableSsl
        || string.IsNullOrWhiteSpace(smtpOptions.FromEmail))
    {
        throw new InvalidOperationException(
            "Configure either Brevo:Enabled=true with Brevo API settings, or Smtp:Enabled=true, Smtp:Host, Smtp:Port, Smtp:EnableSsl=true, Smtp:Username, Smtp:Password, and Smtp:FromEmail outside the Development environment. For Brevo SMTP relay, use smtp-relay.brevo.com, the Brevo login, and the Brevo SMTP key.");
    }

    if (!Uri.TryCreate(platformOptions.PublicApiBaseUrl, UriKind.Absolute, out _))
    {
        throw new InvalidOperationException("Platform:PublicApiBaseUrl must be configured as an absolute URL outside the Development environment.");
    }

    if (externalAuthEnabled && !Uri.TryCreate(platformOptions.PublicWebBaseUrl, UriKind.Absolute, out _))
    {
        throw new InvalidOperationException("Platform:PublicWebBaseUrl must be configured as an absolute URL when external authentication is enabled.");
    }

    if (!Uri.TryCreate(billingOptions.CheckoutBaseUrl, UriKind.Absolute, out _))
    {
        throw new InvalidOperationException("Billing:CheckoutBaseUrl must be configured as an absolute URL outside the Development environment.");
    }

    if (billingOptions.AllowSandboxFallbacks)
    {
        throw new InvalidOperationException("Billing:AllowSandboxFallbacks must be false outside the Development environment.");
    }

    // Stripe secret/webhook/success/cancel values are runtime-rotatable. They
    // are validated by StripeGateway after IRuntimeSettingsProvider merges DB
    // overrides with env/appsettings fallbacks.
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = storageOptions.MaxUploadBytes > 0 ? storageOptions.MaxUploadBytes : 25L * 1024 * 1024;
});

builder.Services.AddDbContext<LearnerDbContext>((serviceProvider, options) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var environment = serviceProvider.GetRequiredService<IWebHostEnvironment>();
    var resolvedConnectionString = DatabaseConfiguration.ResolveConnectionString(configuration, environment.IsDevelopment());
    DatabaseConfiguration.ConfigureDbContext(options, resolvedConnectionString);
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddProblemDetails();
var dataProtectionKeyPath = Path.IsPathRooted(storageOptions.LocalRootPath)
    ? Path.Combine(storageOptions.LocalRootPath, ".data-protection")
    : Path.Combine(builder.Environment.ContentRootPath, storageOptions.LocalRootPath, ".data-protection");
Directory.CreateDirectory(dataProtectionKeyPath);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath))
    .SetApplicationName("OET with Dr Hesham");
if (enableSwagger)
{
    builder.Services.AddSwaggerGen();
    builder.Services.AddOpenApi();
}

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.Configure<AuthTokenOptions>(builder.Configuration.GetSection(AuthTokenOptions.SectionName));
builder.Services.Configure<BrevoOptions>(builder.Configuration.GetSection(BrevoOptions.SectionName));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.Configure<BootstrapOptions>(builder.Configuration.GetSection("Bootstrap"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.Configure<PlatformOptions>(builder.Configuration.GetSection("Platform"));
builder.Services.Configure<BillingOptions>(builder.Configuration.GetSection("Billing"));
builder.Services.Configure<ExternalAuthOptions>(builder.Configuration.GetSection(ExternalAuthOptions.SectionName));
builder.Services.Configure<AiProviderOptions>(builder.Configuration.GetSection(AiProviderOptions.SectionName));
builder.Services.Configure<WebPushOptions>(builder.Configuration.GetSection(WebPushOptions.SectionName));
builder.Services.Configure<NotificationProofHarnessOptions>(builder.Configuration.GetSection(NotificationProofHarnessOptions.SectionName));
builder.Services.Configure<PasswordPolicyOptions>(builder.Configuration.GetSection("PasswordPolicy"));
builder.Services.Configure<SpeakingComplianceOptions>(builder.Configuration.GetSection("Speaking:Compliance"));
builder.Services.Configure<OetWithDrHesham.Api.Configuration.LiveKitOptions>(builder.Configuration.GetSection(OetWithDrHesham.Api.Configuration.LiveKitOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
// No backplane (AddStackExchangeRedis/AddAzureSignalR) is configured — every
// Clients.Group(...)/Clients.User(...) send only reaches connections held by THIS
// process. That's safe only because scripts/deploy/rollout-release.sh and
// auto-deploy-ghcr.sh always cut over to a single active blue/green upstream
// (api-bluegreen.conf.template selects exactly one of learner-api-blue/-green at a
// time — blue and green are never both live simultaneously). If that ever changes to
// true rolling/canary traffic-splitting, or a replica count >1 is added behind a real
// load balancer, group/user sends would silently stop reaching some connections with
// no exception or log — add a Redis backplane before making that change.
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize =
        OetWithDrHesham.Api.Services.Conversation.ConversationRealtimeTransportLimits.MaximumReceiveMessageBytes;
    // Keep under Nginx Proxy Manager's default 60s proxy_read_timeout to prevent 504s
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(45);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton<IWebPushDispatcher, WebPushDispatcher>();
builder.Services.AddHttpClient<IMobilePushDispatcher, MobilePushDispatcher>();
builder.Services.AddSingleton<IPasswordHasher<ApplicationUserAccount>, PasswordHasher<ApplicationUserAccount>>();
builder.Services.AddSingleton<AuthTokenService>();
builder.Services.AddHttpClient<IExternalIdentityProviderClient, ExternalIdentityProviderClient>();
builder.Services.AddSingleton<ExternalAuthTicketService>();
if (brevoOptions.Enabled)
{
    builder.Services.AddHttpClient<BrevoEmailSender>((serviceProvider, client) =>
    {
        var configuredBrevoOptions = serviceProvider.GetRequiredService<IOptions<BrevoOptions>>().Value;
        client.BaseAddress = new Uri(string.IsNullOrWhiteSpace(configuredBrevoOptions.BaseUrl) ? "https://api.brevo.com/v3" : configuredBrevoOptions.BaseUrl);
    })
    // Transient Brevo outages must not cascade into 5xx from our API. The standard
    // resilience handler adds rate limiting, retry with exponential backoff + jitter,
    // a circuit breaker, and total + per-attempt timeouts. Defaults (3 retries, 30s
    // total timeout, 10s per attempt) are safe for transactional email and honour any
    // CancellationToken the caller passes.
    .AddStandardResilienceHandler();
    builder.Services.AddTransient<IEmailSender, BrevoEmailSender>();
}
else
{
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
}
builder.Services.AddScoped<EmailOtpService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuthService>();
// HIBP breach-check client. User-Agent is required by the HIBP API; anything
// identifying your app is acceptable. Timeout is short because breach-check
// failure is fail-open (we do not want HIBP hiccups to block sign-ups).
builder.Services.AddHttpClient(PasswordPolicyService.HibpHttpClientName, client =>
{
    var pwOptions = builder.Configuration.GetSection("PasswordPolicy").Get<PasswordPolicyOptions>() ?? new PasswordPolicyOptions();
    client.BaseAddress = new Uri(pwOptions.BreachApiBaseUrl);
    client.Timeout = pwOptions.BreachApiTimeout;
    client.DefaultRequestHeaders.Add("User-Agent", "OetWithDrHesham-PasswordPolicy");
    client.DefaultRequestHeaders.Add("Add-Padding", "true");
});
builder.Services.AddScoped<PasswordPolicyService>();
builder.Services.AddScoped<ExternalAuthService>();
builder.Services.AddSingleton<CookieBackedAuthCsrfGuard>();

if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("Frontend", policy =>
        {
            policy
                .WithOrigins(corsOrigins)
                .AllowAnyHeader()
                .WithMethods("GET", "POST", "PATCH", "PUT", "DELETE", "OPTIONS")
                .AllowCredentials();
        });
    });
}

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddRateLimiter(options =>
{
    // Per-user request ceilings (fixed 1-minute window). Raised well above the
    // old prod values (100 read / 30 write) so admins can run legitimate bulk
    // content operations (e.g. uploading the full Materials library) without
    // being throttled. Auth brute-force is protected separately by the dedicated
    // "AuthBruteforce" limiter, so this does not weaken login protection.
    var perUserPermitLimit = builder.Environment.IsDevelopment() ? 5000 : 5000;
    var perUserWritePermitLimit = builder.Environment.IsDevelopment() ? 5000 : 5000;
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsync(
            JsonSupport.Serialize(new { code = "rate_limited", message = "Too many requests. Please try again later.", retryable = true }),
            ct);
    };
    options.AddPolicy("PerUser", httpContext =>
    {
        var userId = httpContext.User.Identity?.IsAuthenticated == true
            ? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous"
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = perUserPermitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
    options.AddPolicy("PerUserWrite", httpContext =>
    {
        var userId = httpContext.User.Identity?.IsAuthenticated == true
            ? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous"
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter($"write-{userId}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = perUserWritePermitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
    // Caps SignalR hub negotiate/connect frequency per account. Unlike ordinary API
    // requests, MapHub has no other rate limiting at all — a compromised or buggy
    // client stuck in a reconnect loop could otherwise hammer the negotiate endpoint
    // unthrottled. 30/min comfortably covers legitimate reconnect storms (flaky
    // network, several tabs/devices reconnecting after a deploy cutover).
    options.AddPolicy("HubConnect", httpContext =>
    {
        var userId = httpContext.User.Identity?.IsAuthenticated == true
            ? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous"
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter($"hub-connect-{userId}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
    options.AddPolicy("AiCredentialValidate", httpContext =>
    {
        // Tight limit: this endpoint pings external providers and costs us
        // reputation + rate-limit budget if abused. See §8 of the policy.
        var key = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter($"ai-validate-{key}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
    // Tighter policy for anonymous auth endpoints to mitigate credential stuffing,
    // OTP bombing, and account-enumeration probes. Partitioned by IP because callers
    // are unauthenticated; users behind NAT share a bucket (acceptable trade-off).
    // Dev/test gets headroom so the E2E matrix doesn't false-positive.
    var authBrutePermit = builder.Environment.IsDevelopment() ? 500 : 10;
    options.AddPolicy("AuthBruteforce", httpContext =>
    {
        var key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter($"auth-brute-{key}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = authBrutePermit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
    // /v1/auth/refresh is hit by legitimate already-authenticated sessions on
    // every hard navigation (the SPA does not persist access tokens to
    // localStorage for security). Sharing the AuthBruteforce bucket with
    // sign-in/register starves real users behind NAT and trips 429s during
    // multi-tab use. Single-use refresh-token rotation already provides the
    // anti-replay guarantee, so a higher limit here is safe.
    var authRefreshPermit = builder.Environment.IsDevelopment() ? 500 : 120;
    options.AddPolicy("AuthRefresh", httpContext =>
    {
        var key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter($"auth-refresh-{key}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = authRefreshPermit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
    var devicePairingRedeemPermit = builder.Environment.IsDevelopment() ? 100 : 5;
    options.AddPolicy("DevicePairingRedeem", httpContext =>
    {
        var key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter($"device-pair-{key}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = devicePairingRedeemPermit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
    // Email-scoped OTP throttle. Applied to endpoints that accept a target email
    // in the request body (verification OTP, forgot-password). Key is derived from
    // a header the handler sets; handlers MUST call context.Items["otp_email"] = normalizedEmail
    // before RequireRateLimiting runs for this policy.
    var otpPermit = builder.Environment.IsDevelopment() ? 100 : 5;
    options.AddPolicy("AuthOtpSend", httpContext =>
    {
        var email = (httpContext.Items["otp_email"] as string)
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter($"auth-otp-{email}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = otpPermit,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0
        });
    });

    // ─── Writing Module V2 rate-limit policies (spec §27.21) ──────────────
    // The free vs paid distinction is driven by the `subscription_tier` JWT
    // claim (see the "writing-submissions" policy below): paid learners get the
    // higher budget, everyone else the free budget. Endpoints attach one policy;
    // the limiter itself picks the bucket per-request from the claim.
    static string ResolveWritingUserKey(HttpContext httpContext)
        => httpContext.User.Identity?.IsAuthenticated == true
            ? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous"
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // Writing submissions (and human-graded mocks) share one daily budget that
    // scales with the learner's plan: paid subscribers get a much higher cap than
    // free users. The plan is read from the `subscription_tier` JWT claim
    // (AuthTokenService) so the limiter needs no DB hit; an absent/empty/"free"
    // tier falls through to the free bucket (safe default). Previously the
    // endpoints were pinned to a free-only policy with NO paid bump ever wired up,
    // so paying users were wrongly throttled to the free daily cap and hit
    // "Too many requests" after only a handful of submissions.
    var writingSubmissionsFreeDay = builder.Environment.IsDevelopment() ? 500 : 5;
    var writingSubmissionsPaidDay = builder.Environment.IsDevelopment() ? 3000 : 30;
    options.AddPolicy("writing-submissions", httpContext =>
    {
        var key = ResolveWritingUserKey(httpContext);
        var tier = httpContext.User.FindFirst("subscription_tier")?.Value?.ToLowerInvariant();
        var isPaid = tier is "paid" or "premium" or "pro" or "sponsor" or "trial";
        var permit = isPaid ? writingSubmissionsPaidDay : writingSubmissionsFreeDay;
        return RateLimitPartition.GetSlidingWindowLimiter(
            $"writing-sub-{(isPaid ? "paid" : "free")}-{key}", _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permit,
                Window = TimeSpan.FromDays(1),
                SegmentsPerWindow = 24,
                QueueLimit = 0,
            });
    });

    // Coach: 1 hint per 30 seconds per session. Partition key includes the
    // sessionId pulled from the request body header (handler sets it on
    // HttpContext.Items["writing_coach_session"] before the limiter runs).
    var writingCoachPermit = builder.Environment.IsDevelopment() ? 200 : 1;
    options.AddPolicy("writing-coach", httpContext =>
    {
        var userKey = ResolveWritingUserKey(httpContext);
        var sessionKey = (httpContext.Items["writing_coach_session"] as string) ?? userKey;
        return RateLimitPartition.GetFixedWindowLimiter($"writing-coach-{userKey}-{sessionKey}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = writingCoachPermit,
            Window = TimeSpan.FromSeconds(30),
            QueueLimit = 0,
        });
    });

    var writingDrillsPermit = builder.Environment.IsDevelopment() ? 500 : 5;
    options.AddPolicy("writing-drills", httpContext =>
    {
        var key = ResolveWritingUserKey(httpContext);
        return RateLimitPartition.GetFixedWindowLimiter($"writing-drills-{key}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = writingDrillsPermit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });

    var writingOcrFreePermit = builder.Environment.IsDevelopment() ? 200 : 5;
    options.AddPolicy("writing-ocr-free", httpContext =>
    {
        var key = ResolveWritingUserKey(httpContext);
        return RateLimitPartition.GetSlidingWindowLimiter($"writing-ocr-free-{key}", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = writingOcrFreePermit,
            Window = TimeSpan.FromDays(1),
            SegmentsPerWindow = 24,
            QueueLimit = 0,
        });
    });

    var writingOcrPaidPermit = builder.Environment.IsDevelopment() ? 1000 : 30;
    options.AddPolicy("writing-ocr-paid", httpContext =>
    {
        var key = ResolveWritingUserKey(httpContext);
        return RateLimitPartition.GetSlidingWindowLimiter($"writing-ocr-paid-{key}", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = writingOcrPaidPermit,
            Window = TimeSpan.FromDays(1),
            SegmentsPerWindow = 24,
            QueueLimit = 0,
        });
    });
});

var defaultAuthScheme = useDevelopmentAuth ? HybridDevelopmentAuthScheme : JwtBearerDefaults.AuthenticationScheme;

void ConfigureJwtBearer(JwtBearerOptions options)
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = authTokenOptions.Issuer,
        ValidAudience = authTokenOptions.Audience,
        NameClaimType = ClaimTypes.NameIdentifier,
        RoleClaimType = ClaimTypes.Role,
        // L1 (security): default ClockSkew is 5 minutes. Our access tokens are
        // short-lived (~15m) and the fleet is NTP-synced, so tighten to 60s
        // to reduce the window in which a revoked/expired token is still
        // accepted.
        ClockSkew = TimeSpan.FromSeconds(60),
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authTokenOptions.AccessTokenSigningKey!))
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(accessToken)
                && (context.HttpContext.Request.Path.StartsWithSegments("/v1/notifications/hub")
                    || context.HttpContext.Request.Path.StartsWithSegments("/v1/conversations/hub")
                    || context.HttpContext.Request.Path.StartsWithSegments("/v1/mocks/live-room/hub")
                    || context.HttpContext.Request.Path.StartsWithSegments("/hubs/writing-submissions")
                    || context.HttpContext.Request.Path.StartsWithSegments("/hubs/writing-today")
                    || context.HttpContext.Request.Path.StartsWithSegments("/hubs/writing-coach")
                    || context.HttpContext.Request.Path.StartsWithSegments("/ws/writing/coach")))
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            var principal = context.Principal;
            var authAccountId = principal?.FindFirst(AuthTokenService.AuthAccountIdClaimType)?.Value;
            if (string.IsNullOrWhiteSpace(authAccountId))
            {
                context.Fail("auth_account_id_required");
                return;
            }

            await using var scope = context.HttpContext.RequestServices.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
            var accountState = await db.ApplicationUserAccounts
                .AsNoTracking()
                .Where(account => account.Id == authAccountId)
                .Select(account => new
                {
                    account.DeletedAt,
                    account.Role,
                    LearnerIsActive = account.Role != ApplicationUserRoles.Learner
                        || db.Users.Any(learner =>
                            learner.AuthAccountId == account.Id
                            && learner.AccountStatus.ToLower() == "active"),
                    LearnerAccessExpiresAt = account.Role == ApplicationUserRoles.Learner
                        ? db.Users
                            .Where(learner => learner.AuthAccountId == account.Id)
                            .Select(learner => learner.AccessExpiresAt)
                            .SingleOrDefault()
                        : null,
                    ExpertIsActive = account.Role != ApplicationUserRoles.Expert
                        || db.ExpertUsers.Any(expert =>
                            expert.AuthAccountId == account.Id
                            && expert.IsActive)
                })
                .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

            if (accountState is null)
            {
                context.Fail("account_not_found");
                return;
            }

            if (accountState.DeletedAt is not null)
            {
                context.Fail("account_deleted");
                return;
            }

            if (string.Equals(accountState.Role, ApplicationUserRoles.Learner, StringComparison.Ordinal))
            {
                if (!accountState.LearnerIsActive)
                {
                    context.Fail("account_suspended");
                    return;
                }

                if (accountState.LearnerAccessExpiresAt is { } accessExpiry && accessExpiry <= now)
                {
                    context.Fail("subscription_expired");
                }

                return;
            }

            if (string.Equals(accountState.Role, ApplicationUserRoles.Expert, StringComparison.Ordinal)
                && !accountState.ExpertIsActive)
            {
                context.Fail("account_suspended");
            }
        }
    };
}

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = defaultAuthScheme;
    options.DefaultChallengeScheme = defaultAuthScheme;
});

if (useDevelopmentAuth)
{
    authBuilder.AddPolicyScheme(HybridDevelopmentAuthScheme, HybridDevelopmentAuthScheme, options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var authorizationHeader = context.Request.Headers.Authorization.ToString();
            return !string.IsNullOrWhiteSpace(authorizationHeader)
                   && authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? JwtBearerDefaults.AuthenticationScheme
                : DevelopmentAuthHandler.SchemeName;
        };
    });
    authBuilder.AddScheme<AuthenticationSchemeOptions, DevelopmentAuthHandler>(DevelopmentAuthHandler.SchemeName, _ => { });
    authBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, ConfigureJwtBearer);
}
else
{
    authBuilder.AddJwtBearer(ConfigureJwtBearer);
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("LearnerOnly", policy => policy.RequireAuthenticatedUser().RequireRole(ApplicationUserRoles.Learner));
    options.AddPolicy("ExpertOnly", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(ApplicationUserRoles.Expert)
        .RequireClaim(AuthTokenService.IsEmailVerifiedClaimType, bool.TrueString.ToLowerInvariant()));
    options.AddPolicy("AdminOnly", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(ApplicationUserRoles.Admin)
        .RequireClaim(AuthTokenService.IsEmailVerifiedClaimType, bool.TrueString.ToLowerInvariant()));
    options.AddPolicy("SponsorOnly", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(ApplicationUserRoles.Sponsor)
        .RequireClaim(AuthTokenService.IsEmailVerifiedClaimType, bool.TrueString.ToLowerInvariant()));
    options.AddPolicy("TeachingStaffOnly", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(ApplicationUserRoles.Expert, ApplicationUserRoles.Admin)
        .RequireClaim(AuthTokenService.IsEmailVerifiedClaimType, bool.TrueString.ToLowerInvariant()));
    options.AddPolicy("RulebookReader", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(ApplicationUserRoles.Learner, ApplicationUserRoles.Expert, ApplicationUserRoles.Admin));
    options.AddPolicy("AiCaller", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(ApplicationUserRoles.Learner, ApplicationUserRoles.Expert, ApplicationUserRoles.Admin));

    // Granular admin permission policies
    options.AddPolicy("AdminContentRead", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "content:read", "system_admin")));
    options.AddPolicy("AdminContentWrite", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "content:write", "system_admin")));
    options.AddPolicy("AdminContentPublish", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "content:publish", "system_admin")));
    options.AddPolicy("AdminContentEditorReview", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "content:editor_review", "content:publish", "system_admin")));
    options.AddPolicy("AdminContentPublisherApproval", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "content:publisher_approval", "content:publish", "system_admin")));
    options.AddPolicy("AdminContentPublishRequestsRead", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "content:editor_review", "content:publisher_approval", "content:publish", "system_admin")));
    options.AddPolicy("AdminBillingRead", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "billing:read", "system_admin")));
    options.AddPolicy("AdminBillingWrite", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "billing:write", "system_admin")));

    // Billing-hardening I-7: granular billing-write policies. Each accepts
    // the specific granular permission OR the legacy billing:write superset
    // OR system_admin, preserving backward compatibility for existing admins
    // whose grants only include billing:write.
    options.AddPolicy("AdminBillingRefundWrite", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "billing:refund_write", "billing:write", "system_admin")));
    options.AddPolicy("AdminBillingCatalogWrite", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "billing:catalog_write", "billing:write", "system_admin")));
    options.AddPolicy("AdminBillingSubscriptionWrite", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "billing:subscription_write", "billing:write", "system_admin")));
    options.AddPolicy("AdminFreezeRead", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "billing:read", "users:read", "system_admin")));
    options.AddPolicy("AdminFreezeWrite", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "billing:write", "users:write", "system_admin")));
    options.AddPolicy("AdminUsersRead", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "users:read", "system_admin")));
    options.AddPolicy("AdminUsersWrite", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "users:write", "system_admin")));
    options.AddPolicy("AdminReviewOps", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "review_ops", "system_admin")));
    options.AddPolicy("AdminQualityAnalytics", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "quality_analytics", "system_admin")));
    options.AddPolicy("AdminAiConfig", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "ai_config", "system_admin")));
    options.AddPolicy("AdminFeatureFlags", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "feature_flags", "system_admin")));
    options.AddPolicy("AdminNotifications", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "notifications", "system_admin")));
    options.AddPolicy("AdminAuditLogs", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "audit_logs", "system_admin")));
    options.AddPolicy("AdminManagePermissions", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "manage_permissions", "system_admin")));
    options.AddPolicy("AdminSystemAdmin", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "system_admin")));
    options.AddPolicy("AdminLearnerRead", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "learner:read", "system_admin")));
    options.AddPolicy("AdminLearnerWrite", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "learner:write", "system_admin")));
});

builder.Services.AddScoped<LearnerService>();
builder.Services.AddScoped<MockService>();
builder.Services.AddScoped<MockBookingService>();
builder.Services.AddScoped<MockBookingRecordingService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Mocks.Results.IMockSectionResultAdapter,
    OetWithDrHesham.Api.Services.Mocks.Results.ReadingMockSectionResultAdapter>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Mocks.Results.IMockSectionResultAdapter,
    OetWithDrHesham.Api.Services.Mocks.Results.ListeningMockSectionResultAdapter>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Mocks.Results.IMockSectionResultAdapter,
    OetWithDrHesham.Api.Services.Mocks.Results.LegacyMockSectionResultAdapter>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Mocks.Results.MockSectionResultResolver>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Mocks.Results.IMockReportAggregationService,
    OetWithDrHesham.Api.Services.Mocks.Results.MockReportAggregationService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Mocks.MockReadinessTrendService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Readiness.ReadinessForecastCalculator>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Readiness.ReadinessBlockerRules>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Readiness.ReadinessComputationService>();
// Wave 2 service registrations — added by W2-A on behalf of W2-D/E/F.
// W2-A's own services:
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Mocks.MockBundleReviewStageService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Mocks.MockPassPredictionService>();
// W2-D — Speaking transcription pipeline (ASR adapter).
// Default provider is the deterministic Mock; production wiring swaps this DI line for a real ASR adapter.
// 2026-05-27 audit fix — bind both providers; the pipeline picks Whisper when
// the API key is configured (admin-panel DB override OR `Speaking:Whisper:ApiKey`
// in appsettings), otherwise falls back to the deterministic mock so dev / CI
// environments without an API key remain usable. 2026-05-28: the API key is
// now resolved via IRuntimeSettingsProvider so admins can rotate it from the
// admin panel without an app restart.
builder.Services.AddHttpClient("SpeakingWhisperClient");
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Speaking.OpenAiWhisperSpeakingProvider>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Speaking.MockSpeakingTranscriptionProvider>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Speaking.ISpeakingTranscriptionProvider>(sp =>
{
    var whisper = sp.GetRequiredService<OetWithDrHesham.Api.Services.Speaking.OpenAiWhisperSpeakingProvider>();
    return whisper.IsConfigured
        ? whisper
        : sp.GetRequiredService<OetWithDrHesham.Api.Services.Speaking.MockSpeakingTranscriptionProvider>();
});
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Speaking.SpeakingTranscriptionPipeline>();
// RULE_40 tone assessor — consumed by SpeakingTranscriptionEndpoints below.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Speaking.ISpeakingToneAssessor,
    OetWithDrHesham.Api.Services.Speaking.SpeakingToneAssessor>();
// W2-E — Speaking + Writing AI pre-analysis services.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Speaking.SpeakingPreAnalysisService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.WritingPreScoreService>();
// W2-F — Speaking expert review voice-note service.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Speaking.SpeakingReviewVoiceNoteService>();
// Phase 2 (B.3) — typed Speaking session lifecycle service.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Speaking.SpeakingSessionService>();
// WS6 — Speaking result-visibility config (§10).
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Speaking.ISpeakingResultVisibilityService,
    OetWithDrHesham.Api.Services.Speaking.SpeakingResultVisibilityService>();
// Phase 7 (B.8) — Speaking compliance / consent / GDPR erasure service.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Speaking.SpeakingComplianceService>();
// Phase 2 (B.3) — AI-side speaking assessment scorer.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Speaking.SpeakingAiAssessmentService>();
// Speaking module rebuild (2026-06-11) — two-card exam orchestrator.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Speaking.SpeakingExamService>();
// Phase 6 (P6) — LiveKit gateway. When LiveKit is configured (api key
// present) the cloud adapter mints real JWTs and hits the LiveKit REST
// surface; otherwise we fall back to the stub used by dev + tests.
// Webhook signature verification in both implementations is real.
{
    var liveKitSection = builder.Configuration.GetSection(OetWithDrHesham.Api.Configuration.LiveKitOptions.SectionName);
    var liveKitOptions = liveKitSection.Get<OetWithDrHesham.Api.Configuration.LiveKitOptions>()
        ?? new OetWithDrHesham.Api.Configuration.LiveKitOptions();
    if (liveKitOptions.IsEnabled)
    {
        builder.Services.AddHttpClient("LiveKitCloud", c => { c.Timeout = TimeSpan.FromSeconds(15); });
        builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Speaking.ILiveKitGateway,
            OetWithDrHesham.Api.Services.Speaking.LiveKitCloudGateway>();
    }
    else
    {
        builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Speaking.ILiveKitGateway,
            OetWithDrHesham.Api.Services.Speaking.LiveKitGatewayStub>();
    }
}
// Phase 3 (B.4) — live-tutor room orchestration service.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Speaking.SpeakingLiveRoomService>();
// Phase 4 — tutor-side scoring + review-queue services.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Speaking.TutorAssessmentService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Speaking.TutorReviewQueueService>();
// Double-marking + senior moderation (§15.4 / §15.5).
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Speaking.SpeakingModerationService>();
// Phase 5 — drill bank service.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Speaking.SpeakingDrillService>();
// Phase 6 — analytics + course pathway + interlocutor training services.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Speaking.SpeakingAnalyticsService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Speaking.SpeakingCoursePathwayService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Speaking.InterlocutorTrainingService>();
// DI hotfix (2026-05-06): these services are consumed directly by minimal-API
// endpoint handlers but were not registered, causing ASP.NET to fall back to
// inferring them as Body parameters and aborting host startup with
// "Body was inferred but the method does not allow inferred body parameters".
builder.Services.AddScoped<RemediationPlanService>();
builder.Services.AddScoped<AdminWalletTierService>();
builder.Services.AddScoped<NativeIapService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Content.MediaAssetAccessService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Content.MaterialAccessService>();
builder.Services.AddScoped<IMockEntitlementService, MockEntitlementService>();
builder.Services.AddScoped<MockItemAnalysisService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Recalls.RecallsService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.RefundService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Rulebooks.RulebookAdminService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Rulebooks.IWritingRulebookCoverageValidator,
    OetWithDrHesham.Api.Services.Rulebooks.WritingRulebookCoverageValidator>();
builder.Services.AddScoped<SpeakingTutorCalibrationService>();
builder.Services.AddScoped<IIeltsMockEngine, IeltsMockEngine>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Entitlements.IEffectiveEntitlementResolver, OetWithDrHesham.Api.Services.Entitlements.EffectiveEntitlementResolver>();
// 15 service interfaces consumed by endpoint handlers but missing from DI;
// without these, minimal-API falls back to inferring them as Body params and
// crashes the host on startup with "Body was inferred but the method does not
// allow inferred body parameters".
builder.Services.AddScoped<OetWithDrHesham.Api.Services.IAIEscalationStatsService, OetWithDrHesham.Api.Services.AIEscalationStatsService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Content.IContentEntitlementService, OetWithDrHesham.Api.Services.Content.ContentEntitlementService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.IContentStalenessService, OetWithDrHesham.Api.Services.ContentStalenessService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.IListeningAnalyticsService, OetWithDrHesham.Api.Services.Listening.ListeningAnalyticsService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.IListeningAuthoringService, OetWithDrHesham.Api.Services.Listening.ListeningAuthoringService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.IListeningBackfillService, OetWithDrHesham.Api.Services.Listening.ListeningBackfillService>();
// Listening Part A AI-assisted data entry: Mistral OCR (PDF→Markdown) + Claude
// (structures into the canonical note-completion manifest). Drafts are staged
// for human review and approved through ListeningAuthoringService.ImportManifestAsync.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Ai.IMistralOcrClient, OetWithDrHesham.Api.Services.Ai.MistralOcrClient>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Ai.IOcrService, OetWithDrHesham.Api.Services.Ai.OcrService>();
// Singleton-safe usage recorder for direct (non-gateway) AI calls — OCR, STT,
// and the Listening Part A Claude call. Scopes internally per record.
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Ai.IDirectAiCallRecorder, OetWithDrHesham.Api.Services.Ai.DirectAiCallRecorder>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.IListeningPartAExtractionService, OetWithDrHesham.Api.Services.Listening.ListeningPartAExtractionService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.IListeningPartBCExtractionService, OetWithDrHesham.Api.Services.Listening.ListeningPartBCExtractionService>();
// Listening Part A AI marking (Claude Sonnet 4.6) — additive, non-blocking per-gap
// verdicts on top of the deterministic grade. The hosted poller is opt-in via
// `Listening:PartAAiScoring:Enabled` so it never runs in tests/CI and only marks
// when an anthropic provider + key are configured.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.IListeningPartAAiScoringService, OetWithDrHesham.Api.Services.Listening.ListeningPartAAiScoringService>();
if (builder.Configuration.GetValue<bool>("Listening:PartAAiScoring:Enabled"))
{
    builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Listening.ListeningPartAAiScoringWorker>();
}
// Listening TTS synthesis. The DI seam picks between provider implementations
// based on appsettings `Listening:TtsProvider`. Supported values:
//   "stub"        — emits silence, in-process, no creds (default in dev/CI).
//   "elevenlabs"  — real ElevenLabs synthesis (pcm_16000). Set in production.
// The full synthesis pipeline (segment concat, SHA-256, IFileStorage write,
// audit log) is provider-agnostic and lives in ListeningTtsService.
{
    var ttsProvider = (builder.Configuration["Listening:TtsProvider"] ?? "stub").Trim().ToLowerInvariant();
    switch (ttsProvider)
    {
        case "stub":
            builder.Services.AddSingleton<
                OetWithDrHesham.Api.Services.Listening.IListeningTtsSynthesisProvider,
                OetWithDrHesham.Api.Services.Listening.StubListeningTtsSynthesisProvider>();
            // Production should not run the silence stub once a real provider is
            // configured. Warn loudly so the operator sees the gap, but do NOT
            // crash — other API functionality (auth, content, scoring, etc.) is
            // unaffected and must remain available.
            if (builder.Environment.IsProduction())
            {
                Console.WriteLine(
                    "[ProductionProviderSafetyValidator] WARN: Listening:TtsProvider is 'stub'. "
                    + "Set LISTENING__TTSPROVIDER=elevenlabs before listening TTS features will function.");
            }
            break;
        case "elevenlabs":
            builder.Services.AddSingleton<
                OetWithDrHesham.Api.Services.Listening.IListeningTtsSynthesisProvider,
                OetWithDrHesham.Api.Services.Listening.ElevenLabsListeningTtsSynthesisProvider>();
            break;
        default:
            throw new InvalidOperationException(
                $"Listening:TtsProvider '{ttsProvider}' is not registered. Add the provider's "
                + "DI registration above this switch or set the value to 'stub' (dev/CI) or 'elevenlabs'.");
    }
}
builder.Services.AddScoped<
    OetWithDrHesham.Api.Services.Listening.IListeningTtsService,
    OetWithDrHesham.Api.Services.Listening.ListeningTtsService>();
// TTS background job worker (polls ListeningTtsJobs table, runs synthesise
// jobs through whichever provider is registered above).
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Listening.ListeningTtsJobWorker>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.IListeningCurriculumService, OetWithDrHesham.Api.Services.Listening.ListeningCurriculumService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.IListeningPathwayService, OetWithDrHesham.Api.Services.Listening.ListeningPathwayService>();
// ── Listening V2 — strategy + FSM + version-pinned grading + pathway + classes ──
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Listening.ListeningModePolicyResolver>();
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Listening.ListeningConfirmTokenService>();
// WS4 — admin sequence builder. Consumed by ListeningSessionService for
// per-state window resolution (null sequence ⇒ derived canonical fallback).
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.ListeningSequenceService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.ListeningSessionService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.ListeningGradingService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.ListeningPathwayProgressService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.TeacherClassService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.IListeningExpertService, OetWithDrHesham.Api.Services.Listening.ListeningExpertService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.IListeningPolicyService, OetWithDrHesham.Api.Services.Listening.ListeningPolicyService>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Listening.ListeningV2BackfillService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Reading.IReadingAnalyticsService, OetWithDrHesham.Api.Services.Reading.ReadingAnalyticsService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Reading.IReadingExtractionAi, OetWithDrHesham.Api.Services.Reading.GroundedReadingExtractionAi>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Reading.IReadingExtractionService, OetWithDrHesham.Api.Services.Reading.ReadingExtractionService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Reading.IReadingPathwayService, OetWithDrHesham.Api.Services.Reading.ReadingPathwayService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Reading.IReadingReviewService, OetWithDrHesham.Api.Services.Reading.ReadingReviewService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.IWritingPdfService, OetWithDrHesham.Api.Services.WritingPdfService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.ISpeakingPdfService, OetWithDrHesham.Api.Services.SpeakingPdfService>();
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.IInvoicePdfService, OetWithDrHesham.Api.Services.InvoicePdfService>();
// WS9 (SPK-007) — scanned/text PDF import → structured Speaking draft.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Speaking.ISpeakingContentImportService,
    OetWithDrHesham.Api.Services.Speaking.SpeakingContentImportService>();
builder.Services.AddScoped<ISpeakingEvaluationPipeline, SpeakingEvaluationPipeline>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingEvaluationPipeline, OetWithDrHesham.Api.Services.Writing.WritingEvaluationPipeline>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Speaking.SpeakingAudioRetentionWorker>();
// Speaking module rebuild (2026-06-11) — server-authoritative exam auto-advance.
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Speaking.SpeakingExamAutoAdvanceWorker>();
builder.Services.AddScoped<ExpertService>();
builder.Services.AddScoped<ExpertOnboardingService>();
builder.Services.AddScoped<ExpertMessagingService>();
builder.Services.AddScoped<ExpertCompensationService>();
builder.Services.AddScoped<AdminAlertService>();
builder.Services.AddScoped<LearnerActionsService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.UserAccessAllocationService>();
builder.Services.AddScoped<ILaunchReadinessService, LaunchReadinessService>();
builder.Services.AddScoped<SponsorService>();
builder.Services.AddScoped<ISponsorSeatPackService, SponsorSeatPackService>();
builder.Services.AddScoped<ContentHierarchyService>();
builder.Services.AddScoped<ContentDeduplicationService>();
builder.Services.AddScoped<ContentAccessService>();
builder.Services.AddScoped<ContentImportService>();
builder.Services.AddScoped<ContentSearchService>();
builder.Services.AddScoped<MediaNormalizationService>();
// Video Library (Bunny Stream) — replaces the retired VideoLessonService.
// Playback sessions are attested-native-client only; catalog is universal.
builder.Services.AddHttpClient(
    OetWithDrHesham.Api.Services.VideoLibrary.BunnyStreamClient.HttpClientName,
    c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<OetWithDrHesham.Api.Services.VideoLibrary.IBunnyStreamClient,
    OetWithDrHesham.Api.Services.VideoLibrary.BunnyStreamClient>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.VideoLibrary.IVideoEntitlementService,
    OetWithDrHesham.Api.Services.VideoLibrary.VideoEntitlementService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.VideoLibrary.IVideoAttestationService,
    OetWithDrHesham.Api.Services.VideoLibrary.VideoAttestationService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.VideoLibrary.IVideoPlaybackSessionService,
    OetWithDrHesham.Api.Services.VideoLibrary.VideoPlaybackSessionService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.VideoLibrary.VideoLibraryLearnerService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.VideoLibrary.VideoLibraryAdminService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.VideoLibrary.BunnyCollectionAdminService>();
// Leader lock — only one replica runs the encode reconciliation + challenge
// sweep. Postgres advisory lock in prod; always-leader otherwise.
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.VideoLibrary.IVideoWorkerLeaderLock>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var cs = OetWithDrHesham.Api.Data.DatabaseConfiguration.ResolveConnectionString(cfg, env.IsDevelopment());
    var isPostgres = !cs.StartsWith("InMemory:", StringComparison.OrdinalIgnoreCase)
        && cs.Contains("Host=", StringComparison.OrdinalIgnoreCase);
    return isPostgres
        ? new OetWithDrHesham.Api.Services.VideoLibrary.PostgresVideoWorkerLeaderLock(
            cs, sp.GetRequiredService<ILogger<OetWithDrHesham.Api.Services.VideoLibrary.PostgresVideoWorkerLeaderLock>>())
        : new OetWithDrHesham.Api.Services.VideoLibrary.AlwaysVideoWorkerLeaderLock();
});
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.VideoLibrary.BunnyEncodeStatusWorker>();
builder.Services.AddScoped<StrategyGuideService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Admin.UserHardDeleteService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<INotificationCampaignService, NotificationCampaignService>();
builder.Services.AddScoped<NotificationRuleEngine>();
builder.Services.AddScoped<PeerReviewService>();
builder.Services.Configure<OetWithDrHesham.Api.Configuration.SoketiOptions>(builder.Configuration.GetSection("Soketi"));
builder.Services.AddHttpClient("Soketi");
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.DevicePairing.IDevicePairingCodeService, OetWithDrHesham.Api.Services.DevicePairing.InMemoryDevicePairingCodeService>();
builder.Services.AddScoped<AnalyticsIngestionService>();
builder.Services.AddSingleton<PlatformLinkService>();
builder.Services.AddHttpClient<StripeGateway>();
builder.Services.AddHttpClient<PayPalGateway>();
// Phase 2 international gateways (UK + Gulf + Egypt). HTTP clients are typed so each gateway gets its own pool.
builder.Services.AddHttpClient<OetWithDrHesham.Api.Services.Billing.Gateways.PayTabsGateway>();
builder.Services.AddHttpClient<OetWithDrHesham.Api.Services.Billing.Gateways.PaymobGateway>();
builder.Services.AddHttpClient<OetWithDrHesham.Api.Services.Billing.Gateways.CheckoutComGateway>();
builder.Services.AddHttpClient<OetWithDrHesham.Api.Services.Billing.Gateways.EasyKashGateway>();
builder.Services.AddScoped<PaymentGatewayService>();
builder.Services.AddScoped<IPaymentGatewayProvider>(sp => sp.GetRequiredService<PaymentGatewayService>());
// Phase 1-10 international expansion services.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IGatewayRegistry, OetWithDrHesham.Api.Services.Billing.GatewayRegistry>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IRegionTaxResolver, OetWithDrHesham.Api.Services.Billing.TaxResolver>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IRegionDetector, OetWithDrHesham.Api.Services.Billing.RegionDetector>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IPriceResolver, OetWithDrHesham.Api.Services.Billing.PriceResolver>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IManualPaymentService, OetWithDrHesham.Api.Services.Billing.ManualPaymentService>();
// Scoped, not Singleton: captures LearnerDbContext. Its cache lives in the singleton IMemoryCache, so it survives the scope.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IPlanContentAvailabilityService, OetWithDrHesham.Api.Services.Billing.PlanContentAvailabilityService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IDunningCampaignService, OetWithDrHesham.Api.Services.Billing.DunningCampaignService>();
// DunningCampaignService also implements IDunningService (resolved by the BillingDunningRetry background job); register that interface too so the job doesn't throw "No service registered".
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IDunningService, OetWithDrHesham.Api.Services.Billing.DunningCampaignService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IAbandonedCartRecoveryService, OetWithDrHesham.Api.Services.Billing.AbandonedCartRecoveryService>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Billing.DunningWorker>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Billing.SubscriptionExpiryWorker>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IAffiliateService, OetWithDrHesham.Api.Services.Billing.AffiliateService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IBillingNotificationDispatcher, OetWithDrHesham.Api.Services.Billing.BillingNotificationDispatcher>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IBillingNotificationChannel, OetWithDrHesham.Api.Services.Billing.EmailBillingChannel>();
builder.Services.Configure<OetWithDrHesham.Api.Configuration.TwilioOptions>(builder.Configuration.GetSection("Twilio"));
builder.Services.Configure<OetWithDrHesham.Api.Configuration.WhatsAppOptions>(builder.Configuration.GetSection("WhatsApp"));
builder.Services.AddHttpClient<OetWithDrHesham.Api.Services.Billing.TwilioSmsChannel>();
builder.Services.AddHttpClient<OetWithDrHesham.Api.Services.Billing.WhatsAppChannel>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IBillingNotificationChannel>(sp => sp.GetRequiredService<OetWithDrHesham.Api.Services.Billing.TwilioSmsChannel>());
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IBillingNotificationChannel>(sp => sp.GetRequiredService<OetWithDrHesham.Api.Services.Billing.WhatsAppChannel>());
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.ICouponVariantApplicator, OetWithDrHesham.Api.Services.Billing.CouponVariantApplicator>();
// AI churn / usage forecast / analytics / FX / experiments.
builder.Services.Configure<OetWithDrHesham.Api.Configuration.FxOptions>(builder.Configuration.GetSection("Fx"));
builder.Services.AddHttpClient<OetWithDrHesham.Api.Services.Billing.FxRateService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IFxRateService>(sp => sp.GetRequiredService<OetWithDrHesham.Api.Services.Billing.FxRateService>());
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IPricingExperimentService, OetWithDrHesham.Api.Services.Billing.PricingExperimentService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IChurnPredictionService, OetWithDrHesham.Api.Services.Billing.ChurnPredictionService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IUsageForecastService, OetWithDrHesham.Api.Services.Billing.UsageForecastService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IAiUsageAnalyticsService, OetWithDrHesham.Api.Services.Billing.AiUsageAnalyticsService>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Billing.ChurnPredictionWorker>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Billing.UsageForecastWorker>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Billing.FxRateRefreshWorker>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IRetentionActionDispatcher, OetWithDrHesham.Api.Services.Billing.RetentionActionDispatcher>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Billing.RetentionDispatchWorker>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Billing.ExperimentConversionWorker>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IBillingMetricsService, OetWithDrHesham.Api.Services.Billing.BillingMetricsService>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Billing.BillingMetricsRollupWorker>();
builder.Services.AddScoped<WalletService>();
builder.Services.AddScoped<EngagementService>();

// ── Billing V2 — Stripe catalog, cart, checkout, subscription ──
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Billing.IStripeService, OetWithDrHesham.Api.Services.Billing.StripeService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IBillingCatalogService, OetWithDrHesham.Api.Services.Billing.BillingCatalogService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IFulfillmentService, OetWithDrHesham.Api.Services.Billing.FulfillmentService>();
// Cart / checkout / subscription / promo-code commerce services. These back the
// /v1/cart, /v1/checkout, /v1/subscriptions/me and /v1/promo-codes endpoints;
// without these registrations route-building infers the service params as a
// request body and the app fails to boot.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.ICartService, OetWithDrHesham.Api.Services.Billing.CartService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.ICheckoutService, OetWithDrHesham.Api.Services.Billing.CheckoutService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.ISubscriptionService, OetWithDrHesham.Api.Services.Billing.SubscriptionService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IPromoCodeService, OetWithDrHesham.Api.Services.Billing.PromoCodeService>();
builder.Services.AddHostedService<BackgroundJobProcessor>();

// ── Phase 1 new services ──
builder.Services.AddScoped<GamificationService>();
builder.Services.AddSingleton<ISpacedRepetitionScheduler, Sm2Scheduler>();
builder.Services.AddScoped<SpacedRepetitionService>();

// ── Study planner engine (May 2026 overhaul) ──
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Planner.IStudyPlanEntitlementResolver,
    OetWithDrHesham.Api.Services.Planner.StudyPlanEntitlementResolver>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Planner.StudyPlanTemplateSelector>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Planner.ContentPicker>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Planner.ReviewItemInjector>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Planner.IStudyPlanGenerator,
    OetWithDrHesham.Api.Services.Planner.StudyPlanGenerator>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Planner.StudyPlanTemplateSeeder>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Planner.IPlanPersonalizer,
    OetWithDrHesham.Api.Services.Planner.RuleBasedPlanPersonalizer>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Planner.StudyPlanReminderWorker>();
builder.Services.AddScoped<VocabularyService>();
// 2026-05-27 audit fix — Grammar + Remediation backend APIs (previously missing).
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Grammar.IGrammarRulebookService, OetWithDrHesham.Api.Services.Grammar.GrammarRulebookService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Remediation.IRemediationApiService, OetWithDrHesham.Api.Services.Remediation.RemediationApiService>();
builder.Services.AddScoped<VocabularyDraftService>();
builder.Services.AddScoped<VocabularyGlossService>();
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Vocabulary.IVocabularyAudioQueue,
    OetWithDrHesham.Api.Services.Vocabulary.VocabularyAudioQueue>();
// Leader lock — only one replica runs the bulk audio operations (startup resume +
// reconciliation sweep). Postgres advisory lock in prod; always-leader otherwise.
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Vocabulary.IAudioWorkerLeaderLock>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var cs = OetWithDrHesham.Api.Data.DatabaseConfiguration.ResolveConnectionString(cfg, env.IsDevelopment());
    var isPostgres = !cs.StartsWith("InMemory:", StringComparison.OrdinalIgnoreCase)
        && cs.Contains("Host=", StringComparison.OrdinalIgnoreCase);
    return isPostgres
        ? new OetWithDrHesham.Api.Services.Vocabulary.PostgresAudioWorkerLeaderLock(
            cs, sp.GetRequiredService<ILogger<OetWithDrHesham.Api.Services.Vocabulary.PostgresAudioWorkerLeaderLock>>())
        : new OetWithDrHesham.Api.Services.Vocabulary.AlwaysLeaderLock();
});
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Vocabulary.VocabularyAudioWorker>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.VoiceDesign.IVoiceDesignRegenerationService,
    OetWithDrHesham.Api.Services.VoiceDesign.VoiceDesignRegenerationService>();
builder.Services.AddScoped<AdaptiveDifficultyService>();

// ── Phase 2 new services ──
builder.Services.AddScoped<PredictionService>();
builder.Services.AddScoped<ScoringService>();
builder.Services.AddScoped<ContentGenerationService>();
builder.Services.AddScoped<ConversationService>();

// ── Conversation subsystem ────────────────────────────────────────────────
builder.Services.Configure<OetWithDrHesham.Api.Configuration.ConversationOptions>(
    builder.Configuration.GetSection(OetWithDrHesham.Api.Configuration.ConversationOptions.SectionName));
foreach (var name in new[]
{
    "ConversationAzureClient", "ConversationWhisperClient", "ConversationDeepgramClient",
    "ConversationElevenLabsClient",
})
{
    builder.Services.AddHttpClient(name, c => { c.Timeout = TimeSpan.FromMinutes(2); });
}
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Conversation.Asr.IConversationAsrProvider,
    OetWithDrHesham.Api.Services.Conversation.Asr.MockConversationAsrProvider>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Conversation.Asr.IConversationAsrProvider,
    OetWithDrHesham.Api.Services.Conversation.Asr.AzureConversationAsrProvider>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Conversation.Asr.IConversationAsrProvider,
    OetWithDrHesham.Api.Services.Conversation.Asr.WhisperConversationAsrProvider>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Conversation.Asr.IConversationAsrProvider,
    OetWithDrHesham.Api.Services.Conversation.Asr.DeepgramConversationAsrProvider>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Conversation.Asr.IConversationRealtimeAsrProvider,
    OetWithDrHesham.Api.Services.Conversation.Asr.MockConversationRealtimeAsrProvider>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Conversation.Asr.IConversationRealtimeAsrProvider,
    OetWithDrHesham.Api.Services.Conversation.Asr.ElevenLabsConversationRealtimeAsrProvider>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Conversation.Asr.IConversationAsrProviderSelector,
    OetWithDrHesham.Api.Services.Conversation.Asr.ConversationAsrProviderSelector>();

// RW-012 — admin-managed PDF / OCR provider selector. Concrete
// IPaperExtractionProvider implementations register against this hook;
// the selector resolves the active row at call time so admins can rotate
// providers via /admin/ai-providers without a redeploy.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Content.IPaperExtractionProviderSelector,
    OetWithDrHesham.Api.Services.Content.PaperExtractionProviderSelector>();

builder.Services.AddScoped<OetWithDrHesham.Api.Services.Conversation.Tts.IConversationTtsProvider,
    OetWithDrHesham.Api.Services.Conversation.Tts.ElevenLabsConversationTtsProvider>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Conversation.Tts.IConversationTtsProviderSelector,
    OetWithDrHesham.Api.Services.Conversation.Tts.ConversationTtsProviderSelector>();

builder.Services.AddScoped<OetWithDrHesham.Api.Services.Conversation.IConversationAudioService,
    OetWithDrHesham.Api.Services.Conversation.ConversationAudioService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Conversation.IConversationTranscriptExportService,
    OetWithDrHesham.Api.Services.Conversation.ConversationTranscriptExportService>();
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Conversation.IConversationOptionsProvider,
    OetWithDrHesham.Api.Services.Conversation.ConversationOptionsProvider>();
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Settings.IRuntimeSettingsProvider,
    OetWithDrHesham.Api.Services.Settings.RuntimeSettingsProvider>();
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Conversation.ConversationRealtimeTurnStore>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Conversation.IConversationEntitlementService,
    OetWithDrHesham.Api.Services.Conversation.ConversationEntitlementService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Conversation.IConversationAiOrchestrator,
    OetWithDrHesham.Api.Services.Conversation.ConversationAiOrchestrator>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Conversation.ConversationAudioRetentionWorker>();
builder.Services.AddScoped<PronunciationService>();
builder.Services.AddScoped<WritingCoachService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingLearnerPathwayService,
    OetWithDrHesham.Api.Services.Writing.WritingLearnerPathwayService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingLessonService,
    OetWithDrHesham.Api.Services.Writing.WritingLessonService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingDrillService,
    OetWithDrHesham.Api.Services.Writing.WritingDrillService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingCaseNoteDrillService,
    OetWithDrHesham.Api.Services.Writing.WritingCaseNoteDrillService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.WritingWeaknessAnalyticsService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.WritingDualAssessmentService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Expert.IExpertAssignmentNotifier,
    OetWithDrHesham.Api.Services.Expert.ExpertAssignmentNotifier>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Expert.IExpertAutoAssignmentService,
    OetWithDrHesham.Api.Services.Expert.ExpertAutoAssignmentService>();
builder.Services.Configure<OetWithDrHesham.Api.Configuration.ExpertAutoAssignmentOptions>(
    builder.Configuration.GetSection(OetWithDrHesham.Api.Configuration.ExpertAutoAssignmentOptions.SectionName));
builder.Services.AddScoped<MarketplaceService>();

// ── Pronunciation subsystem (Phase 2+) ───────────────────────────────────
builder.Services.Configure<OetWithDrHesham.Api.Configuration.PronunciationOptions>(
    builder.Configuration.GetSection(OetWithDrHesham.Api.Configuration.PronunciationOptions.SectionName));
builder.Services.AddHttpClient("PronunciationAzureClient", c =>
{
    c.Timeout = TimeSpan.FromMinutes(3);
});
builder.Services.AddHttpClient("PronunciationWhisperClient", c =>
{
    c.Timeout = TimeSpan.FromMinutes(3);
});
builder.Services.AddHttpClient("GeminiNativeClient", c =>
{
    c.Timeout = TimeSpan.FromMinutes(5);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Pronunciation.IPronunciationAsrProvider,
    OetWithDrHesham.Api.Services.Pronunciation.MockPronunciationAsrProvider>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Pronunciation.IPronunciationAsrProvider,
    OetWithDrHesham.Api.Services.Pronunciation.AzurePronunciationAsrProvider>();
// Phase 6c: register the concrete Azure provider too so the phoneme adapter
// below can take it directly (same scope; same HttpClient handler pool).
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Pronunciation.AzurePronunciationAsrProvider>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Pronunciation.IPronunciationAsrProvider,
    OetWithDrHesham.Api.Services.Pronunciation.WhisperPronunciationAsrProvider>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Pronunciation.IPronunciationAsrProvider,
    OetWithDrHesham.Api.Services.Pronunciation.GeminiPronunciationAsrProvider>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Pronunciation.IPronunciationAsrProviderSelector,
    OetWithDrHesham.Api.Services.Pronunciation.PronunciationAsrProviderSelector>();
// Phase 6c: registry-first credential resolver (singleton, 30s cache) +
// scaffolding interface + Azure adapter for phoneme scoring. The live
// grading path still routes through the ASR selector — the phoneme
// interface is currently visibility-only (Phase 6d will move grading
// to it once production traffic is verified stable).
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Pronunciation.IPronunciationCredentialResolver,
    OetWithDrHesham.Api.Services.Pronunciation.PronunciationCredentialResolver>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Pronunciation.IPronunciationPhonemeProvider,
    OetWithDrHesham.Api.Services.Pronunciation.AzurePronunciationPhonemeProvider>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Pronunciation.IPronunciationFeedbackService,
    OetWithDrHesham.Api.Services.Pronunciation.PronunciationFeedbackService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Pronunciation.IPronunciationSchedulerService,
    OetWithDrHesham.Api.Services.Pronunciation.PronunciationSchedulerService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Pronunciation.IPronunciationEntitlementService,
    OetWithDrHesham.Api.Services.Pronunciation.PronunciationEntitlementService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Pronunciation.IPronunciationAdminDraftService,
    OetWithDrHesham.Api.Services.Pronunciation.PronunciationAdminDraftService>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Pronunciation.PronunciationAudioRetentionWorker>();

// Retention sweeper for auth-layer rows (expired OTPs, revoked refresh tokens)
// that would otherwise bloat their tables and indexes forever.
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Auth.AuthDataRetentionWorker>();

// Retention sweeper for append-only event tables (analytics, audit, payment
// webhooks, notification delivery attempts). Windows are configured via the
// "DataRetention" section; defaults are conservative (see DataRetentionOptions).
builder.Services.Configure<OetWithDrHesham.Api.Configuration.DataRetentionOptions>(
    builder.Configuration.GetSection("DataRetention"));
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.DataRetentionWorker>();

// Billing-hardening I-9: tiered PII retention. Nulls the payload column on
// PaymentWebhookEvents at the PaymentWebhookPiiNullOutAge cutoff (default
// 90 days) while keeping the event metadata for forensic chain-of-custody.
// The companion DataRetentionWorker deletes the row entirely at the longer
// PaymentWebhookEvents cutoff (default 180 days).
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Billing.WebhookPiiRetentionWorker>();

// Mocks Wave 5: dispatches the 24-h / 2-h / 30-min pre-booking reminder
// notifications for upcoming MockBookings. Idempotent via the
// NotificationService dedupe-key contract; ticks every 5 min.
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Mocks.MockBookingReminderWorker>();

// Partition-maintenance worker: keeps next-month range partitions pre-created
// for candidate time-ordered tables (AnalyticsEvents, AuditEvents, AiUsageRecords).
// No-op on SQLite and no-op on a Postgres DB whose tables are not yet partitioned.
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.PartitionMaintenanceWorker>();

// OET rulebook engine + grounded AI gateway. These services are the single
// source of truth for rule enforcement and for every AI call: no code path
// invokes a model without a rulebook-grounded prompt built here.
//
// IRulebookLoader resolves to DbBackedRulebookLoader (Scoped — depends on
// LearnerDbContext) so admin-published DB overrides take precedence over the
// embedded JSON. The plain RulebookLoader is registered as a Singleton fallback
// that the DB-backed loader composes for JSON content (Tables, StateMachines,
// AssessmentCriteria, and (kind, profession) pairs without a Published row).
// All current IRulebookLoader consumers (WritingRuleEngine, SpeakingRuleEngine,
// AiGatewayService, RulebookEndpoints, VocabularyDraft/Gloss, GrammarDraft,
// PronunciationAdminDraft, BuiltInTools) are Scoped, so the lifetime change
// from Singleton → Scoped is safe.
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Rulebook.RulebookLoader>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Rulebook.IRulebookLoader,
    OetWithDrHesham.Api.Services.Rulebook.DbBackedRulebookLoader>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Rulebook.WritingRuleEngine>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Rulebook.SpeakingRuleEngine>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Rulebook.IAiUsageRecorder,
    OetWithDrHesham.Api.Services.Rulebook.AiUsageRecorder>();
builder.Services.AddHttpClient("AiOpenAiCompatible", client =>
{
    client.Timeout = TimeSpan.FromMinutes(30);
    if (!string.IsNullOrWhiteSpace(aiProviderOptions.BaseUrl))
    {
        client.BaseAddress = new Uri(aiProviderOptions.BaseUrl.TrimEnd('/') + "/");
    }
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Rulebook.IAiModelProvider,
    OetWithDrHesham.Api.Services.Rulebook.MockAiProvider>();
// Config-based OpenAI-compatible provider reads AI__* env vars directly.
// Production deployments populate these from .env.production and the same
// key is mirrored into the AiProviders row at boot (see DatabaseBootstrapper).
// The gateway prefers the first non-mock provider registered — this one
// precedes the registry-backed provider so env-var config works even before
// the DB row is populated on first boot.
//
// Dev / test fallback: when neither AI__BaseUrl nor AI__ApiKey is configured,
// skip registering this provider so the gateway can fall back to the mock
// instead of attempting an unauthenticated HTTP call. This keeps unit + e2e
// suites runnable without provisioning AI credentials, while production
// behaviour is unchanged whenever AI__* env vars are populated.
if (!string.IsNullOrWhiteSpace(aiProviderOptions.BaseUrl)
    && !string.IsNullOrWhiteSpace(aiProviderOptions.ApiKey))
{
    builder.Services.AddScoped<OetWithDrHesham.Api.Services.Rulebook.IAiModelProvider,
        OetWithDrHesham.Api.Services.Rulebook.OpenAiCompatibleProvider>();
}
builder.Services.AddMemoryCache();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.AiManagement.IAiQuotaService,
    OetWithDrHesham.Api.Services.AiManagement.AiQuotaService>();
builder.Services.AddHttpClient("AiCredentialValidator");
builder.Services.AddScoped<OetWithDrHesham.Api.Services.AiManagement.IAiCredentialVault,
    OetWithDrHesham.Api.Services.AiManagement.AiCredentialVault>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.AiManagement.IAiCredentialResolver,
    OetWithDrHesham.Api.Services.AiManagement.AiCredentialResolver>();
builder.Services.AddHttpClient("AiRegistryClient", c => c.Timeout = TimeSpan.FromMinutes(30))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Rulebook.IAiProviderRegistry,
    OetWithDrHesham.Api.Services.Rulebook.AiProviderRegistry>();
// Multi-account pool registry — siblings of AiProviderRegistry, used by
// Copilot-style providers (Phase 2 of GitHub Copilot integration). The
// registry guarantees atomic pick + counter increment via
// ExecuteUpdateAsync; see AiProviderAccountRegistry XML doc.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Rulebook.IAiProviderAccountRegistry,
    OetWithDrHesham.Api.Services.Rulebook.AiProviderAccountRegistry>();
// Phase 7: per-feature routing override resolver. Consulted by the
// gateway between explicit pins and the registry-default fallback.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Rulebook.IAiFeatureRouteResolver,
    OetWithDrHesham.Api.Services.Rulebook.AiFeatureRouteResolver>();
// Phase 4: admin connectivity probe. Bypasses gateway grounding +
// quota on purpose — see AiProviderConnectionTester XML doc.
builder.Services.AddHttpClient(nameof(OetWithDrHesham.Api.Services.Rulebook.AiProviderConnectionTester))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Rulebook.IAiProviderConnectionTester,
    OetWithDrHesham.Api.Services.Rulebook.AiProviderConnectionTester>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Rulebook.IAiModelProvider,
    OetWithDrHesham.Api.Services.Rulebook.RegistryBackedProvider>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Rulebook.IAiModelProvider,
    OetWithDrHesham.Api.Services.Rulebook.AnthropicProvider>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Rulebook.IAiModelProvider,
    OetWithDrHesham.Api.Services.Rulebook.GeminiNativeProvider>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Rulebook.IAiModelProvider,
    OetWithDrHesham.Api.Services.Rulebook.CloudflareWorkersAiProvider>();
// GitHub Copilot / Models adapter — uses the official `Azure.AI.Inference`
// typed SDK (ChatCompletionsClient + AzureKeyCredential) against the
// chat-completions endpoint at the registered base URL (default
// https://models.github.ai/inference). DB-backed credentials live in the
// AiProviders row keyed by Code="copilot"; Phase 2 will extend this to
// a multi-account pool with auto-failover. The SDK owns its own pipeline
// (retry / timeout / transport) so no IHttpClientFactory entry is needed.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Rulebook.IAiModelProvider,
    OetWithDrHesham.Api.Services.Rulebook.CopilotAiModelProvider>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.AiManagement.IAiCreditService,
    OetWithDrHesham.Api.Services.AiManagement.AiCreditService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IAiPackageCreditService,
    OetWithDrHesham.Api.Services.Billing.AiPackageCreditService>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.AiManagement.AiCreditRenewalWorker>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.AiManagement.AiAccountQuotaResetWorker>();

// Content Upload subsystem (Slice 2). IFileStorage sits in front of disk
// access. Wave 4: provider is runtime-selected via Storage:Provider.
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Content.IHtmlSanitizer,
    OetWithDrHesham.Api.Services.Content.HtmlSanitizerService>();

// Storage provider selector — "s3" activates S3CompatibleFileStorage.
var storageProvider = storageOptions?.Provider ?? "local";
if (storageProvider.Equals("s3", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Content.IFileStorage,
        OetWithDrHesham.Api.Services.Content.S3CompatibleFileStorage>();
}
else
{
    builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Content.IFileStorage,
        OetWithDrHesham.Api.Services.Content.LocalFileStorage>();
}
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Content.IUploadContentValidator,
    OetWithDrHesham.Api.Services.Content.MagicByteValidator>();
// Upload antivirus scanner. Effective provider is runtime-admin configurable
// with env/appsettings fallback. In production we REFUSE to boot unless the
// effective provider is a real scanner — see the fail-fast check below.
builder.Services.Configure<UploadScannerOptions>(builder.Configuration.GetSection("UploadScanner"));
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Content.IUploadScanner,
    OetWithDrHesham.Api.Services.Content.ClamAvUploadScanner>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Content.IChunkedUploadService,
    OetWithDrHesham.Api.Services.Content.ChunkedUploadService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Content.IContentPaperService,
    OetWithDrHesham.Api.Services.Content.ContentPaperService>();
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Content.IContentConventionParser,
    OetWithDrHesham.Api.Services.Content.ContentConventionParser>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Content.IContentBulkImportService,
    OetWithDrHesham.Api.Services.Content.ContentBulkImportService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Content.RealContentFolderImporter>();
builder.Services.Configure<PdfExtractionOptions>(
    builder.Configuration.GetSection(PdfExtractionOptions.SectionName));
builder.Services.AddHttpClient("AzureDocIntel");
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Content.PdfPigPdfTextExtractor>();
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Content.IPdfTextExtractor,
    OetWithDrHesham.Api.Services.Content.AutoPdfTextExtractor>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Content.IContentTextExtractionService,
    OetWithDrHesham.Api.Services.Content.ContentTextExtractionService>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Content.ContentTextExtractionWorker>();

// NOTE: The Writing sample seeder (WritingSampleSeeder) and the Writing V2
// content seeder (WritingV2ContentSeeder) were removed permanently — the
// platform now runs on real, admin-authored Writing content only. Auto-seeding
// re-created admin-deleted tasks on every deploy, so it has been retired.

// OET 2026 catalog seeder — startup load of 20+ BillingPlans + 7 BillingAddOns +
// matching ContentPackages from Data/Seeds/oet-2026-catalog.json. Disabled by
// default; enable with Content:Oet2026Catalog:Enabled=true. Idempotent UPSERT
// on Code.
builder.Services.Configure<Oet2026CatalogSeedOptions>(
    builder.Configuration.GetSection(Oet2026CatalogSeedOptions.SectionName));
// Register as singleton + hosted service so the admin reseed endpoint can
// resolve the same instance.
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Billing.Oet2026CatalogSeeder>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<OetWithDrHesham.Api.Services.Billing.Oet2026CatalogSeeder>());

// Wave B4 — sync canonical Stripe seed catalogue (stripe-product-catalog.v1.json)
// into BillingProducts + BillingPrices on every boot. Idempotent UPSERT on
// metadata.code; the canonical JSON also drives the StripeProductSeeder CLI so
// the local DB and the live Stripe account stay aligned.
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Billing.BillingCatalogSyncStartupTask>();
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<OetWithDrHesham.Api.Services.Billing.BillingCatalogSyncStartupTask>());

// Canonical profession taxonomy (SignupProfessionCatalog). The startup task mirrors it into the
// legacy Professions reference table on every boot — admin CRUD only mirrored its own writes, so
// seeder-born professions such as other-allied-health never propagated and the discipline filters
// that join on the reference id fell through for those learners.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Professions.IProfessionCatalogService,
    OetWithDrHesham.Api.Services.Professions.ProfessionCatalogService>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Professions.ProfessionTaxonomySyncStartupTask>();

// Add-on eligibility service — enforces three-flag rule + Tutor Book
// double-charge guard. Called from /v1/billing/quote/addon and the
// checkout session creator.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IAddonEligibilityService,
    OetWithDrHesham.Api.Services.Billing.AddonEligibilityService>();

// Add-on grant processor — applies / reverses entitlement grants after
// successful payment / refund webhook. Idempotent on (subscription_id,
// addon_version_id, payment_event_id).
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Billing.IAddonGrantProcessor,
    OetWithDrHesham.Api.Services.Billing.AddonGrantProcessor>();

// Tutor Book watermarking — stamps PDF with buyer + HMAC fingerprint so
// leaks are traceable.
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.TutorBook.ITutorBookWatermarkService,
    OetWithDrHesham.Api.Services.TutorBook.TutorBookWatermarkService>();
// Reading Authoring subsystem (Slices R1–R7).
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Reading.IReadingStructureService,
    OetWithDrHesham.Api.Services.Reading.ReadingStructureService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Reading.IReadingPolicyService,
    OetWithDrHesham.Api.Services.Reading.ReadingPolicyService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Reading.IReadingGradingService,
    OetWithDrHesham.Api.Services.Reading.ReadingGradingService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Reading.IReadingAttemptService,
    OetWithDrHesham.Api.Services.Reading.ReadingAttemptService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Reading.IReadingTutorService,
    OetWithDrHesham.Api.Services.Reading.ReadingTutorService>();
// Reading Pathway subsystem (Reading Module Pathway Plan).
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Reading.IReadingLearnerPathwayService, OetWithDrHesham.Api.Services.Reading.ReadingLearnerPathwayService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Reading.ISkillScoringService, OetWithDrHesham.Api.Services.Reading.SkillScoringService>();
// Listening Pathway subsystem (OET_LISTENING_MODULE_PATHWAY.md §5–§6 / A6+A7).
// IListeningPathwayGenerator is a pure-function service so registering it as
// a singleton avoids per-request allocation; the others touch DbContext.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.IListeningLearnerPathwayService, OetWithDrHesham.Api.Services.Listening.ListeningLearnerPathwayService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.IListeningSkillScoringService, OetWithDrHesham.Api.Services.Listening.ListeningSkillScoringService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.IListeningLearnerGradingService, OetWithDrHesham.Api.Services.Listening.ListeningLearnerGradingService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.IListeningLessonService, OetWithDrHesham.Api.Services.Listening.ListeningLessonService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.IListeningStrategyService, OetWithDrHesham.Api.Services.Listening.ListeningStrategyService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.IListeningMockService, OetWithDrHesham.Api.Services.Listening.ListeningMockService>();
// Listening dictation drill subsystem (Phase 4 of OET_LISTENING_MODULE_PATHWAY.md §14).
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.IDictationService, OetWithDrHesham.Api.Services.Listening.DictationService>();
// Listening pronunciation library — SM-2 spaced repetition (Phase 4 of §15).
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.IPronunciationService, OetWithDrHesham.Api.Services.Listening.PronunciationService>();
// Phase 3 daily plan + adaptive practice selection (§8, §10).
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.IListeningDailyPlanService, OetWithDrHesham.Api.Services.Listening.ListeningDailyPlanService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.IListeningPracticeSelectionService, OetWithDrHesham.Api.Services.Listening.ListeningPracticeSelectionService>();
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Listening.IListeningPathwayGenerator, OetWithDrHesham.Api.Services.Listening.ListeningPathwayGenerator>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Reading.IDailyPlanService, OetWithDrHesham.Api.Services.Reading.DailyPlanService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Reading.IPracticeSelectionService, OetWithDrHesham.Api.Services.Reading.PracticeSelectionService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Reading.IReviewQueueService, OetWithDrHesham.Api.Services.Reading.ReviewQueueService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Reading.IReadingVocabularyService, OetWithDrHesham.Api.Services.Reading.ReadingVocabularyService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Reading.IReadingExplanationService, OetWithDrHesham.Api.Services.Reading.ReadingExplanationService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Reading.IStreakService, OetWithDrHesham.Api.Services.Reading.StreakService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Reading.IXpService, OetWithDrHesham.Api.Services.Reading.XpService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Reading.ILessonService, OetWithDrHesham.Api.Services.Reading.LessonService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Reading.IStrategyService, OetWithDrHesham.Api.Services.Reading.StrategyService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.ListeningLearnerService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Listening.IListeningStructureService,
    OetWithDrHesham.Api.Services.Listening.ListeningStructureService>();
// Listening sample ingester (Slice E of docs/LISTENING-INGESTION-PRD.md).
// Disabled by default — operator opts in via Seed:ListeningSamples:Enabled=true.
builder.Services.Configure<OetWithDrHesham.Api.Services.Listening.ListeningSampleSeederOptions>(
    builder.Configuration.GetSection(OetWithDrHesham.Api.Services.Listening.ListeningSampleSeederOptions.SectionName));
builder.Services.Configure<OetWithDrHesham.Api.Services.Listening.ListeningStarterContentSeederOptions>(
    builder.Configuration.GetSection(OetWithDrHesham.Api.Services.Listening.ListeningStarterContentSeederOptions.SectionName));
builder.Services.AddScoped<
    OetWithDrHesham.Api.Services.Listening.IListeningStarterContentSeeder,
    OetWithDrHesham.Api.Services.Listening.ListeningStarterContentSeeder>();
builder.Services.AddScoped<
    OetWithDrHesham.Api.Services.Listening.IListeningSampleSeeder,
    OetWithDrHesham.Api.Services.Listening.ListeningSampleSeeder>();

// Mock sample seeder (Development only). On startup, ingests three fully-
// assembled draft MockBundle rows from `Project Real Content/` so the admin
// (and learner) can immediately preview the new mock wizard + flow.
// Idempotent (slug-keyed); non-fatal on failure.
builder.Services.Configure<OetWithDrHesham.Api.Services.Seeding.MockSampleSeederOptions>(
    builder.Configuration.GetSection(OetWithDrHesham.Api.Services.Seeding.MockSampleSeederOptions.SectionName));
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Seeding.MockSampleSeeder>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Reading.ReadingAttemptExpireWorker>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Listening.ListeningAttemptExpireWorker>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Content.AdminUploadCleanupWorker>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Rulebook.IAiGatewayService,
    OetWithDrHesham.Api.Services.Rulebook.AiGatewayService>();

// ── Writing Module V2 prompt templates (OET_WRITING_MODULE_PATHWAY.md §12+§13) ──
// Singleton registry so the 10 templates (coach, rewrite, scenario.generate,
// exemplar.embed, appeal, canon.detect, drill.grade, outline, paraphrase, ask)
// resolve consistently across every Writing V2 service. The registrar runs
// inline below so prompt-template misconfig fails fast at boot, not at the
// first learner-visible request — every expected feature code is probed via
// WritingPromptTemplateRegistrar.AssertAllExpectedTemplatesRegistered.
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Rulebook.IWritingPromptTemplateRegistry>(_ =>
{
    var registry = new OetWithDrHesham.Api.Services.Rulebook.WritingPromptTemplateRegistry();
    OetWithDrHesham.Api.Services.Rulebook.WritingPromptTemplateRegistrar.RegisterWritingV2Templates(registry);
    return registry;
});

// ── Phase 5 — AI Tool Calling ──
// All AI-tool services are Scoped so the registry, invoker, and executors
// share the per-request LearnerDbContext. The grant cache remains process-
// wide because IMemoryCache is a Singleton — InvalidateFeature() therefore
// fans out to every subsequent request regardless of which scope mutates
// it. (Phase 6c hardening — making the registry Singleton crashed DI
// validation because it captured Scoped IEnumerable<IAiToolExecutor>.)
builder.Services.Configure<OetWithDrHesham.Api.Services.AiTools.AiToolOptions>(
    builder.Configuration.GetSection("AiTool"));
builder.Services.AddScoped<OetWithDrHesham.Api.Services.AiTools.IAiToolRegistry,
    OetWithDrHesham.Api.Services.AiTools.AiToolRegistry>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.AiTools.IAiToolInvoker,
    OetWithDrHesham.Api.Services.AiTools.AiToolInvoker>();

// 7-tool catalog (Phase 5 v1). Deny-by-default — admins grant per feature.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.AiTools.IAiToolExecutor,
    OetWithDrHesham.Api.Services.AiTools.Tools.LookupRulebookRuleTool>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.AiTools.IAiToolExecutor,
    OetWithDrHesham.Api.Services.AiTools.Tools.LookupVocabularyTermTool>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.AiTools.IAiToolExecutor,
    OetWithDrHesham.Api.Services.AiTools.Tools.GetUserRecentAttemptsTool>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.AiTools.IAiToolExecutor,
    OetWithDrHesham.Api.Services.AiTools.Tools.SearchRecallSetTool>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.AiTools.IAiToolExecutor,
    OetWithDrHesham.Api.Services.AiTools.Tools.SaveUserNoteTool>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.AiTools.IAiToolExecutor,
    OetWithDrHesham.Api.Services.AiTools.Tools.BookmarkRecallTermTool>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.AiTools.IAiToolExecutor,
    OetWithDrHesham.Api.Services.AiTools.Tools.FetchDictionaryDefinitionTool>();

// External-network tool HTTP client — strict timeout, no auto-redirect, no
// proxy passthrough. The tool itself enforces host allowlist + max-bytes.
builder.Services.AddHttpClient(
    OetWithDrHesham.Api.Services.AiTools.Tools.FetchDictionaryDefinitionTool.HttpClientName,
    c =>
    {
        c.Timeout = TimeSpan.FromSeconds(5);
        c.DefaultRequestHeaders.Clear();
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
    });

// Catalog seeder — one-shot at startup, idempotent. Mirrors the
// WritingSampleSeeder pattern: hosted service that opens its own scope.
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.AiTools.AiToolCatalogSeederHostedService>();

// AI Assistant orchestrator (Phase A–H). Multi-role conversational agent.
builder.Services.Configure<OetWithDrHesham.Api.Services.AiAssistant.AiAssistantOptions>(
    builder.Configuration.GetSection("AiAssistant"));
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.AiAssistant.SystemPrompts.ISystemPromptProvider,
    OetWithDrHesham.Api.Services.AiAssistant.SystemPrompts.SystemPromptProvider>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.AiAssistant.IAiAssistantOrchestrator,
    OetWithDrHesham.Api.Services.AiAssistant.AiAssistantOrchestrator>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.AiAssistant.IAiAssistantGateway,
    OetWithDrHesham.Api.Services.AiAssistant.AiAssistantGateway>();

// Phase 6b — backfill voice provider rows (TTS/ASR/Phoneme) into the
// AiProviders registry so admins see them in /admin/ai-providers.
// Strictly additive: never overwrites existing rows; selectors still
// read credentials from existing options sources.
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Voice.AiVoiceProviderSeeder>();
// Registered AFTER the voice seeder so an env-derived whisper-asr row (if any)
// wins creation; this seeder only backfills missing canonical rows keyless so
// admins can paste a key in /admin/ai-providers and the integration just works.
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Ai.CoreAiProviderSeeder>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.AiAssistant.AiAssistantFeatureRouteSeeder>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Grammar.IGrammarDraftService,
    OetWithDrHesham.Api.Services.Grammar.GrammarDraftService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingDraftService,
    OetWithDrHesham.Api.Services.Writing.WritingDraftService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Grammar.IGrammarPublishGateService,
    OetWithDrHesham.Api.Services.Grammar.GrammarPublishGateService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Grammar.IGrammarEntitlementService,
    OetWithDrHesham.Api.Services.Grammar.GrammarEntitlementService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingOptionsProvider,
    OetWithDrHesham.Api.Services.Writing.WritingOptionsProvider>();
builder.Services.AddScoped<IWritingEntitlementService,
    OetWithDrHesham.Api.Services.Writing.WritingEntitlementService>();

// ── Writing Module V2 services and crons (OET_WRITING_MODULE_PATHWAY.md §WS5) ──
// Config root bound from appsettings.json:Writing.* — feature flags,
// daily caps, cron toggles, OCR provider keys.
builder.Services.Configure<OetWithDrHesham.Api.Services.Writing.Configuration.WritingV2Options>(
    builder.Configuration.GetSection(OetWithDrHesham.Api.Services.Writing.Configuration.WritingV2Options.SectionName));
// Event bus is singleton — opens scopes per dispatch so handlers see fresh DbContext.
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Writing.Events.IWritingEventBus,
    OetWithDrHesham.Api.Services.Writing.Events.WritingEventBus>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.Events.IWritingEventHandler<OetWithDrHesham.Api.Services.Writing.Events.WritingGradeReady>,
    OetWithDrHesham.Api.Services.Writing.Events.WritingGradeReadyHubEventHandler>();
// Canon engine — scoped so it can consume scoped IAiGatewayService;
// internal compiled-Regex cache lives across requests via a static cache in the implementation.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingCanonEngine,
    OetWithDrHesham.Api.Services.Writing.WritingCanonEngine>();
// Pure pathway generator — no DB, no DI dependencies; safe as singleton.
builder.Services.AddSingleton<OetWithDrHesham.Api.Services.Writing.IWritingPathwayGenerator,
    OetWithDrHesham.Api.Services.Writing.WritingPathwayGenerator>();
// Scoped services (one per HTTP request) — every method takes UserId first.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingOnboardingService,
    OetWithDrHesham.Api.Services.Writing.WritingOnboardingService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingPathwayServiceV2,
    OetWithDrHesham.Api.Services.Writing.WritingPathwayServiceV2>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingDailyPlanServiceV2,
    OetWithDrHesham.Api.Services.Writing.WritingDailyPlanServiceV2>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingPracticeSelectionService,
    OetWithDrHesham.Api.Services.Writing.WritingPracticeSelectionService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingScenarioService,
    OetWithDrHesham.Api.Services.Writing.WritingScenarioService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingCanonService,
    OetWithDrHesham.Api.Services.Writing.WritingCanonService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingDrillServiceV2,
    OetWithDrHesham.Api.Services.Writing.WritingDrillServiceV2>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingCaseNoteDrillService,
    OetWithDrHesham.Api.Services.Writing.WritingCaseNoteDrillService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingLessonServiceV2,
    OetWithDrHesham.Api.Services.Writing.WritingLessonServiceV2>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingMockService,
    OetWithDrHesham.Api.Services.Writing.WritingMockService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingReadinessService,
    OetWithDrHesham.Api.Services.Writing.WritingReadinessService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingCoachServiceV2,
    OetWithDrHesham.Api.Services.Writing.WritingCoachServiceV2>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingOcrService,
    OetWithDrHesham.Api.Services.Writing.WritingOcrService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingModerationService,
    OetWithDrHesham.Api.Services.Writing.WritingModerationService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingAnnotationService,
    OetWithDrHesham.Api.Services.Writing.WritingAnnotationService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingHeuristicPreAssessmentService,
    OetWithDrHesham.Api.Services.Writing.WritingHeuristicPreAssessmentService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingAdminAnalyticsService,
    OetWithDrHesham.Api.Services.Writing.WritingAdminAnalyticsService>();
// OET Writing exam-faithful closure: unified task authoring + JSON import/export,
// the ContentPaper→Scenario publish bridge, and learner attempt-event ingestion.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingTaskAuthoringService,
    OetWithDrHesham.Api.Services.Writing.WritingTaskAuthoringService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingTaskProjectionService,
    OetWithDrHesham.Api.Services.Writing.WritingTaskProjectionService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingAttemptEventService,
    OetWithDrHesham.Api.Services.Writing.WritingAttemptEventService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingTutorReviewService,
    OetWithDrHesham.Api.Services.Writing.WritingTutorReviewService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.WritingMarkingVoiceNoteService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingAppealService,
    OetWithDrHesham.Api.Services.Writing.WritingAppealService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingAnalyticsServiceV2,
    OetWithDrHesham.Api.Services.Writing.WritingAnalyticsServiceV2>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingMistakeService,
    OetWithDrHesham.Api.Services.Writing.WritingMistakeService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingContentAuditService,
    OetWithDrHesham.Api.Services.Writing.WritingContentAuditService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingShowcaseService,
    OetWithDrHesham.Api.Services.Writing.WritingShowcaseService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingRewriteService,
    OetWithDrHesham.Api.Services.Writing.WritingRewriteService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingParaphraseService,
    OetWithDrHesham.Api.Services.Writing.WritingParaphraseService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingAskService,
    OetWithDrHesham.Api.Services.Writing.WritingAskService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingOutlineService,
    OetWithDrHesham.Api.Services.Writing.WritingOutlineService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingScenarioGeneratorService,
    OetWithDrHesham.Api.Services.Writing.WritingScenarioGeneratorService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingDraftServiceV2,
    OetWithDrHesham.Api.Services.Writing.WritingDraftServiceV2>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingCaseNoteHighlightService,
    OetWithDrHesham.Api.Services.Writing.WritingCaseNoteHighlightService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingSubmissionEvaluationPipeline,
    OetWithDrHesham.Api.Services.Writing.WritingSubmissionEvaluationPipeline>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingSubmissionService,
    OetWithDrHesham.Api.Services.Writing.WritingSubmissionService>();
// Result-visibility config + learner-facing gated feedback (spec §15.2/§15.3, WS-B4 Section D/E).
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingResultVisibilityService,
    OetWithDrHesham.Api.Services.Writing.WritingResultVisibilityService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingResultFeedbackService,
    OetWithDrHesham.Api.Services.Writing.WritingResultFeedbackService>();
// Buddy System (spec §23.5) — opt-in matching + chat + weekly check-ins.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingBuddyService,
    OetWithDrHesham.Api.Services.Writing.WritingBuddyService>();
// 50-letter calibration harness (spec §33) — pre-release AI agreement gate.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Writing.IWritingCalibrationService,
    OetWithDrHesham.Api.Services.Writing.WritingCalibrationService>();
// HttpClient used by OCR to call Google Cloud Vision REST endpoint.
builder.Services.AddHttpClient("writing-ocr-gcv");
// 8 hosted crons — gated by Writing:CronsEnabled (default true). Each runs
// on its own cadence inside WritingCronBase; the daily ones run hourly and
// short-circuit unless the current UTC hour matches.
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Writing.Crons.WritingDailyPlanCron>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Writing.Crons.WritingReadinessCron>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Writing.Crons.WritingBatchGradingCron>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Writing.Crons.WritingAnalyticsAggregationCron>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Writing.Crons.WritingTutorQueueAlertCron>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Writing.Crons.WritingDraftCleanupCron>();
builder.Services.AddHostedService<OetWithDrHesham.Api.Services.Writing.Crons.WritingContentAuditCron>();

// ── Private Speaking Sessions ──
builder.Services.Configure<ZoomOptions>(builder.Configuration.GetSection("Zoom"));
builder.Services.AddHttpClient("ZoomApi");
builder.Services.AddHttpClient("ZoomAuth");
builder.Services.AddHttpClient("GoogleCalendar");
builder.Services.AddSingleton<ZoomMeetingService>();
builder.Services.AddScoped<PrivateSpeakingCalendarService>();
builder.Services.AddScoped<PrivateSpeakingService>();
builder.Services.AddScoped<LiveClassService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.LiveClasses.LiveClassRecordingService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.LiveClasses.LiveClassRecordingProcessingService>();

// Wave A1 — Zoom tutor stack: tutor profile + availability + earnings,
// and learner-facing class feedback. See OET_ZOOM_INTEGRATION_PLAN.md §7-§9.
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Classes.ITutorService,
    OetWithDrHesham.Api.Services.Classes.TutorService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Classes.IClassFeedbackService,
    OetWithDrHesham.Api.Services.Classes.ClassFeedbackService>();
builder.Services.AddScoped<OetWithDrHesham.Api.Services.Classes.IClassNotificationService, OetWithDrHesham.Api.Services.Classes.ClassNotificationService>();

var app = builder.Build();

// ── Dev-only one-off: emit the full EF model CREATE script and exit. ──
// Usage: dotnet run -- --emit-create-script <outputPath>
// Used to rebuild a fresh local dev database from the current model when the
// migration chain has accumulated drift (tables present in the model snapshot
// but never created by any migration). Never runs in normal startup paths and
// is a no-op unless the explicit CLI flag is supplied.
if (args.Contains("--emit-create-script"))
{
    var idx = Array.IndexOf(args, "--emit-create-script");
    var outPath = (idx >= 0 && idx + 1 < args.Length) ? args[idx + 1] : "model-create-script.sql";
    using var emitScope = app.Services.CreateScope();
    var emitDb = emitScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
    var script = emitDb.Database.GenerateCreateScript();
    File.WriteAllText(outPath, script);
    Console.WriteLine($"[emit-create-script] Wrote model create script to {outPath} ({script.Length} chars).");
    return;
}

// Runtime settings are read by production startup guards below. Apply pending
// migrations first so a newly deployed image can safely read columns added by
// the same release before the full bootstrap/seeding phase runs later.
await using (var migrationScope = app.Services.CreateAsyncScope())
{
    var db = migrationScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
    // Production PostgreSQL only: the in-memory provider builds its schema from
    // the model, and SQLite desktop/test runtimes use the compatibility
    // bootstrapper because the production migration chain contains
    // PostgreSQL-specific DDL.
    if (db.Database.IsNpgsql())
    {
        await db.Database.MigrateAsync();

        // Idempotent schema self-heal for the RuntimeSettings override columns
        // that the singleton settings provider reads during this very startup.
        // These are all nullable additive columns; running the guarded DDL is a
        // no-op once the column exists. This guarantees a freshly deployed image
        // can read the row even if its migration has not been recorded yet on
        // this database, closing the gap that would otherwise crash startup with
        // "column does not exist".
        try
        {
            await db.Database.ExecuteSqlRawAsync(OetWithDrHesham.Api.Services.Settings.RuntimeSettingsSchemaSelfHeal.Sql);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "RuntimeSettings schema self-heal skipped (non-fatal).");
        }
    }
}

// ── Production safety gate: forbid NoOpUploadScanner when running in production. ──
// Rationale: the NoOp scanner accepts every byte; if production accidentally
// boots with it (misconfiguration, missing env var, container swap), learner
// content uploads can carry malware into storage. Better to refuse to start
// and make the operator look at the config than to silently become a vector.
// Dev/test explicitly opt in via the default UploadScanner:Provider=noop.
{
    var runtimeSettings = app.Services.GetRequiredService<OetWithDrHesham.Api.Services.Settings.IRuntimeSettingsProvider>();
    var scannerSettings = (await runtimeSettings.GetAsync()).UploadScanner;
    var scannerProvider = scannerSettings.Provider;
    if (app.Environment.IsProduction()
        && !string.Equals(scannerProvider, "clamav", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"UploadScanner provider is '{scannerProvider}' in Production. Configure UploadScanner:Provider=clamav "
            + "(or set it through /admin/settings) and set UploadScanner:Host / UploadScanner:Port. See "
            + "Configuration/UploadScannerOptions.cs for the full option surface.");
    }
    if (app.Environment.IsProduction())
    {
        if (!scannerSettings.FailClosedOnError)
        {
            throw new InvalidOperationException("UploadScanner:FailClosedOnError must be true in Production.");
        }

        var scannerOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<OetWithDrHesham.Api.Configuration.UploadScannerOptions>>().Value;
        var endpointReason = OetWithDrHesham.Api.Services.Content.UploadScannerEndpointGuard.GetUnsafeEndpointReason(
            scannerSettings.Host,
            scannerSettings.Port,
            scannerOptions.Host,
            scannerOptions.Port,
            requireDeploymentEndpoint: true);
        if (endpointReason is not null)
        {
            throw new InvalidOperationException($"UploadScanner endpoint is unsafe in Production: {endpointReason}");
        }
    }
}

// ── Storage persistence guard ─────────────────────────────────────────────────
// If running inside a container (DOTNET_RUNNING_IN_CONTAINER=true) and the
// storage root is the default relative "App_Data/storage", files will be lost
// on container rebuild. Fail-fast in Production; warn loudly in Development.
{
    var isContainer = string.Equals(
        Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true",
        StringComparison.OrdinalIgnoreCase);

    if (isContainer && !Path.IsPathRooted(storageOptions.LocalRootPath))
    {
        var msg = $"Storage:LocalRootPath is '{storageOptions.LocalRootPath}' (relative) inside a container. "
                + "Files stored here will be LOST on container rebuild. "
                + "Set Storage__LocalRootPath to an absolute path backed by a Docker volume "
                + "(e.g. /var/opt/oet-with-dr-hesham/storage).";

        if (app.Environment.IsProduction())
        {
            throw new InvalidOperationException(msg);
        }

        app.Logger.LogCritical("⚠️  STORAGE DATA LOSS RISK: {Message}", msg);
    }
}

if (trustForwardHeaders)
{
    app.UseForwardedHeaders();
}

// Correlation ID middleware
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                        ?? Guid.NewGuid().ToString("N");
    context.Items["CorrelationId"] = correlationId;
    context.Response.Headers["X-Correlation-Id"] = correlationId;

    using (app.Logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId, ["UserId"] = context.User?.FindFirst("sub")?.Value ?? "anonymous" }))
    {
        await next();
    }
});

// Security headers
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(self), microphone=(self), geolocation=()";
    await next();
});

app.UseExceptionHandler(handler =>
{
    handler.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var exception = feature?.Error;
        context.Response.ContentType = "application/problem+json";

        var correlationId = context.Items.TryGetValue("CorrelationId", out var cid) ? cid as string : null;

        if (exception is ApiException apiException)
        {
            context.Response.StatusCode = apiException.StatusCode;
            var apiPayload = new
            {
                code = apiException.ErrorCode,
                message = apiException.Message,
                fieldErrors = apiException.FieldErrors.Select(x => new
                {
                    field = x.Field,
                    code = x.Code,
                    message = x.Message
                }),
                retryable = apiException.Retryable,
                supportHint = apiException.SupportHint,
                correlationId
            };
            await context.Response.WriteAsync(JsonSupport.Serialize(apiPayload));
            return;
        }

        if (exception is MfaChallengeRequiredException mfaChallengeRequiredException)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            var mfaPayload = new
            {
                code = "mfa_challenge_required",
                message = mfaChallengeRequiredException.Message,
                email = mfaChallengeRequiredException.Email,
                challengeToken = mfaChallengeRequiredException.ChallengeToken,
                retryable = false,
                correlationId
            };
            await context.Response.WriteAsync(JsonSupport.Serialize(mfaPayload));
            return;
        }

        if (exception is BadHttpRequestException badHttpRequestException)
        {
            var statusCode = badHttpRequestException.StatusCode is >= 400 and < 500
                ? badHttpRequestException.StatusCode
                : StatusCodes.Status400BadRequest;
            context.Response.StatusCode = statusCode;
            var badRequestPayload = new
            {
                code = "invalid_request",
                message = app.Environment.IsDevelopment()
                    ? badHttpRequestException.Message
                    : "The request body, route values, or query string are invalid.",
                retryable = false,
                correlationId
            };
            await context.Response.WriteAsync(JsonSupport.Serialize(badRequestPayload));
            return;
        }

        // Optimistic-concurrency conflict (xmin mismatch on Subscription,
        // Invoice, SubscriptionItem, Evaluation, or any other entity mapped
        // with a concurrency token). Surface as 409 so the UI / caller can
        // reload state and retry. DO NOT auto-retry here — that is the
        // caller's decision and, for idempotent server paths, happens inside
        // ConcurrencyRetry.ExecuteAsync.
        if (exception is Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException concurrencyException)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            app.Logger.LogWarning(
                concurrencyException,
                "Concurrency conflict on {Method} {Path}. CorrelationId: {CorrelationId}",
                context.Request.Method,
                context.Request.Path,
                correlationId ?? "missing");
            // Tag in Sentry as a filterable group so we can alert on
            // concurrency_conflict rate without pulling in a full metrics stack.
            // No-op when Sentry:Dsn is unset.
            Sentry.SentrySdk.CaptureException(concurrencyException, scope =>
            {
                scope.Level = Sentry.SentryLevel.Warning;
                scope.SetTag("conflict_kind", "concurrency");
                scope.SetTag("http_status", "409");
                scope.SetTag("route", context.Request.Path.Value ?? "unknown");
            });
            var conflictPayload = new
            {
                code = "concurrency_conflict",
                message = "The resource was modified by another request. Reload and try again.",
                retryable = true,
                correlationId
            };
            await context.Response.WriteAsync(JsonSupport.Serialize(conflictPayload));
            return;
        }

        app.Logger.LogError(
            exception,
            "Unhandled exception while processing {Method} {Path}. CorrelationId: {CorrelationId}",
            context.Request.Method,
            context.Request.Path,
            correlationId ?? "missing");

        // H10: forward unhandled exceptions to Sentry. No-op when Sentry:Dsn is empty
        // because SentrySdk is not initialised. PII is scrubbed by SentryBootstrap.ScrubPii.
        if (exception is not null)
        {
            Sentry.SentrySdk.CaptureException(exception);
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var payload = new
        {
            code = "internal_server_error",
            message = app.Environment.IsDevelopment()
                ? exception?.Message ?? "An unexpected server error occurred."
                : "An unexpected server error occurred.",
            retryable = false,
            supportHint = "If this persists, contact support with the correlation ID.",
            correlationId
        };
        await context.Response.WriteAsync(JsonSupport.Serialize(payload));
    });
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (enforceHttps)
{
    app.UseHttpsRedirection();
}

if (enableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI();
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }
}

if (corsOrigins.Length > 0)
{
    app.UseCors("Frontend");
}

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
// Forced-update enforcement: reject out-of-date desktop/mobile shells with 426.
// Inert until an admin enables it; header-gated so the website is never affected.
app.UseClientVersionGate();
// Enable native WebSocket upgrades for the Writing Coach panel fallback
// (/ws/writing/coach/{sessionId}). SignalR has its own internal websocket
// handling; this only affects raw WS endpoints mapped via app.Map.
app.UseWebSockets();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok", service = "OET with Dr Hesham API", timestamp = DateTimeOffset.UtcNow, check = "live" }))
    .AllowAnonymous();
app.MapGet("/health/ready", async (LearnerDbContext db, IOptions<StorageOptions> storageOptions, ILoggerFactory loggerFactory, CancellationToken ct) =>
{
    try
    {
        var checks = new Dictionary<string, string>();
        var healthy = true;

        // Database connectivity
        var dbOk = db.Database.IsInMemory() || await db.Database.CanConnectAsync(ct);
        checks["database"] = dbOk ? "ok" : "unavailable";
        if (!dbOk) healthy = false;

        if (dbOk && !db.Database.IsInMemory())
        {
            if (db.Database.IsSqlite())
            {
                // SQLite schemas are created via EnsureCreatedAsync (see
                // DatabaseBootstrapper), which never writes __EFMigrationsHistory,
                // so GetPendingMigrationsAsync would report every migration as
                // pending forever and wedge the bundled desktop backend at 503.
                checks["migrations"] = "ok";
            }
            else
            {
                var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(ct)).Take(1).ToArray();
                checks["migrations"] = pendingMigrations.Length == 0 ? "ok" : "pending";
                if (pendingMigrations.Length > 0) healthy = false;
            }
        }

        // Stuck jobs detection (processing for > 10 minutes)
        if (dbOk && !db.Database.IsInMemory())
        {
            var stuckThreshold = DateTimeOffset.UtcNow.AddMinutes(-10);
            int stuckJobs;

            try
            {
                stuckJobs = await db.BackgroundJobs
                    .AsNoTracking()
                    .CountAsync(j => j.State == AsyncState.Processing && j.LastTransitionAt < stuckThreshold, ct);
            }
            catch (InvalidOperationException) when (db.Database.IsSqlite())
            {
                var backgroundJobSnapshots = await db.BackgroundJobs
                    .AsNoTracking()
                    .Select(j => new { j.State, j.LastTransitionAt })
                    .ToListAsync(ct);

                stuckJobs = backgroundJobSnapshots.Count(j => j.State == AsyncState.Processing && j.LastTransitionAt < stuckThreshold);
            }

            checks["stuck_jobs"] = stuckJobs > 0 ? $"warning:{stuckJobs}" : "ok";
        }

        // Storage path writable
        var storagePath = storageOptions.Value.LocalRootPath;
        if (!string.IsNullOrEmpty(storagePath))
        {
            try
            {
                var resolvedPath = Path.GetFullPath(storagePath);
                Directory.CreateDirectory(resolvedPath);
                var testFile = Path.Combine(resolvedPath, ".health-check");
                await File.WriteAllTextAsync(testFile, "ok", ct);
                File.Delete(testFile);
                checks["storage"] = "ok";
            }
            catch
            {
                checks["storage"] = "unavailable";
                healthy = false;
            }
        }

        var result = new { status = healthy ? "ok" : "failed", service = "OET with Dr Hesham API", checks, timestamp = DateTimeOffset.UtcNow, check = "ready" };
        return healthy
            ? Results.Ok(result)
            : Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        loggerFactory.CreateLogger("Health.Ready").LogWarning(ex, "Readiness health check failed.");
        return Results.Json(new
        {
            status = "failed",
            service = "OET with Dr Hesham API",
            checks = new { readiness = "unavailable" },
            timestamp = DateTimeOffset.UtcNow,
            check = "ready"
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();
app.MapGet("/health", async (LearnerDbContext db, CancellationToken ct) =>
{
    var database = db.Database.IsInMemory() || await db.Database.CanConnectAsync(ct);
    return database
        ? Results.Ok(new { status = "ok", service = "OET with Dr Hesham API", database = "ok", timestamp = DateTimeOffset.UtcNow })
        : Results.Json(new { status = "failed", service = "OET with Dr Hesham API", database = "unavailable", timestamp = DateTimeOffset.UtcNow }, statusCode: StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();

app.MapAuthEndpoints();
app.MapProfessionCatalogEndpoints();
app.MapPublicSupportEndpoints();
app.MapAnalyticsEndpoints();
app.MapNotificationEndpoints();
app.MapLearnerEndpoints();
app.MapStudyPlanTemplateAdminEndpoints();
app.MapBillingRegionEndpoints();
app.MapBillingExpansionEndpoints();
app.MapBillingExpansionV2Endpoints();
app.MapBillingExpansionV3Endpoints();
app.MapOet2026CatalogEndpoints();
app.MapBillingCatalogEndpoints();
app.MapBillingCartEndpoints();
app.MapBillingCheckoutEndpoints();
app.MapBillingSubscriptionEndpoints();
app.MapBillingPromoCodeEndpoints();
app.MapAiPackageCreditEndpoints();
app.MapStripeWebhookEndpoints();
// Video Library — learner catalog + attested playback, admin CRUD, Bunny webhook.
app.MapVideoLibraryEndpoints();
app.MapVideoLibraryAdminEndpoints();
app.MapVideoLibraryWebhookEndpoints();
// Wave B4 — finance/ops admin surface (revenue/MRR/churn/LTV/refunds + product
// & coupon CRUD + Stripe Tax registrations). Each route requires a specific
// granular billing permission (read/refund_write/catalog_write).
app.MapAdminBillingEndpoints();
app.MapTutorBookEndpoints();
app.MapAffiliatePortalEndpoints();
app.MapAiAnalyticsEndpoints();
app.MapMockReadinessEndpoints();
app.MapReadinessEndpoints();
app.MapAdminReadinessEndpoints();
app.MapMockBookingEndpoints();
app.MapLearnerActionsEndpoints();
app.MapExpertEndpoints();
app.MapExpertMessagingEndpoints();
app.MapExpertCompensationEndpoints();
app.MapAdminEndpoints();
app.MapVoiceDesignAdminEndpoints();
app.MapAdminAlertEndpoints();
app.MapAdminCampaignEndpoints();
app.MapAdminLaunchReadinessEndpoints();
app.MapAiUsageAdminEndpoints();
app.MapAiEscalationAdminEndpoints();
app.MapAiToolsAdminEndpoints();
app.MapAiMeEndpoints();
OetWithDrHesham.Api.Endpoints.AiAssistantEndpoints.MapAiAssistantEndpoints(app);
app.MapContentPapersAdminEndpoints();
app.MapExpertAdminEndpoints();
app.MapContentStalenessEndpoints();
app.MapMockAdminEndpoints();
app.MapMockEntitlementEndpoints();
// Wave 2 endpoint groups — added by W2-A on behalf of W2-D/E/F.
// W2-A's own endpoints:
app.MapMockBundleReviewStageEndpoints();
app.MapMockAnalyticsEndpoints();
// W2-D — Speaking ASR transcription endpoints.
app.MapSpeakingTranscriptionEndpoints();
// W2-F — Speaking expert review voice-note endpoints.
app.MapSpeakingReviewVoiceNoteEndpoints();
app.MapContentPapersLearnerEndpoints();
app.MapReadingAuthoringAdminEndpoints();
app.MapReadingAnalyticsAdminEndpoints();
app.MapWritingAnalyticsAdminEndpoints();
app.MapWritingPathwayEndpoints();
// Writing Module V2 — onboarding, diagnostic, pathway V2, submissions,
// drafts V2, scenarios, exemplars, drills V2, lessons V2, mocks, coach,
// stats, canon library, mistakes, tutor review, OCR, showcase, AI tools,
// admin content, tutor portal + native WebSocket coach fallback (~60+
// routes across 20 endpoint files). See WritingRouteBuilderExtensions.cs.
app.MapWritingV2Endpoints();
app.MapListeningAuthoringAdminEndpoints();
app.MapListeningAdminAnalyticsEndpoints();
app.MapReadingLearnerEndpoints();
app.MapReadingTutorAdminEndpoints();
app.MapReadingPathwayEndpoints();
app.MapListeningPathwayEndpoints();
app.MapListeningAudioEndpoints();
app.MapListeningLearnerEndpoints();
app.MapListeningV2Endpoints();
app.MapListeningExpertEndpoints();
app.MapListeningPolicyAdminEndpoints();
app.MapReadingPolicyAdminEndpoints();
app.MapContentHierarchyEndpoints();
app.MapRecallsEndpoints();
app.MapScoringPolicyEndpoints();
app.MapRulebookReferencePdfEndpoints();
app.MapResultTemplatesEndpoints();
app.MapSpeakingSharedResourcesEndpoints();
app.MapMaterialsAdminEndpoints();
app.MapMaterialsLearnerEndpoints();
app.MapRealContentFolderImportEndpoints();

// ── Phase 1 new endpoints ──
app.MapGamificationEndpoints();
app.MapReviewItemEndpoints();
app.MapVocabularyEndpoints();
// 2026-05-27 audit fix — Grammar + Remediation endpoints (closed missing-API gap).
app.MapGrammarEndpoints();
app.MapRemediationEndpoints();
app.MapRecallSetTagsEndpoints();
app.MapAdaptiveEndpoints();

// ── Phase 2 new endpoints ──
app.MapPredictionEndpoints();

// ── Phase 3 new endpoints ──
app.MapLearningContentEndpoints();
app.MapCommunityEndpoints();
app.MapPeerReviewEndpoints();
app.MapNotificationRuleEndpoints();
app.MapSocialEndpoints();

// ── Phase 2 new endpoints ──
app.MapConversationEndpoints();
app.MapPronunciationEndpoints();
app.MapWritingCoachEndpoints();
app.MapWritingPdfEndpoints();
app.MapSpeakingPdfEndpoints();
app.MapMarketplaceEndpoints();

// ── Sponsor Dashboard ──
if (app.Configuration.GetValue<bool>("Features:SponsorPortalEnabled"))
{
    app.MapSponsorEndpoints();
}

// ── Private Speaking Sessions ──
app.MapPrivateSpeakingEndpoints();
app.MapSpeakingAliasEndpoints();
app.MapLiveClassEndpoints();
app.MapTutorEndpoints();
app.MapSpeakingCalibrationEndpoints();

// ── OET Speaking module (Phase 1+ role-play cards, sessions, compliance) ──
app.MapAdminSpeakingContentEndpoints();
app.MapLearnerSpeakingRolePlayCardEndpoints();
app.MapSpeakingComplianceEndpoints();
// Phase 2 — typed Speaking session lifecycle (prep → active → finished).
app.MapSpeakingSessionEndpoints();
app.MapSpeakingExamEndpoints();
// WS6 — Speaking result-visibility (learner read + admin upsert, §10).
app.MapSpeakingResultVisibilityEndpoints();
// Phase 3 — live-tutor rooms + LiveKit webhook ingestion.
app.MapSpeakingLiveRoomEndpoints();
app.MapLiveKitWebhookEndpoint();
// Phase 4 — tutor-side speaking review surface.
app.MapTutorSpeakingEndpoints();
// Double-marking + senior moderation surface (§15.4 / §15.5).
app.MapSpeakingModerationEndpoints();
// Phase 5 — speaking drill bank (learner + admin).
app.MapSpeakingDrillEndpoints();
// Phase 6 — speaking analytics dashboards, interlocutor training modules,
// and the 16-stage learner course pathway.
app.MapSpeakingAnalyticsEndpoints();
app.MapInterlocutorTrainingEndpoints();
app.MapSpeakingCoursePathwayEndpoints();

// ── Rulebook + Writing linter + Speaking auditor + Grounded AI gateway ──
app.MapRulebookEndpoints();
app.MapRulebookAdminEndpoints();

// ── Media Management ──
app.MapMediaEndpoints();

// ── Device Pairing (H13 scaffold) ──
app.MapDevicePairingEndpoints();

// "HubConnect" caps negotiate/connect frequency per account — SignalR hubs have no
// other rate limiting, so nothing else throttles a reconnect-loop client.
app.MapHub<NotificationHub>("/v1/notifications/hub").RequireAuthorization().RequireRateLimiting("HubConnect");
app.MapHub<ConversationHub>("/v1/conversations/hub").RequireAuthorization().RequireRateLimiting("HubConnect");
app.MapHub<OetWithDrHesham.Api.Hubs.AiAssistantHub>("/v1/ai-assistant/hub").RequireAuthorization().RequireRateLimiting("HubConnect");
app.MapHub<OetWithDrHesham.Api.Services.Mocks.MockLiveRoomHub>("/v1/mocks/live-room/hub").RequireAuthorization().RequireRateLimiting("HubConnect");
app.MapHub<OetWithDrHesham.Api.Hubs.SpeakingLiveRoomHub>("/v1/speaking/live-rooms/hub").RequireAuthorization().RequireRateLimiting("HubConnect");
// Writing Module V2 hubs — submission grade events, coach hint streaming,
// today/plan + pathway recalculate broadcasts. See Hubs/Writing*Hub.cs.
app.MapHub<OetWithDrHesham.Api.Hubs.WritingSubmissionHub>("/hubs/writing-submissions").RequireAuthorization("LearnerOnly").RequireRateLimiting("HubConnect");
app.MapHub<OetWithDrHesham.Api.Hubs.WritingCoachHub>("/hubs/writing-coach").RequireAuthorization("LearnerOnly").RequireRateLimiting("HubConnect");
app.MapHub<OetWithDrHesham.Api.Hubs.WritingTodayHub>("/hubs/writing-today").RequireAuthorization("LearnerOnly").RequireRateLimiting("HubConnect");

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
    var storage = scope.ServiceProvider
        .GetRequiredService<OetWithDrHesham.Api.Services.Content.IFileStorage>();
    await DatabaseBootstrapper.InitializeAsync(db, app.Environment, bootstrapOptions, storageOptions, storage);

    // Sync AI provider key from env (AI__ApiKey, AI__BaseUrl, AI__DefaultModel)
    // into the AiProviders row so the registry-backed provider resolves it at
    // runtime. Encrypts with Data Protection. Idempotent.
    var dp = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
    var aiOpts = scope.ServiceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<OetWithDrHesham.Api.Configuration.AiProviderOptions>>()
        .Value;
    await DatabaseBootstrapper.SynchroniseAiProviderFromEnvAsync(db, dp, aiOpts);

    // Seed the RecallSetTags registry — DISABLED: admin manages recalls catalog manually.
    // var seedLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
    //     .CreateLogger("RecallSetTagRegistrySeeder");
    // try
    // {
    //     await OetWithDrHesham.Api.Services.Recalls.RecallSetTagRegistrySeeder.EnsureAsync(db, seedLogger);
    // }
    // catch (Exception ex)
    // {
    //     seedLogger.LogWarning(ex, "RecallSetTagRegistrySeeder failed at boot; continuing.");
    // }

    // Writing Module V2 *content* seeding (demo scenarios, mocks, lessons,
    // drills, exemplars, common mistakes, sample papers) has been removed
    // permanently — it resurrected admin-deleted tasks on every deploy. The
    // platform now runs on real, admin-authored Writing content only.
    //
    // The AI-grading canon rulebook is NOT learner-facing content and creates
    // no tasks, so its idempotent materialisation is preserved here so writing
    // scoring keeps the same R* rules the lint engine uses.
    var writingCanonLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("BackendRulebookCanonBridge");
    try
    {
        await OetWithDrHesham.Api.Services.Writing.BackendRulebookCanonBridge
            .SeedFromRulebooksAsync(db, writingCanonLogger, CancellationToken.None);
    }
    catch (Exception ex)
    {
        writingCanonLogger.LogWarning(ex, "BackendRulebookCanonBridge failed at boot; continuing.");
    }
}

// Speaking drill catalogue seed (Speaking module G.5). Seeds 24 canonical
// drills (2 per SpeakingDrillKind) + their published ContentItems. The seeder
// existed but was never wired into startup, so no canonical SpeakingDrillItem
// rows were ever seeded (prod or tests). Idempotent (probes the "sdi-seed-"
// id prefix); non-fatal on failure so it never blocks startup.
using (var speakingDrillScope = app.Services.CreateScope())
{
    var speakingDrillLogger = speakingDrillScope.ServiceProvider
        .GetRequiredService<ILogger<Program>>();
    try
    {
        var speakingDrillDb = speakingDrillScope.ServiceProvider
            .GetRequiredService<OetWithDrHesham.Api.Data.LearnerDbContext>();
        await OetWithDrHesham.Api.Services.Seeding.SpeakingDrillSeed.SeedAsync(speakingDrillDb, CancellationToken.None);
    }
    catch (Exception ex)
    {
        speakingDrillLogger.LogWarning(ex, "Speaking drill seeder failed (non-fatal)");
    }
}

// Speaking card types + role-play cards seed (Speaking module rules pass,
// 2026-06-29). Seeds the 6 hidden communication-function card types (one-off
// bootstrap, admin-editable thereafter) THEN the original sample role-play
// cards, each mapped to a card type. Ordered so the card-type FK target exists
// before the cards reference it. Both are idempotent; non-fatal on failure.
using (var speakingContentScope = app.Services.CreateScope())
{
    var speakingContentLogger = speakingContentScope.ServiceProvider
        .GetRequiredService<ILogger<Program>>();
    try
    {
        var speakingContentDb = speakingContentScope.ServiceProvider
            .GetRequiredService<OetWithDrHesham.Api.Data.LearnerDbContext>();
        await OetWithDrHesham.Api.Services.Seeding.SpeakingCardTypeSeed.SeedAsync(speakingContentDb, CancellationToken.None);
        await OetWithDrHesham.Api.Services.Seeding.SpeakingRolePlayCardSeed.SeedAsync(speakingContentDb, CancellationToken.None);
    }
    catch (Exception ex)
    {
        speakingContentLogger.LogWarning(ex, "Speaking card type / role-play card seeder failed (non-fatal)");
    }
}

// Listening sample ingester (Slice E of docs/LISTENING-INGESTION-PRD.md).
// Reads `Project Real Content/Listening (..)/Listening Sample {1,2,3}/` and
// registers each as a Draft ContentPaper with all 4 required asset roles
// (Audio + QuestionPaper + AudioScript + AnswerKey). Idempotent (SHA-256 +
// slug). Disabled by default; non-fatal on failure so a missing asset folder
// never breaks startup.
using (var seedScope = app.Services.CreateScope())
{
    var listeningSeeder = seedScope.ServiceProvider
        .GetRequiredService<OetWithDrHesham.Api.Services.Listening.IListeningSampleSeeder>();
    try
    {
        await listeningSeeder.SeedAsync(CancellationToken.None);
    }
    catch (Exception ex)
    {
        seedScope.ServiceProvider.GetRequiredService<ILogger<Program>>()
            .LogWarning(ex, "Listening sample seeder failed (non-fatal)");
    }
}

// Listening starter content seeder — Wave 5b of the OET Listening gap-fill
// plan. Reads hand-authored 42-question JSON fixtures from
// `Data/SeedData/listening/` and upserts them via the authoring service.
// Idempotent; disabled by default via `Seed:ListeningStarter:Enabled`.
using (var seedScope = app.Services.CreateScope())
{
    var starterSeeder = seedScope.ServiceProvider
        .GetRequiredService<OetWithDrHesham.Api.Services.Listening.IListeningStarterContentSeeder>();
    try
    {
        await starterSeeder.SeedAsync(CancellationToken.None);
    }
    catch (Exception ex)
    {
        seedScope.ServiceProvider.GetRequiredService<ILogger<Program>>()
            .LogWarning(ex, "Listening starter content seeder failed (non-fatal)");
    }
}

// Mock sample seeder — Development-only preview data. Reads
// `Project Real Content/` and creates three Draft MockBundles so the admin
// wizard and learner mock surface have something to point at out of the box.
// Idempotent (skips if `sample-mock-{n}` already exists); non-fatal.
if (app.Environment.IsDevelopment())
{
    using var seedScope = app.Services.CreateScope();
    var mockSeeder = seedScope.ServiceProvider
        .GetRequiredService<OetWithDrHesham.Api.Services.Seeding.MockSampleSeeder>();
    try
    {
        await mockSeeder.SeedAsync(CancellationToken.None);
    }
    catch (Exception ex)
    {
        seedScope.ServiceProvider.GetRequiredService<ILogger<Program>>()
            .LogWarning(ex, "Mock sample seeder failed (non-fatal)");
    }
}

// Study plan template seeder — runs in all environments. Idempotent: if the
// StudyPlanTemplates table already has rows the seeder no-ops, so admins can
// edit/delete the starter set without re-introduction on restart. Non-fatal.
{
    using var seedScope = app.Services.CreateScope();
    var templateSeeder = seedScope.ServiceProvider
        .GetRequiredService<OetWithDrHesham.Api.Services.Planner.StudyPlanTemplateSeeder>();
    try
    {
        await templateSeeder.SeedIfEmptyAsync(CancellationToken.None);
    }
    catch (Exception ex)
    {
        seedScope.ServiceProvider.GetRequiredService<ILogger<Program>>()
            .LogWarning(ex, "Study plan template seeder failed (non-fatal)");
    }
}

app.Run();

static bool HasAdminPermission(AuthorizationHandlerContext ctx, params string[] anyOf)
{
    var perms = ctx.User.FindFirstValue(AuthTokenService.AdminPermissionsClaimType);
    return OetWithDrHesham.Api.Security.AdminPermissionEvaluator.HasAny(perms, anyOf);
}

public partial class Program;
