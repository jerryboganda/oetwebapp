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
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;
using OetLearner.Api.Hubs;
using OetLearner.Api.Security;
using OetLearner.Api.Services;
using OetLearner.Api.Services.LiveClasses;
using OetLearner.Api.Observability;

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
    .SetApplicationName("OET Prep");
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
builder.Services.Configure<OetLearner.Api.Configuration.LiveKitOptions>(builder.Configuration.GetSection(OetLearner.Api.Configuration.LiveKitOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize =
        OetLearner.Api.Services.Conversation.ConversationRealtimeTransportLimits.MaximumReceiveMessageBytes;
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
    client.DefaultRequestHeaders.Add("User-Agent", "OetLearner-PasswordPolicy");
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
    // The desktop smoke suite reuses seeded accounts across many browser projects in parallel.
    // Keep production protections tight, but give development/test runs enough headroom to avoid
    // false-positive 429s from shared-session traffic bursts.
    var perUserPermitLimit = builder.Environment.IsDevelopment() ? 5000 : 100;
    var perUserWritePermitLimit = builder.Environment.IsDevelopment() ? 300 : 30;
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
    // The free vs paid distinction is driven by entitlement claims; learners
    // who lack a paid entitlement get the "-free" budget. Endpoints attach
    // the free policy; the WritingEntitlementService bumps qualifying users
    // to the paid bucket via an HttpContext.Items signal set in middleware.
    static string ResolveWritingUserKey(HttpContext httpContext)
        => httpContext.User.Identity?.IsAuthenticated == true
            ? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous"
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    var writingSubmissionsFreeHour = builder.Environment.IsDevelopment() ? 100 : 1;
    var writingSubmissionsFreeDay = builder.Environment.IsDevelopment() ? 500 : 5;
    options.AddPolicy("writing-submissions-free", httpContext =>
    {
        var key = ResolveWritingUserKey(httpContext);
        // Two-window guard: hourly OR daily quotas — both must allow. The
        // hourly window is the tighter rejection; daily is enforced via a
        // partition-keyed sliding window matched on the user id.
        return RateLimitPartition.GetSlidingWindowLimiter($"writing-sub-free-{key}", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = writingSubmissionsFreeDay,
            Window = TimeSpan.FromDays(1),
            SegmentsPerWindow = 24,
            QueueLimit = 0,
        });
    });

    var writingSubmissionsPaidHour = builder.Environment.IsDevelopment() ? 300 : 5;
    var writingSubmissionsPaidDay = builder.Environment.IsDevelopment() ? 3000 : 30;
    options.AddPolicy("writing-submissions-paid", httpContext =>
    {
        var key = ResolveWritingUserKey(httpContext);
        return RateLimitPartition.GetSlidingWindowLimiter($"writing-sub-paid-{key}", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = writingSubmissionsPaidDay,
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
            var account = await db.ApplicationUserAccounts
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == authAccountId, context.HttpContext.RequestAborted);

            if (account is null)
            {
                context.Fail("account_not_found");
                return;
            }

            if (account.DeletedAt is not null)
            {
                context.Fail("account_deleted");
                return;
            }

            if (string.Equals(account.Role, ApplicationUserRoles.Learner, StringComparison.Ordinal))
            {
                var learnerActive = await db.Users
                    .AsNoTracking()
                    .Where(x => x.AuthAccountId == account.Id)
                    .Select(x => x.AccountStatus)
                    .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

                if (!string.Equals(learnerActive, "active", StringComparison.OrdinalIgnoreCase))
                {
                    context.Fail("account_suspended");
                }

                return;
            }

            if (string.Equals(account.Role, ApplicationUserRoles.Expert, StringComparison.Ordinal))
            {
                var expertActive = await db.ExpertUsers
                    .AsNoTracking()
                    .Where(x => x.AuthAccountId == account.Id)
                    .Select(x => x.IsActive)
                    .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

                if (!expertActive)
                {
                    context.Fail("account_suspended");
                }
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
builder.Services.AddScoped<OetLearner.Api.Services.Mocks.Results.IMockSectionResultAdapter,
    OetLearner.Api.Services.Mocks.Results.ReadingMockSectionResultAdapter>();
builder.Services.AddScoped<OetLearner.Api.Services.Mocks.Results.IMockSectionResultAdapter,
    OetLearner.Api.Services.Mocks.Results.ListeningMockSectionResultAdapter>();
builder.Services.AddScoped<OetLearner.Api.Services.Mocks.Results.IMockSectionResultAdapter,
    OetLearner.Api.Services.Mocks.Results.LegacyMockSectionResultAdapter>();
builder.Services.AddScoped<OetLearner.Api.Services.Mocks.Results.MockSectionResultResolver>();
builder.Services.AddScoped<OetLearner.Api.Services.Mocks.Results.IMockReportAggregationService,
    OetLearner.Api.Services.Mocks.Results.MockReportAggregationService>();
builder.Services.AddScoped<OetLearner.Api.Services.Mocks.MockReadinessTrendService>();
builder.Services.AddScoped<OetLearner.Api.Services.Readiness.ReadinessForecastCalculator>();
builder.Services.AddScoped<OetLearner.Api.Services.Readiness.ReadinessBlockerRules>();
builder.Services.AddScoped<OetLearner.Api.Services.Readiness.ReadinessComputationService>();
// Wave 2 service registrations — added by W2-A on behalf of W2-D/E/F.
// W2-A's own services:
builder.Services.AddScoped<OetLearner.Api.Services.Mocks.MockBundleReviewStageService>();
builder.Services.AddScoped<OetLearner.Api.Services.Mocks.MockPassPredictionService>();
// W2-D — Speaking transcription pipeline (ASR adapter).
// Default provider is the deterministic Mock; production wiring swaps this DI line for a real ASR adapter.
// 2026-05-27 audit fix — bind both providers; the pipeline picks Whisper when
// the API key is configured (admin-panel DB override OR `Speaking:Whisper:ApiKey`
// in appsettings), otherwise falls back to the deterministic mock so dev / CI
// environments without an API key remain usable. 2026-05-28: the API key is
// now resolved via IRuntimeSettingsProvider so admins can rotate it from the
// admin panel without an app restart.
builder.Services.AddHttpClient("SpeakingWhisperClient");
builder.Services.AddScoped<OetLearner.Api.Services.Speaking.OpenAiWhisperSpeakingProvider>();
builder.Services.AddScoped<OetLearner.Api.Services.Speaking.MockSpeakingTranscriptionProvider>();
builder.Services.AddScoped<OetLearner.Api.Services.Speaking.ISpeakingTranscriptionProvider>(sp =>
{
    var whisper = sp.GetRequiredService<OetLearner.Api.Services.Speaking.OpenAiWhisperSpeakingProvider>();
    return whisper.IsConfigured
        ? whisper
        : sp.GetRequiredService<OetLearner.Api.Services.Speaking.MockSpeakingTranscriptionProvider>();
});
builder.Services.AddScoped<OetLearner.Api.Services.Speaking.SpeakingTranscriptionPipeline>();
// RULE_40 tone assessor — consumed by SpeakingTranscriptionEndpoints below.
builder.Services.AddScoped<OetLearner.Api.Services.Speaking.ISpeakingToneAssessor,
    OetLearner.Api.Services.Speaking.SpeakingToneAssessor>();
// W2-E — Speaking + Writing AI pre-analysis services.
builder.Services.AddScoped<OetLearner.Api.Services.Speaking.SpeakingPreAnalysisService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.WritingPreScoreService>();
// W2-F — Speaking expert review voice-note service.
builder.Services.AddScoped<OetLearner.Api.Services.Speaking.SpeakingReviewVoiceNoteService>();
// Phase 2 (B.3) — typed Speaking session lifecycle service.
builder.Services.AddScoped<OetLearner.Api.Services.Speaking.SpeakingSessionService>();
// WS6 — Speaking result-visibility config (§10).
builder.Services.AddScoped<OetLearner.Api.Services.Speaking.ISpeakingResultVisibilityService,
    OetLearner.Api.Services.Speaking.SpeakingResultVisibilityService>();
// Phase 7 (B.8) — Speaking compliance / consent / GDPR erasure service.
builder.Services.AddScoped<OetLearner.Api.Services.Speaking.SpeakingComplianceService>();
// Phase 2 (B.3) — AI-side speaking assessment scorer.
builder.Services.AddScoped<OetLearner.Api.Services.Speaking.SpeakingAiAssessmentService>();
// Speaking module rebuild (2026-06-11) — two-card exam orchestrator.
builder.Services.AddScoped<OetLearner.Api.Services.Speaking.SpeakingExamService>();
// Phase 6 (P6) — LiveKit gateway. When LiveKit is configured (api key
// present) the cloud adapter mints real JWTs and hits the LiveKit REST
// surface; otherwise we fall back to the stub used by dev + tests.
// Webhook signature verification in both implementations is real.
{
    var liveKitSection = builder.Configuration.GetSection(OetLearner.Api.Configuration.LiveKitOptions.SectionName);
    var liveKitOptions = liveKitSection.Get<OetLearner.Api.Configuration.LiveKitOptions>()
        ?? new OetLearner.Api.Configuration.LiveKitOptions();
    if (liveKitOptions.IsEnabled)
    {
        builder.Services.AddHttpClient("LiveKitCloud", c => { c.Timeout = TimeSpan.FromSeconds(15); });
        builder.Services.AddSingleton<OetLearner.Api.Services.Speaking.ILiveKitGateway,
            OetLearner.Api.Services.Speaking.LiveKitCloudGateway>();
    }
    else
    {
        builder.Services.AddSingleton<OetLearner.Api.Services.Speaking.ILiveKitGateway,
            OetLearner.Api.Services.Speaking.LiveKitGatewayStub>();
    }
}
// Phase 3 (B.4) — live-tutor room orchestration service.
builder.Services.AddScoped<OetLearner.Api.Services.Speaking.SpeakingLiveRoomService>();
// Phase 4 — tutor-side scoring + review-queue services.
builder.Services.AddScoped<OetLearner.Api.Services.Speaking.TutorAssessmentService>();
builder.Services.AddScoped<OetLearner.Api.Services.Speaking.TutorReviewQueueService>();
// Double-marking + senior moderation (§15.4 / §15.5).
builder.Services.AddScoped<OetLearner.Api.Services.Speaking.SpeakingModerationService>();
// Phase 5 — drill bank service.
builder.Services.AddScoped<OetLearner.Api.Services.Speaking.SpeakingDrillService>();
// Phase 6 — analytics + course pathway + interlocutor training services.
builder.Services.AddScoped<OetLearner.Api.Services.Speaking.SpeakingAnalyticsService>();
builder.Services.AddScoped<OetLearner.Api.Services.Speaking.SpeakingCoursePathwayService>();
builder.Services.AddScoped<OetLearner.Api.Services.Speaking.InterlocutorTrainingService>();
// DI hotfix (2026-05-06): these services are consumed directly by minimal-API
// endpoint handlers but were not registered, causing ASP.NET to fall back to
// inferring them as Body parameters and aborting host startup with
// "Body was inferred but the method does not allow inferred body parameters".
builder.Services.AddScoped<RemediationPlanService>();
builder.Services.AddScoped<AdminWalletTierService>();
builder.Services.AddScoped<NativeIapService>();
builder.Services.AddScoped<OetLearner.Api.Services.Content.MediaAssetAccessService>();
builder.Services.AddScoped<OetLearner.Api.Services.Content.MaterialAccessService>();
builder.Services.AddScoped<MockDiagnosticEntitlementService>();
builder.Services.AddScoped<IMockEntitlementService, MockEntitlementService>();
builder.Services.AddScoped<MockItemAnalysisService>();
builder.Services.AddScoped<OetLearner.Api.Services.Recalls.RecallsService>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.RefundService>();
builder.Services.AddScoped<OetLearner.Api.Services.Rulebooks.RulebookAdminService>();
builder.Services.AddScoped<OetLearner.Api.Services.Rulebooks.IWritingRulebookCoverageValidator,
    OetLearner.Api.Services.Rulebooks.WritingRulebookCoverageValidator>();
builder.Services.AddScoped<SpeakingTutorCalibrationService>();
builder.Services.AddScoped<IIeltsMockEngine, IeltsMockEngine>();
builder.Services.AddScoped<OetLearner.Api.Services.Entitlements.IEffectiveEntitlementResolver, OetLearner.Api.Services.Entitlements.EffectiveEntitlementResolver>();
// 15 service interfaces consumed by endpoint handlers but missing from DI;
// without these, minimal-API falls back to inferring them as Body params and
// crashes the host on startup with "Body was inferred but the method does not
// allow inferred body parameters".
builder.Services.AddScoped<OetLearner.Api.Services.IAIEscalationStatsService, OetLearner.Api.Services.AIEscalationStatsService>();
builder.Services.AddScoped<OetLearner.Api.Services.Content.IContentEntitlementService, OetLearner.Api.Services.Content.ContentEntitlementService>();
builder.Services.AddScoped<OetLearner.Api.Services.IContentStalenessService, OetLearner.Api.Services.ContentStalenessService>();
builder.Services.AddScoped<OetLearner.Api.Services.Listening.IListeningAnalyticsService, OetLearner.Api.Services.Listening.ListeningAnalyticsService>();
builder.Services.AddScoped<OetLearner.Api.Services.Listening.IListeningAuthoringService, OetLearner.Api.Services.Listening.ListeningAuthoringService>();
builder.Services.AddScoped<OetLearner.Api.Services.Listening.IListeningBackfillService, OetLearner.Api.Services.Listening.ListeningBackfillService>();
// Listening Part A AI-assisted data entry: Mistral OCR (PDF→Markdown) + Claude
// (structures into the canonical note-completion manifest). Drafts are staged
// for human review and approved through ListeningAuthoringService.ImportManifestAsync.
builder.Services.AddScoped<OetLearner.Api.Services.Ai.IMistralOcrClient, OetLearner.Api.Services.Ai.MistralOcrClient>();
builder.Services.AddScoped<OetLearner.Api.Services.Ai.IOcrService, OetLearner.Api.Services.Ai.OcrService>();
// Singleton-safe usage recorder for direct (non-gateway) AI calls — OCR, STT,
// and the Listening Part A Claude call. Scopes internally per record.
builder.Services.AddSingleton<OetLearner.Api.Services.Ai.IDirectAiCallRecorder, OetLearner.Api.Services.Ai.DirectAiCallRecorder>();
builder.Services.AddScoped<OetLearner.Api.Services.Listening.IListeningPartAExtractionService, OetLearner.Api.Services.Listening.ListeningPartAExtractionService>();
// Listening Part A AI marking (Claude Sonnet 4.6) — additive, non-blocking per-gap
// verdicts on top of the deterministic grade. The hosted poller is opt-in via
// `Listening:PartAAiScoring:Enabled` so it never runs in tests/CI and only marks
// when an anthropic provider + key are configured.
builder.Services.AddScoped<OetLearner.Api.Services.Listening.IListeningPartAAiScoringService, OetLearner.Api.Services.Listening.ListeningPartAAiScoringService>();
if (builder.Configuration.GetValue<bool>("Listening:PartAAiScoring:Enabled"))
{
    builder.Services.AddHostedService<OetLearner.Api.Services.Listening.ListeningPartAAiScoringWorker>();
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
                OetLearner.Api.Services.Listening.IListeningTtsSynthesisProvider,
                OetLearner.Api.Services.Listening.StubListeningTtsSynthesisProvider>();
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
                OetLearner.Api.Services.Listening.IListeningTtsSynthesisProvider,
                OetLearner.Api.Services.Listening.ElevenLabsListeningTtsSynthesisProvider>();
            break;
        default:
            throw new InvalidOperationException(
                $"Listening:TtsProvider '{ttsProvider}' is not registered. Add the provider's "
                + "DI registration above this switch or set the value to 'stub' (dev/CI) or 'elevenlabs'.");
    }
}
builder.Services.AddScoped<
    OetLearner.Api.Services.Listening.IListeningTtsService,
    OetLearner.Api.Services.Listening.ListeningTtsService>();
// TTS background job worker (polls ListeningTtsJobs table, runs synthesise
// jobs through whichever provider is registered above).
builder.Services.AddHostedService<OetLearner.Api.Services.Listening.ListeningTtsJobWorker>();
builder.Services.AddScoped<OetLearner.Api.Services.Listening.IListeningCurriculumService, OetLearner.Api.Services.Listening.ListeningCurriculumService>();
builder.Services.AddScoped<OetLearner.Api.Services.Listening.IListeningPathwayService, OetLearner.Api.Services.Listening.ListeningPathwayService>();
// ── Listening V2 — strategy + FSM + version-pinned grading + pathway + classes ──
builder.Services.AddSingleton<OetLearner.Api.Services.Listening.ListeningModePolicyResolver>();
builder.Services.AddSingleton<OetLearner.Api.Services.Listening.ListeningConfirmTokenService>();
// WS4 — admin sequence builder. Consumed by ListeningSessionService for
// per-state window resolution (null sequence ⇒ derived canonical fallback).
builder.Services.AddScoped<OetLearner.Api.Services.Listening.ListeningSequenceService>();
builder.Services.AddScoped<OetLearner.Api.Services.Listening.ListeningSessionService>();
builder.Services.AddScoped<OetLearner.Api.Services.Listening.ListeningGradingService>();
builder.Services.AddScoped<OetLearner.Api.Services.Listening.ListeningPathwayProgressService>();
builder.Services.AddScoped<OetLearner.Api.Services.Listening.TeacherClassService>();
builder.Services.AddScoped<OetLearner.Api.Services.Listening.IListeningExpertService, OetLearner.Api.Services.Listening.ListeningExpertService>();
builder.Services.AddScoped<OetLearner.Api.Services.Listening.IListeningPolicyService, OetLearner.Api.Services.Listening.ListeningPolicyService>();
builder.Services.AddHostedService<OetLearner.Api.Services.Listening.ListeningV2BackfillService>();
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IReadingAnalyticsService, OetLearner.Api.Services.Reading.ReadingAnalyticsService>();
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IReadingExtractionAi, OetLearner.Api.Services.Reading.GroundedReadingExtractionAi>();
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IReadingExtractionService, OetLearner.Api.Services.Reading.ReadingExtractionService>();
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IReadingPathwayService, OetLearner.Api.Services.Reading.ReadingPathwayService>();
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IReadingReviewService, OetLearner.Api.Services.Reading.ReadingReviewService>();
builder.Services.AddScoped<OetLearner.Api.Services.IWritingPdfService, OetLearner.Api.Services.WritingPdfService>();
builder.Services.AddScoped<OetLearner.Api.Services.ISpeakingPdfService, OetLearner.Api.Services.SpeakingPdfService>();
builder.Services.AddSingleton<OetLearner.Api.Services.IInvoicePdfService, OetLearner.Api.Services.InvoicePdfService>();
// WS9 (SPK-007) — scanned/text PDF import → structured Speaking draft.
builder.Services.AddScoped<OetLearner.Api.Services.Speaking.ISpeakingContentImportService,
    OetLearner.Api.Services.Speaking.SpeakingContentImportService>();
builder.Services.AddScoped<ISpeakingEvaluationPipeline, SpeakingEvaluationPipeline>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingEvaluationPipeline, OetLearner.Api.Services.Writing.WritingEvaluationPipeline>();
builder.Services.AddHostedService<OetLearner.Api.Services.Speaking.SpeakingAudioRetentionWorker>();
// Speaking module rebuild (2026-06-11) — server-authoritative exam auto-advance.
builder.Services.AddHostedService<OetLearner.Api.Services.Speaking.SpeakingExamAutoAdvanceWorker>();
builder.Services.AddScoped<ExpertService>();
builder.Services.AddScoped<ExpertOnboardingService>();
builder.Services.AddScoped<ExpertMessagingService>();
builder.Services.AddScoped<ExpertCompensationService>();
builder.Services.AddScoped<AdminAlertService>();
builder.Services.AddScoped<LearnerActionsService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<ILaunchReadinessService, LaunchReadinessService>();
builder.Services.AddScoped<SponsorService>();
builder.Services.AddScoped<ISponsorSeatPackService, SponsorSeatPackService>();
builder.Services.AddScoped<ContentHierarchyService>();
builder.Services.AddScoped<ContentDeduplicationService>();
builder.Services.AddScoped<ContentAccessService>();
builder.Services.AddScoped<MockDiagnosticService>();
builder.Services.AddScoped<ContentImportService>();
builder.Services.AddScoped<ContentSearchService>();
builder.Services.AddScoped<MediaNormalizationService>();
builder.Services.AddScoped<VideoLessonService>();
builder.Services.AddScoped<StrategyGuideService>();
builder.Services.AddScoped<OetLearner.Api.Services.Admin.UserHardDeleteService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<INotificationCampaignService, NotificationCampaignService>();
builder.Services.AddScoped<NotificationRuleEngine>();
builder.Services.AddScoped<PeerReviewService>();
builder.Services.Configure<OetLearner.Api.Configuration.SoketiOptions>(builder.Configuration.GetSection("Soketi"));
builder.Services.AddHttpClient("Soketi");
builder.Services.AddSingleton<OetLearner.Api.Services.DevicePairing.IDevicePairingCodeService, OetLearner.Api.Services.DevicePairing.InMemoryDevicePairingCodeService>();
builder.Services.AddScoped<AnalyticsIngestionService>();
builder.Services.AddSingleton<PlatformLinkService>();
builder.Services.AddHttpClient<StripeGateway>();
builder.Services.AddHttpClient<PayPalGateway>();
// Phase 2 international gateways (UK + Gulf + Egypt). HTTP clients are typed so each gateway gets its own pool.
builder.Services.AddHttpClient<OetLearner.Api.Services.Billing.Gateways.PayTabsGateway>();
builder.Services.AddHttpClient<OetLearner.Api.Services.Billing.Gateways.PaymobGateway>();
builder.Services.AddHttpClient<OetLearner.Api.Services.Billing.Gateways.CheckoutComGateway>();
builder.Services.AddScoped<PaymentGatewayService>();
builder.Services.AddScoped<IPaymentGatewayProvider>(sp => sp.GetRequiredService<PaymentGatewayService>());
// Phase 1-10 international expansion services.
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IGatewayRegistry, OetLearner.Api.Services.Billing.GatewayRegistry>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IRegionTaxResolver, OetLearner.Api.Services.Billing.TaxResolver>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IRegionDetector, OetLearner.Api.Services.Billing.RegionDetector>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IPriceResolver, OetLearner.Api.Services.Billing.PriceResolver>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IManualPaymentService, OetLearner.Api.Services.Billing.ManualPaymentService>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IDunningCampaignService, OetLearner.Api.Services.Billing.DunningCampaignService>();
// DunningCampaignService also implements IDunningService (resolved by the BillingDunningRetry background job); register that interface too so the job doesn't throw "No service registered".
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IDunningService, OetLearner.Api.Services.Billing.DunningCampaignService>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IAbandonedCartRecoveryService, OetLearner.Api.Services.Billing.AbandonedCartRecoveryService>();
builder.Services.AddHostedService<OetLearner.Api.Services.Billing.DunningWorker>();
builder.Services.AddHostedService<OetLearner.Api.Services.Billing.SubscriptionExpiryWorker>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IAffiliateService, OetLearner.Api.Services.Billing.AffiliateService>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IBillingNotificationDispatcher, OetLearner.Api.Services.Billing.BillingNotificationDispatcher>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IBillingNotificationChannel, OetLearner.Api.Services.Billing.EmailBillingChannel>();
builder.Services.Configure<OetLearner.Api.Configuration.TwilioOptions>(builder.Configuration.GetSection("Twilio"));
builder.Services.Configure<OetLearner.Api.Configuration.WhatsAppOptions>(builder.Configuration.GetSection("WhatsApp"));
builder.Services.AddHttpClient<OetLearner.Api.Services.Billing.TwilioSmsChannel>();
builder.Services.AddHttpClient<OetLearner.Api.Services.Billing.WhatsAppChannel>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IBillingNotificationChannel>(sp => sp.GetRequiredService<OetLearner.Api.Services.Billing.TwilioSmsChannel>());
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IBillingNotificationChannel>(sp => sp.GetRequiredService<OetLearner.Api.Services.Billing.WhatsAppChannel>());
builder.Services.AddScoped<OetLearner.Api.Services.Billing.ICouponVariantApplicator, OetLearner.Api.Services.Billing.CouponVariantApplicator>();
// AI churn / usage forecast / analytics / FX / experiments.
builder.Services.Configure<OetLearner.Api.Configuration.FxOptions>(builder.Configuration.GetSection("Fx"));
builder.Services.AddHttpClient<OetLearner.Api.Services.Billing.FxRateService>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IFxRateService>(sp => sp.GetRequiredService<OetLearner.Api.Services.Billing.FxRateService>());
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IPricingExperimentService, OetLearner.Api.Services.Billing.PricingExperimentService>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IChurnPredictionService, OetLearner.Api.Services.Billing.ChurnPredictionService>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IUsageForecastService, OetLearner.Api.Services.Billing.UsageForecastService>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IAiUsageAnalyticsService, OetLearner.Api.Services.Billing.AiUsageAnalyticsService>();
builder.Services.AddHostedService<OetLearner.Api.Services.Billing.ChurnPredictionWorker>();
builder.Services.AddHostedService<OetLearner.Api.Services.Billing.UsageForecastWorker>();
builder.Services.AddHostedService<OetLearner.Api.Services.Billing.FxRateRefreshWorker>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IRetentionActionDispatcher, OetLearner.Api.Services.Billing.RetentionActionDispatcher>();
builder.Services.AddHostedService<OetLearner.Api.Services.Billing.RetentionDispatchWorker>();
builder.Services.AddHostedService<OetLearner.Api.Services.Billing.ExperimentConversionWorker>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IBillingMetricsService, OetLearner.Api.Services.Billing.BillingMetricsService>();
builder.Services.AddHostedService<OetLearner.Api.Services.Billing.BillingMetricsRollupWorker>();
builder.Services.AddScoped<WalletService>();
builder.Services.AddScoped<EngagementService>();

// ── Billing V2 — Stripe catalog, cart, checkout, subscription ──
builder.Services.AddSingleton<OetLearner.Api.Services.Billing.IStripeService, OetLearner.Api.Services.Billing.StripeService>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IBillingCatalogService, OetLearner.Api.Services.Billing.BillingCatalogService>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IFulfillmentService, OetLearner.Api.Services.Billing.FulfillmentService>();
// Cart / checkout / subscription / promo-code commerce services. These back the
// /v1/cart, /v1/checkout, /v1/subscriptions/me and /v1/promo-codes endpoints;
// without these registrations route-building infers the service params as a
// request body and the app fails to boot.
builder.Services.AddScoped<OetLearner.Api.Services.Billing.ICartService, OetLearner.Api.Services.Billing.CartService>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.ICheckoutService, OetLearner.Api.Services.Billing.CheckoutService>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.ISubscriptionService, OetLearner.Api.Services.Billing.SubscriptionService>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IPromoCodeService, OetLearner.Api.Services.Billing.PromoCodeService>();
builder.Services.AddHostedService<BackgroundJobProcessor>();

// ── Phase 1 new services ──
builder.Services.AddScoped<GamificationService>();
builder.Services.AddSingleton<ISpacedRepetitionScheduler, Sm2Scheduler>();
builder.Services.AddScoped<SpacedRepetitionService>();

// ── Study planner engine (May 2026 overhaul) ──
builder.Services.AddScoped<OetLearner.Api.Services.Planner.IStudyPlanEntitlementResolver,
    OetLearner.Api.Services.Planner.StudyPlanEntitlementResolver>();
builder.Services.AddScoped<OetLearner.Api.Services.Planner.StudyPlanTemplateSelector>();
builder.Services.AddScoped<OetLearner.Api.Services.Planner.ContentPicker>();
builder.Services.AddScoped<OetLearner.Api.Services.Planner.ReviewItemInjector>();
builder.Services.AddScoped<OetLearner.Api.Services.Planner.IStudyPlanGenerator,
    OetLearner.Api.Services.Planner.StudyPlanGenerator>();
builder.Services.AddScoped<OetLearner.Api.Services.Planner.StudyPlanTemplateSeeder>();
builder.Services.AddScoped<OetLearner.Api.Services.Planner.IPlanPersonalizer,
    OetLearner.Api.Services.Planner.RuleBasedPlanPersonalizer>();
builder.Services.AddHostedService<OetLearner.Api.Services.Planner.StudyPlanReminderWorker>();
builder.Services.AddScoped<VocabularyService>();
// 2026-05-27 audit fix — Grammar + Remediation backend APIs (previously missing).
builder.Services.AddScoped<OetLearner.Api.Services.Grammar.IGrammarRulebookService, OetLearner.Api.Services.Grammar.GrammarRulebookService>();
builder.Services.AddScoped<OetLearner.Api.Services.Remediation.IRemediationApiService, OetLearner.Api.Services.Remediation.RemediationApiService>();
builder.Services.AddScoped<VocabularyDraftService>();
builder.Services.AddScoped<VocabularyGlossService>();
builder.Services.AddSingleton<OetLearner.Api.Services.Vocabulary.IVocabularyAudioQueue,
    OetLearner.Api.Services.Vocabulary.VocabularyAudioQueue>();
// Leader lock — only one replica runs the bulk audio operations (startup resume +
// reconciliation sweep). Postgres advisory lock in prod; always-leader otherwise.
builder.Services.AddSingleton<OetLearner.Api.Services.Vocabulary.IAudioWorkerLeaderLock>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var cs = OetLearner.Api.Data.DatabaseConfiguration.ResolveConnectionString(cfg, env.IsDevelopment());
    var isPostgres = !cs.StartsWith("InMemory:", StringComparison.OrdinalIgnoreCase)
        && cs.Contains("Host=", StringComparison.OrdinalIgnoreCase);
    return isPostgres
        ? new OetLearner.Api.Services.Vocabulary.PostgresAudioWorkerLeaderLock(
            cs, sp.GetRequiredService<ILogger<OetLearner.Api.Services.Vocabulary.PostgresAudioWorkerLeaderLock>>())
        : new OetLearner.Api.Services.Vocabulary.AlwaysLeaderLock();
});
builder.Services.AddHostedService<OetLearner.Api.Services.Vocabulary.VocabularyAudioWorker>();
builder.Services.AddScoped<OetLearner.Api.Services.VoiceDesign.IVoiceDesignRegenerationService,
    OetLearner.Api.Services.VoiceDesign.VoiceDesignRegenerationService>();
builder.Services.AddScoped<AdaptiveDifficultyService>();

// ── Phase 2 new services ──
builder.Services.AddScoped<PredictionService>();
builder.Services.AddScoped<ScoringService>();
builder.Services.AddScoped<ContentGenerationService>();
builder.Services.AddScoped<ConversationService>();

// ── Conversation subsystem ────────────────────────────────────────────────
builder.Services.Configure<OetLearner.Api.Configuration.ConversationOptions>(
    builder.Configuration.GetSection(OetLearner.Api.Configuration.ConversationOptions.SectionName));
foreach (var name in new[]
{
    "ConversationAzureClient", "ConversationWhisperClient", "ConversationDeepgramClient",
    "ConversationElevenLabsClient",
})
{
    builder.Services.AddHttpClient(name, c => { c.Timeout = TimeSpan.FromMinutes(2); });
}
builder.Services.AddScoped<OetLearner.Api.Services.Conversation.Asr.IConversationAsrProvider,
    OetLearner.Api.Services.Conversation.Asr.MockConversationAsrProvider>();
builder.Services.AddScoped<OetLearner.Api.Services.Conversation.Asr.IConversationAsrProvider,
    OetLearner.Api.Services.Conversation.Asr.AzureConversationAsrProvider>();
builder.Services.AddScoped<OetLearner.Api.Services.Conversation.Asr.IConversationAsrProvider,
    OetLearner.Api.Services.Conversation.Asr.WhisperConversationAsrProvider>();
builder.Services.AddScoped<OetLearner.Api.Services.Conversation.Asr.IConversationAsrProvider,
    OetLearner.Api.Services.Conversation.Asr.DeepgramConversationAsrProvider>();
builder.Services.AddScoped<OetLearner.Api.Services.Conversation.Asr.IConversationRealtimeAsrProvider,
    OetLearner.Api.Services.Conversation.Asr.MockConversationRealtimeAsrProvider>();
builder.Services.AddScoped<OetLearner.Api.Services.Conversation.Asr.IConversationRealtimeAsrProvider,
    OetLearner.Api.Services.Conversation.Asr.ElevenLabsConversationRealtimeAsrProvider>();
builder.Services.AddScoped<OetLearner.Api.Services.Conversation.Asr.IConversationAsrProviderSelector,
    OetLearner.Api.Services.Conversation.Asr.ConversationAsrProviderSelector>();

// RW-012 — admin-managed PDF / OCR provider selector. Concrete
// IPaperExtractionProvider implementations register against this hook;
// the selector resolves the active row at call time so admins can rotate
// providers via /admin/ai-providers without a redeploy.
builder.Services.AddScoped<OetLearner.Api.Services.Content.IPaperExtractionProviderSelector,
    OetLearner.Api.Services.Content.PaperExtractionProviderSelector>();

builder.Services.AddScoped<OetLearner.Api.Services.Conversation.Tts.IConversationTtsProvider,
    OetLearner.Api.Services.Conversation.Tts.ElevenLabsConversationTtsProvider>();
builder.Services.AddScoped<OetLearner.Api.Services.Conversation.Tts.IConversationTtsProviderSelector,
    OetLearner.Api.Services.Conversation.Tts.ConversationTtsProviderSelector>();

builder.Services.AddScoped<OetLearner.Api.Services.Conversation.IConversationAudioService,
    OetLearner.Api.Services.Conversation.ConversationAudioService>();
builder.Services.AddScoped<OetLearner.Api.Services.Conversation.IConversationTranscriptExportService,
    OetLearner.Api.Services.Conversation.ConversationTranscriptExportService>();
builder.Services.AddSingleton<OetLearner.Api.Services.Conversation.IConversationOptionsProvider,
    OetLearner.Api.Services.Conversation.ConversationOptionsProvider>();
builder.Services.AddSingleton<OetLearner.Api.Services.Settings.IRuntimeSettingsProvider,
    OetLearner.Api.Services.Settings.RuntimeSettingsProvider>();
builder.Services.AddSingleton<OetLearner.Api.Services.Conversation.ConversationRealtimeTurnStore>();
builder.Services.AddScoped<OetLearner.Api.Services.Conversation.IConversationEntitlementService,
    OetLearner.Api.Services.Conversation.ConversationEntitlementService>();
builder.Services.AddScoped<OetLearner.Api.Services.Conversation.IConversationAiOrchestrator,
    OetLearner.Api.Services.Conversation.ConversationAiOrchestrator>();
builder.Services.AddHostedService<OetLearner.Api.Services.Conversation.ConversationAudioRetentionWorker>();
builder.Services.AddScoped<PronunciationService>();
builder.Services.AddScoped<WritingCoachService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingLearnerPathwayService,
    OetLearner.Api.Services.Writing.WritingLearnerPathwayService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingLessonService,
    OetLearner.Api.Services.Writing.WritingLessonService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingDrillService,
    OetLearner.Api.Services.Writing.WritingDrillService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingCaseNoteDrillService,
    OetLearner.Api.Services.Writing.WritingCaseNoteDrillService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.WritingWeaknessAnalyticsService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.WritingDualAssessmentService>();
builder.Services.AddScoped<OetLearner.Api.Services.Expert.IExpertAssignmentNotifier,
    OetLearner.Api.Services.Expert.ExpertAssignmentNotifier>();
builder.Services.AddScoped<OetLearner.Api.Services.Expert.IExpertAutoAssignmentService,
    OetLearner.Api.Services.Expert.ExpertAutoAssignmentService>();
builder.Services.Configure<OetLearner.Api.Configuration.ExpertAutoAssignmentOptions>(
    builder.Configuration.GetSection(OetLearner.Api.Configuration.ExpertAutoAssignmentOptions.SectionName));
builder.Services.AddScoped<MarketplaceService>();

// ── Pronunciation subsystem (Phase 2+) ───────────────────────────────────
builder.Services.Configure<OetLearner.Api.Configuration.PronunciationOptions>(
    builder.Configuration.GetSection(OetLearner.Api.Configuration.PronunciationOptions.SectionName));
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
builder.Services.AddScoped<OetLearner.Api.Services.Pronunciation.IPronunciationAsrProvider,
    OetLearner.Api.Services.Pronunciation.MockPronunciationAsrProvider>();
builder.Services.AddScoped<OetLearner.Api.Services.Pronunciation.IPronunciationAsrProvider,
    OetLearner.Api.Services.Pronunciation.AzurePronunciationAsrProvider>();
// Phase 6c: register the concrete Azure provider too so the phoneme adapter
// below can take it directly (same scope; same HttpClient handler pool).
builder.Services.AddScoped<OetLearner.Api.Services.Pronunciation.AzurePronunciationAsrProvider>();
builder.Services.AddScoped<OetLearner.Api.Services.Pronunciation.IPronunciationAsrProvider,
    OetLearner.Api.Services.Pronunciation.WhisperPronunciationAsrProvider>();
builder.Services.AddScoped<OetLearner.Api.Services.Pronunciation.IPronunciationAsrProvider,
    OetLearner.Api.Services.Pronunciation.GeminiPronunciationAsrProvider>();
builder.Services.AddScoped<OetLearner.Api.Services.Pronunciation.IPronunciationAsrProviderSelector,
    OetLearner.Api.Services.Pronunciation.PronunciationAsrProviderSelector>();
// Phase 6c: registry-first credential resolver (singleton, 30s cache) +
// scaffolding interface + Azure adapter for phoneme scoring. The live
// grading path still routes through the ASR selector — the phoneme
// interface is currently visibility-only (Phase 6d will move grading
// to it once production traffic is verified stable).
builder.Services.AddSingleton<OetLearner.Api.Services.Pronunciation.IPronunciationCredentialResolver,
    OetLearner.Api.Services.Pronunciation.PronunciationCredentialResolver>();
builder.Services.AddScoped<OetLearner.Api.Services.Pronunciation.IPronunciationPhonemeProvider,
    OetLearner.Api.Services.Pronunciation.AzurePronunciationPhonemeProvider>();
builder.Services.AddScoped<OetLearner.Api.Services.Pronunciation.IPronunciationFeedbackService,
    OetLearner.Api.Services.Pronunciation.PronunciationFeedbackService>();
builder.Services.AddScoped<OetLearner.Api.Services.Pronunciation.IPronunciationSchedulerService,
    OetLearner.Api.Services.Pronunciation.PronunciationSchedulerService>();
builder.Services.AddScoped<OetLearner.Api.Services.Pronunciation.IPronunciationEntitlementService,
    OetLearner.Api.Services.Pronunciation.PronunciationEntitlementService>();
builder.Services.AddScoped<OetLearner.Api.Services.Pronunciation.IPronunciationAdminDraftService,
    OetLearner.Api.Services.Pronunciation.PronunciationAdminDraftService>();
builder.Services.AddHostedService<OetLearner.Api.Services.Pronunciation.PronunciationAudioRetentionWorker>();

// Retention sweeper for auth-layer rows (expired OTPs, revoked refresh tokens)
// that would otherwise bloat their tables and indexes forever.
builder.Services.AddHostedService<OetLearner.Api.Services.Auth.AuthDataRetentionWorker>();

// Retention sweeper for append-only event tables (analytics, audit, payment
// webhooks, notification delivery attempts). Windows are configured via the
// "DataRetention" section; defaults are conservative (see DataRetentionOptions).
builder.Services.Configure<OetLearner.Api.Configuration.DataRetentionOptions>(
    builder.Configuration.GetSection("DataRetention"));
builder.Services.AddHostedService<OetLearner.Api.Services.DataRetentionWorker>();

// Billing-hardening I-9: tiered PII retention. Nulls the payload column on
// PaymentWebhookEvents at the PaymentWebhookPiiNullOutAge cutoff (default
// 90 days) while keeping the event metadata for forensic chain-of-custody.
// The companion DataRetentionWorker deletes the row entirely at the longer
// PaymentWebhookEvents cutoff (default 180 days).
builder.Services.AddHostedService<OetLearner.Api.Services.Billing.WebhookPiiRetentionWorker>();

// Mocks Wave 5: dispatches the 24-h / 2-h / 30-min pre-booking reminder
// notifications for upcoming MockBookings. Idempotent via the
// NotificationService dedupe-key contract; ticks every 5 min.
builder.Services.AddHostedService<OetLearner.Api.Services.Mocks.MockBookingReminderWorker>();

// Partition-maintenance worker: keeps next-month range partitions pre-created
// for candidate time-ordered tables (AnalyticsEvents, AuditEvents, AiUsageRecords).
// No-op on SQLite and no-op on a Postgres DB whose tables are not yet partitioned.
builder.Services.AddHostedService<OetLearner.Api.Services.PartitionMaintenanceWorker>();

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
builder.Services.AddSingleton<OetLearner.Api.Services.Rulebook.RulebookLoader>();
builder.Services.AddScoped<OetLearner.Api.Services.Rulebook.IRulebookLoader,
    OetLearner.Api.Services.Rulebook.DbBackedRulebookLoader>();
builder.Services.AddScoped<OetLearner.Api.Services.Rulebook.WritingRuleEngine>();
builder.Services.AddScoped<OetLearner.Api.Services.Rulebook.SpeakingRuleEngine>();
builder.Services.AddScoped<OetLearner.Api.Services.Rulebook.IAiUsageRecorder,
    OetLearner.Api.Services.Rulebook.AiUsageRecorder>();
builder.Services.AddHttpClient("AiOpenAiCompatible", client =>
{
    client.Timeout = TimeSpan.FromMinutes(30);
    if (!string.IsNullOrWhiteSpace(aiProviderOptions.BaseUrl))
    {
        client.BaseAddress = new Uri(aiProviderOptions.BaseUrl.TrimEnd('/') + "/");
    }
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<OetLearner.Api.Services.Rulebook.IAiModelProvider,
    OetLearner.Api.Services.Rulebook.MockAiProvider>();
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
    builder.Services.AddScoped<OetLearner.Api.Services.Rulebook.IAiModelProvider,
        OetLearner.Api.Services.Rulebook.OpenAiCompatibleProvider>();
}
builder.Services.AddMemoryCache();
builder.Services.AddScoped<OetLearner.Api.Services.AiManagement.IAiQuotaService,
    OetLearner.Api.Services.AiManagement.AiQuotaService>();
builder.Services.AddHttpClient("AiCredentialValidator");
builder.Services.AddScoped<OetLearner.Api.Services.AiManagement.IAiCredentialVault,
    OetLearner.Api.Services.AiManagement.AiCredentialVault>();
builder.Services.AddScoped<OetLearner.Api.Services.AiManagement.IAiCredentialResolver,
    OetLearner.Api.Services.AiManagement.AiCredentialResolver>();
builder.Services.AddHttpClient("AiRegistryClient", c => c.Timeout = TimeSpan.FromMinutes(30))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<OetLearner.Api.Services.Rulebook.IAiProviderRegistry,
    OetLearner.Api.Services.Rulebook.AiProviderRegistry>();
// Multi-account pool registry — siblings of AiProviderRegistry, used by
// Copilot-style providers (Phase 2 of GitHub Copilot integration). The
// registry guarantees atomic pick + counter increment via
// ExecuteUpdateAsync; see AiProviderAccountRegistry XML doc.
builder.Services.AddScoped<OetLearner.Api.Services.Rulebook.IAiProviderAccountRegistry,
    OetLearner.Api.Services.Rulebook.AiProviderAccountRegistry>();
// Phase 7: per-feature routing override resolver. Consulted by the
// gateway between explicit pins and the registry-default fallback.
builder.Services.AddScoped<OetLearner.Api.Services.Rulebook.IAiFeatureRouteResolver,
    OetLearner.Api.Services.Rulebook.AiFeatureRouteResolver>();
// Phase 4: admin connectivity probe. Bypasses gateway grounding +
// quota on purpose — see AiProviderConnectionTester XML doc.
builder.Services.AddHttpClient(nameof(OetLearner.Api.Services.Rulebook.AiProviderConnectionTester))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<OetLearner.Api.Services.Rulebook.IAiProviderConnectionTester,
    OetLearner.Api.Services.Rulebook.AiProviderConnectionTester>();
builder.Services.AddScoped<OetLearner.Api.Services.Rulebook.IAiModelProvider,
    OetLearner.Api.Services.Rulebook.RegistryBackedProvider>();
builder.Services.AddScoped<OetLearner.Api.Services.Rulebook.IAiModelProvider,
    OetLearner.Api.Services.Rulebook.AnthropicProvider>();
builder.Services.AddScoped<OetLearner.Api.Services.Rulebook.IAiModelProvider,
    OetLearner.Api.Services.Rulebook.GeminiNativeProvider>();
builder.Services.AddScoped<OetLearner.Api.Services.Rulebook.IAiModelProvider,
    OetLearner.Api.Services.Rulebook.CloudflareWorkersAiProvider>();
// GitHub Copilot / Models adapter — uses the official `Azure.AI.Inference`
// typed SDK (ChatCompletionsClient + AzureKeyCredential) against the
// chat-completions endpoint at the registered base URL (default
// https://models.github.ai/inference). DB-backed credentials live in the
// AiProviders row keyed by Code="copilot"; Phase 2 will extend this to
// a multi-account pool with auto-failover. The SDK owns its own pipeline
// (retry / timeout / transport) so no IHttpClientFactory entry is needed.
builder.Services.AddScoped<OetLearner.Api.Services.Rulebook.IAiModelProvider,
    OetLearner.Api.Services.Rulebook.CopilotAiModelProvider>();
builder.Services.AddScoped<OetLearner.Api.Services.AiManagement.IAiCreditService,
    OetLearner.Api.Services.AiManagement.AiCreditService>();
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IAiPackageCreditService,
    OetLearner.Api.Services.Billing.AiPackageCreditService>();
builder.Services.AddHostedService<OetLearner.Api.Services.AiManagement.AiCreditRenewalWorker>();
builder.Services.AddHostedService<OetLearner.Api.Services.AiManagement.AiAccountQuotaResetWorker>();

// Content Upload subsystem (Slice 2). IFileStorage sits in front of disk
// access. Wave 4: provider is runtime-selected via Storage:Provider.
builder.Services.AddSingleton<OetLearner.Api.Services.Content.IHtmlSanitizer,
    OetLearner.Api.Services.Content.HtmlSanitizerService>();

// Storage provider selector — "s3" activates S3CompatibleFileStorage.
var storageProvider = storageOptions?.Provider ?? "local";
if (storageProvider.Equals("s3", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<OetLearner.Api.Services.Content.IFileStorage,
        OetLearner.Api.Services.Content.S3CompatibleFileStorage>();
}
else
{
    builder.Services.AddSingleton<OetLearner.Api.Services.Content.IFileStorage,
        OetLearner.Api.Services.Content.LocalFileStorage>();
}
builder.Services.AddSingleton<OetLearner.Api.Services.Content.IUploadContentValidator,
    OetLearner.Api.Services.Content.MagicByteValidator>();
// Upload antivirus scanner. Effective provider is runtime-admin configurable
// with env/appsettings fallback. In production we REFUSE to boot unless the
// effective provider is a real scanner — see the fail-fast check below.
builder.Services.Configure<UploadScannerOptions>(builder.Configuration.GetSection("UploadScanner"));
builder.Services.AddSingleton<OetLearner.Api.Services.Content.IUploadScanner,
    OetLearner.Api.Services.Content.ClamAvUploadScanner>();
builder.Services.AddScoped<OetLearner.Api.Services.Content.IChunkedUploadService,
    OetLearner.Api.Services.Content.ChunkedUploadService>();
builder.Services.AddScoped<OetLearner.Api.Services.Content.IContentPaperService,
    OetLearner.Api.Services.Content.ContentPaperService>();
builder.Services.AddSingleton<OetLearner.Api.Services.Content.IContentConventionParser,
    OetLearner.Api.Services.Content.ContentConventionParser>();
builder.Services.AddScoped<OetLearner.Api.Services.Content.IContentBulkImportService,
    OetLearner.Api.Services.Content.ContentBulkImportService>();
builder.Services.AddScoped<OetLearner.Api.Services.Content.RealContentFolderImporter>();
builder.Services.Configure<PdfExtractionOptions>(
    builder.Configuration.GetSection(PdfExtractionOptions.SectionName));
builder.Services.AddHttpClient("AzureDocIntel");
builder.Services.AddSingleton<OetLearner.Api.Services.Content.PdfPigPdfTextExtractor>();
builder.Services.AddSingleton<OetLearner.Api.Services.Content.IPdfTextExtractor,
    OetLearner.Api.Services.Content.AutoPdfTextExtractor>();
builder.Services.AddScoped<OetLearner.Api.Services.Content.IContentTextExtractionService,
    OetLearner.Api.Services.Content.ContentTextExtractionService>();
builder.Services.AddHostedService<OetLearner.Api.Services.Content.ContentTextExtractionWorker>();

// Writing sample seeder — one-shot startup load of canonical OET Writing 1-6
// papers from the bundled seed JSON. Disabled by default; enable per-environment
// with Content:WritingSeed:Enabled=true. Idempotent: skips rows whose
// SourceProvenance already matches the seed id.
builder.Services.Configure<WritingSeedOptions>(
    builder.Configuration.GetSection(WritingSeedOptions.SectionName));
builder.Services.AddHostedService<OetLearner.Api.Services.Content.WritingSampleSeeder>();

// OET 2026 catalog seeder — startup load of 20+ BillingPlans + 7 BillingAddOns +
// matching ContentPackages from Data/Seeds/oet-2026-catalog.json. Disabled by
// default; enable with Content:Oet2026Catalog:Enabled=true. Idempotent UPSERT
// on Code.
builder.Services.Configure<Oet2026CatalogSeedOptions>(
    builder.Configuration.GetSection(Oet2026CatalogSeedOptions.SectionName));
// Register as singleton + hosted service so the admin reseed endpoint can
// resolve the same instance.
builder.Services.AddSingleton<OetLearner.Api.Services.Billing.Oet2026CatalogSeeder>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<OetLearner.Api.Services.Billing.Oet2026CatalogSeeder>());

// Wave B4 — sync canonical Stripe seed catalogue (stripe-product-catalog.v1.json)
// into BillingProducts + BillingPrices on every boot. Idempotent UPSERT on
// metadata.code; the canonical JSON also drives the StripeProductSeeder CLI so
// the local DB and the live Stripe account stay aligned.
builder.Services.AddSingleton<OetLearner.Api.Services.Billing.BillingCatalogSyncStartupTask>();
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<OetLearner.Api.Services.Billing.BillingCatalogSyncStartupTask>());

// Add-on eligibility service — enforces three-flag rule + Tutor Book
// double-charge guard. Called from /v1/billing/quote/addon and the
// checkout session creator.
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IAddonEligibilityService,
    OetLearner.Api.Services.Billing.AddonEligibilityService>();

// Add-on grant processor — applies / reverses entitlement grants after
// successful payment / refund webhook. Idempotent on (subscription_id,
// addon_version_id, payment_event_id).
builder.Services.AddScoped<OetLearner.Api.Services.Billing.IAddonGrantProcessor,
    OetLearner.Api.Services.Billing.AddonGrantProcessor>();

// Tutor Book watermarking — stamps PDF with buyer + HMAC fingerprint so
// leaks are traceable.
builder.Services.AddSingleton<OetLearner.Api.Services.TutorBook.ITutorBookWatermarkService,
    OetLearner.Api.Services.TutorBook.TutorBookWatermarkService>();
// Reading Authoring subsystem (Slices R1–R7).
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IReadingStructureService,
    OetLearner.Api.Services.Reading.ReadingStructureService>();
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IReadingPolicyService,
    OetLearner.Api.Services.Reading.ReadingPolicyService>();
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IReadingGradingService,
    OetLearner.Api.Services.Reading.ReadingGradingService>();
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IReadingAttemptService,
    OetLearner.Api.Services.Reading.ReadingAttemptService>();
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IReadingTutorService,
    OetLearner.Api.Services.Reading.ReadingTutorService>();
// Reading Pathway subsystem (Reading Module Pathway Plan).
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IReadingLearnerPathwayService, OetLearner.Api.Services.Reading.ReadingLearnerPathwayService>();
builder.Services.AddScoped<OetLearner.Api.Services.Reading.ISkillScoringService, OetLearner.Api.Services.Reading.SkillScoringService>();
// Listening Pathway subsystem (OET_LISTENING_MODULE_PATHWAY.md §5–§6 / A6+A7).
// IListeningPathwayGenerator is a pure-function service so registering it as
// a singleton avoids per-request allocation; the others touch DbContext.
builder.Services.AddScoped<OetLearner.Api.Services.Listening.IListeningLearnerPathwayService, OetLearner.Api.Services.Listening.ListeningLearnerPathwayService>();
builder.Services.AddScoped<OetLearner.Api.Services.Listening.IListeningSkillScoringService, OetLearner.Api.Services.Listening.ListeningSkillScoringService>();
builder.Services.AddScoped<OetLearner.Api.Services.Listening.IListeningLearnerGradingService, OetLearner.Api.Services.Listening.ListeningLearnerGradingService>();
builder.Services.AddScoped<OetLearner.Api.Services.Listening.IListeningLessonService, OetLearner.Api.Services.Listening.ListeningLessonService>();
builder.Services.AddScoped<OetLearner.Api.Services.Listening.IListeningStrategyService, OetLearner.Api.Services.Listening.ListeningStrategyService>();
builder.Services.AddScoped<OetLearner.Api.Services.Listening.IListeningMockService, OetLearner.Api.Services.Listening.ListeningMockService>();
// Listening dictation drill subsystem (Phase 4 of OET_LISTENING_MODULE_PATHWAY.md §14).
builder.Services.AddScoped<OetLearner.Api.Services.Listening.IDictationService, OetLearner.Api.Services.Listening.DictationService>();
// Listening pronunciation library — SM-2 spaced repetition (Phase 4 of §15).
builder.Services.AddScoped<OetLearner.Api.Services.Listening.IPronunciationService, OetLearner.Api.Services.Listening.PronunciationService>();
// Phase 3 daily plan + adaptive practice selection (§8, §10).
builder.Services.AddScoped<OetLearner.Api.Services.Listening.IListeningDailyPlanService, OetLearner.Api.Services.Listening.ListeningDailyPlanService>();
builder.Services.AddScoped<OetLearner.Api.Services.Listening.IListeningPracticeSelectionService, OetLearner.Api.Services.Listening.ListeningPracticeSelectionService>();
builder.Services.AddSingleton<OetLearner.Api.Services.Listening.IListeningPathwayGenerator, OetLearner.Api.Services.Listening.ListeningPathwayGenerator>();
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IDailyPlanService, OetLearner.Api.Services.Reading.DailyPlanService>();
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IPracticeSelectionService, OetLearner.Api.Services.Reading.PracticeSelectionService>();
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IReviewQueueService, OetLearner.Api.Services.Reading.ReviewQueueService>();
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IReadingVocabularyService, OetLearner.Api.Services.Reading.ReadingVocabularyService>();
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IReadingExplanationService, OetLearner.Api.Services.Reading.ReadingExplanationService>();
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IStreakService, OetLearner.Api.Services.Reading.StreakService>();
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IXpService, OetLearner.Api.Services.Reading.XpService>();
builder.Services.AddScoped<OetLearner.Api.Services.Reading.ILessonService, OetLearner.Api.Services.Reading.LessonService>();
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IStrategyService, OetLearner.Api.Services.Reading.StrategyService>();
builder.Services.AddScoped<OetLearner.Api.Services.Listening.ListeningLearnerService>();
builder.Services.AddScoped<OetLearner.Api.Services.Listening.IListeningStructureService,
    OetLearner.Api.Services.Listening.ListeningStructureService>();
// Listening sample ingester (Slice E of docs/LISTENING-INGESTION-PRD.md).
// Disabled by default — operator opts in via Seed:ListeningSamples:Enabled=true.
builder.Services.Configure<OetLearner.Api.Services.Listening.ListeningSampleSeederOptions>(
    builder.Configuration.GetSection(OetLearner.Api.Services.Listening.ListeningSampleSeederOptions.SectionName));
builder.Services.Configure<OetLearner.Api.Services.Listening.ListeningStarterContentSeederOptions>(
    builder.Configuration.GetSection(OetLearner.Api.Services.Listening.ListeningStarterContentSeederOptions.SectionName));
builder.Services.AddScoped<
    OetLearner.Api.Services.Listening.IListeningStarterContentSeeder,
    OetLearner.Api.Services.Listening.ListeningStarterContentSeeder>();
builder.Services.AddScoped<
    OetLearner.Api.Services.Listening.IListeningSampleSeeder,
    OetLearner.Api.Services.Listening.ListeningSampleSeeder>();

// Listening diagnostic seeder — Stage 2 of OET_LISTENING_MODULE_PATHWAY.md §6.1.
// Idempotently materialises the 23-item Phase 1 diagnostic mini-test. Disabled
// by default — opt in via Seed:ListeningDiagnostic:Enabled=true.
builder.Services.Configure<OetLearner.Api.Services.Listening.ListeningDiagnosticSeederOptions>(
    builder.Configuration.GetSection(OetLearner.Api.Services.Listening.ListeningDiagnosticSeederOptions.SectionName));
builder.Services.AddScoped<OetLearner.Api.Services.Listening.ListeningDiagnosticSeeder>();

// Mock sample seeder (Development only). On startup, ingests three fully-
// assembled draft MockBundle rows from `Project Real Content/` so the admin
// (and learner) can immediately preview the new mock wizard + flow.
// Idempotent (slug-keyed); non-fatal on failure.
builder.Services.Configure<OetLearner.Api.Services.Seeding.MockSampleSeederOptions>(
    builder.Configuration.GetSection(OetLearner.Api.Services.Seeding.MockSampleSeederOptions.SectionName));
builder.Services.AddScoped<OetLearner.Api.Services.Seeding.MockSampleSeeder>();
builder.Services.AddHostedService<OetLearner.Api.Services.Reading.ReadingAttemptExpireWorker>();
builder.Services.AddHostedService<OetLearner.Api.Services.Listening.ListeningAttemptExpireWorker>();
builder.Services.AddHostedService<OetLearner.Api.Services.Content.AdminUploadCleanupWorker>();
builder.Services.AddScoped<OetLearner.Api.Services.Rulebook.IAiGatewayService,
    OetLearner.Api.Services.Rulebook.AiGatewayService>();

// ── Writing Module V2 prompt templates (OET_WRITING_MODULE_PATHWAY.md §12+§13) ──
// Singleton registry so the 10 templates (coach, rewrite, scenario.generate,
// exemplar.embed, appeal, canon.detect, drill.grade, outline, paraphrase, ask)
// resolve consistently across every Writing V2 service. The registrar runs
// inline below so prompt-template misconfig fails fast at boot, not at the
// first learner-visible request — every expected feature code is probed via
// WritingPromptTemplateRegistrar.AssertAllExpectedTemplatesRegistered.
builder.Services.AddSingleton<OetLearner.Api.Services.Rulebook.IWritingPromptTemplateRegistry>(_ =>
{
    var registry = new OetLearner.Api.Services.Rulebook.WritingPromptTemplateRegistry();
    OetLearner.Api.Services.Rulebook.WritingPromptTemplateRegistrar.RegisterWritingV2Templates(registry);
    return registry;
});

// ── Phase 5 — AI Tool Calling ──
// All AI-tool services are Scoped so the registry, invoker, and executors
// share the per-request LearnerDbContext. The grant cache remains process-
// wide because IMemoryCache is a Singleton — InvalidateFeature() therefore
// fans out to every subsequent request regardless of which scope mutates
// it. (Phase 6c hardening — making the registry Singleton crashed DI
// validation because it captured Scoped IEnumerable<IAiToolExecutor>.)
builder.Services.Configure<OetLearner.Api.Services.AiTools.AiToolOptions>(
    builder.Configuration.GetSection("AiTool"));
builder.Services.AddScoped<OetLearner.Api.Services.AiTools.IAiToolRegistry,
    OetLearner.Api.Services.AiTools.AiToolRegistry>();
builder.Services.AddScoped<OetLearner.Api.Services.AiTools.IAiToolInvoker,
    OetLearner.Api.Services.AiTools.AiToolInvoker>();

// 7-tool catalog (Phase 5 v1). Deny-by-default — admins grant per feature.
builder.Services.AddScoped<OetLearner.Api.Services.AiTools.IAiToolExecutor,
    OetLearner.Api.Services.AiTools.Tools.LookupRulebookRuleTool>();
builder.Services.AddScoped<OetLearner.Api.Services.AiTools.IAiToolExecutor,
    OetLearner.Api.Services.AiTools.Tools.LookupVocabularyTermTool>();
builder.Services.AddScoped<OetLearner.Api.Services.AiTools.IAiToolExecutor,
    OetLearner.Api.Services.AiTools.Tools.GetUserRecentAttemptsTool>();
builder.Services.AddScoped<OetLearner.Api.Services.AiTools.IAiToolExecutor,
    OetLearner.Api.Services.AiTools.Tools.SearchRecallSetTool>();
builder.Services.AddScoped<OetLearner.Api.Services.AiTools.IAiToolExecutor,
    OetLearner.Api.Services.AiTools.Tools.SaveUserNoteTool>();
builder.Services.AddScoped<OetLearner.Api.Services.AiTools.IAiToolExecutor,
    OetLearner.Api.Services.AiTools.Tools.BookmarkRecallTermTool>();
builder.Services.AddScoped<OetLearner.Api.Services.AiTools.IAiToolExecutor,
    OetLearner.Api.Services.AiTools.Tools.FetchDictionaryDefinitionTool>();

// External-network tool HTTP client — strict timeout, no auto-redirect, no
// proxy passthrough. The tool itself enforces host allowlist + max-bytes.
builder.Services.AddHttpClient(
    OetLearner.Api.Services.AiTools.Tools.FetchDictionaryDefinitionTool.HttpClientName,
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
builder.Services.AddHostedService<OetLearner.Api.Services.AiTools.AiToolCatalogSeederHostedService>();

// AI Assistant orchestrator (Phase A–H). Multi-role conversational agent.
builder.Services.Configure<OetLearner.Api.Services.AiAssistant.AiAssistantOptions>(
    builder.Configuration.GetSection("AiAssistant"));
builder.Services.AddSingleton<OetLearner.Api.Services.AiAssistant.SystemPrompts.ISystemPromptProvider,
    OetLearner.Api.Services.AiAssistant.SystemPrompts.SystemPromptProvider>();
builder.Services.AddScoped<OetLearner.Api.Services.AiAssistant.IAiAssistantOrchestrator,
    OetLearner.Api.Services.AiAssistant.AiAssistantOrchestrator>();
builder.Services.AddScoped<OetLearner.Api.Services.AiAssistant.IAiAssistantGateway,
    OetLearner.Api.Services.AiAssistant.AiAssistantGateway>();

// Phase 6b — backfill voice provider rows (TTS/ASR/Phoneme) into the
// AiProviders registry so admins see them in /admin/ai-providers.
// Strictly additive: never overwrites existing rows; selectors still
// read credentials from existing options sources.
builder.Services.AddHostedService<OetLearner.Api.Services.Voice.AiVoiceProviderSeeder>();
// Registered AFTER the voice seeder so an env-derived whisper-asr row (if any)
// wins creation; this seeder only backfills missing canonical rows keyless so
// admins can paste a key in /admin/ai-providers and the integration just works.
builder.Services.AddHostedService<OetLearner.Api.Services.Ai.CoreAiProviderSeeder>();
builder.Services.AddHostedService<OetLearner.Api.Services.AiAssistant.AiAssistantFeatureRouteSeeder>();
builder.Services.AddScoped<OetLearner.Api.Services.Grammar.IGrammarDraftService,
    OetLearner.Api.Services.Grammar.GrammarDraftService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingDraftService,
    OetLearner.Api.Services.Writing.WritingDraftService>();
builder.Services.AddScoped<OetLearner.Api.Services.Grammar.IGrammarPublishGateService,
    OetLearner.Api.Services.Grammar.GrammarPublishGateService>();
builder.Services.AddScoped<OetLearner.Api.Services.Grammar.IGrammarEntitlementService,
    OetLearner.Api.Services.Grammar.GrammarEntitlementService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingOptionsProvider,
    OetLearner.Api.Services.Writing.WritingOptionsProvider>();
builder.Services.AddScoped<IWritingEntitlementService,
    OetLearner.Api.Services.Writing.WritingEntitlementService>();

// ── Writing Module V2 services and crons (OET_WRITING_MODULE_PATHWAY.md §WS5) ──
// Config root bound from appsettings.json:Writing.* — feature flags,
// daily caps, cron toggles, OCR provider keys.
builder.Services.Configure<OetLearner.Api.Services.Writing.Configuration.WritingV2Options>(
    builder.Configuration.GetSection(OetLearner.Api.Services.Writing.Configuration.WritingV2Options.SectionName));
// Event bus is singleton — opens scopes per dispatch so handlers see fresh DbContext.
builder.Services.AddSingleton<OetLearner.Api.Services.Writing.Events.IWritingEventBus,
    OetLearner.Api.Services.Writing.Events.WritingEventBus>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.Events.IWritingEventHandler<OetLearner.Api.Services.Writing.Events.WritingGradeReady>,
    OetLearner.Api.Services.Writing.Events.WritingGradeReadyHubEventHandler>();
// Canon engine — scoped so it can consume scoped IAiGatewayService;
// internal compiled-Regex cache lives across requests via a static cache in the implementation.
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingCanonEngine,
    OetLearner.Api.Services.Writing.WritingCanonEngine>();
// Pure pathway generator — no DB, no DI dependencies; safe as singleton.
builder.Services.AddSingleton<OetLearner.Api.Services.Writing.IWritingPathwayGenerator,
    OetLearner.Api.Services.Writing.WritingPathwayGenerator>();
// Scoped services (one per HTTP request) — every method takes UserId first.
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingOnboardingService,
    OetLearner.Api.Services.Writing.WritingOnboardingService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingPathwayServiceV2,
    OetLearner.Api.Services.Writing.WritingPathwayServiceV2>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingDailyPlanServiceV2,
    OetLearner.Api.Services.Writing.WritingDailyPlanServiceV2>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingPracticeSelectionService,
    OetLearner.Api.Services.Writing.WritingPracticeSelectionService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingScenarioService,
    OetLearner.Api.Services.Writing.WritingScenarioService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingCanonService,
    OetLearner.Api.Services.Writing.WritingCanonService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingDrillServiceV2,
    OetLearner.Api.Services.Writing.WritingDrillServiceV2>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingCaseNoteDrillService,
    OetLearner.Api.Services.Writing.WritingCaseNoteDrillService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingLessonServiceV2,
    OetLearner.Api.Services.Writing.WritingLessonServiceV2>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingMockService,
    OetLearner.Api.Services.Writing.WritingMockService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingReadinessService,
    OetLearner.Api.Services.Writing.WritingReadinessService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingCoachServiceV2,
    OetLearner.Api.Services.Writing.WritingCoachServiceV2>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingOcrService,
    OetLearner.Api.Services.Writing.WritingOcrService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingModerationService,
    OetLearner.Api.Services.Writing.WritingModerationService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingAnnotationService,
    OetLearner.Api.Services.Writing.WritingAnnotationService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingHeuristicPreAssessmentService,
    OetLearner.Api.Services.Writing.WritingHeuristicPreAssessmentService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingAdminAnalyticsService,
    OetLearner.Api.Services.Writing.WritingAdminAnalyticsService>();
// OET Writing exam-faithful closure: unified task authoring + JSON import/export,
// the ContentPaper→Scenario publish bridge, and learner attempt-event ingestion.
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingTaskAuthoringService,
    OetLearner.Api.Services.Writing.WritingTaskAuthoringService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingTaskProjectionService,
    OetLearner.Api.Services.Writing.WritingTaskProjectionService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingAttemptEventService,
    OetLearner.Api.Services.Writing.WritingAttemptEventService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingTutorReviewService,
    OetLearner.Api.Services.Writing.WritingTutorReviewService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.WritingMarkingVoiceNoteService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingAppealService,
    OetLearner.Api.Services.Writing.WritingAppealService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingAnalyticsServiceV2,
    OetLearner.Api.Services.Writing.WritingAnalyticsServiceV2>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingMistakeService,
    OetLearner.Api.Services.Writing.WritingMistakeService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingContentAuditService,
    OetLearner.Api.Services.Writing.WritingContentAuditService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingShowcaseService,
    OetLearner.Api.Services.Writing.WritingShowcaseService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingRewriteService,
    OetLearner.Api.Services.Writing.WritingRewriteService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingParaphraseService,
    OetLearner.Api.Services.Writing.WritingParaphraseService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingAskService,
    OetLearner.Api.Services.Writing.WritingAskService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingOutlineService,
    OetLearner.Api.Services.Writing.WritingOutlineService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingScenarioGeneratorService,
    OetLearner.Api.Services.Writing.WritingScenarioGeneratorService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingDraftServiceV2,
    OetLearner.Api.Services.Writing.WritingDraftServiceV2>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingSubmissionEvaluationPipeline,
    OetLearner.Api.Services.Writing.WritingSubmissionEvaluationPipeline>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingSubmissionService,
    OetLearner.Api.Services.Writing.WritingSubmissionService>();
// Result-visibility config + learner-facing gated feedback (spec §15.2/§15.3, WS-B4 Section D/E).
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingResultVisibilityService,
    OetLearner.Api.Services.Writing.WritingResultVisibilityService>();
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingResultFeedbackService,
    OetLearner.Api.Services.Writing.WritingResultFeedbackService>();
// Buddy System (spec §23.5) — opt-in matching + chat + weekly check-ins.
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingBuddyService,
    OetLearner.Api.Services.Writing.WritingBuddyService>();
// 50-letter calibration harness (spec §33) — pre-release AI agreement gate.
builder.Services.AddScoped<OetLearner.Api.Services.Writing.IWritingCalibrationService,
    OetLearner.Api.Services.Writing.WritingCalibrationService>();
// HttpClient used by OCR to call Google Cloud Vision REST endpoint.
builder.Services.AddHttpClient("writing-ocr-gcv");
// 8 hosted crons — gated by Writing:CronsEnabled (default true). Each runs
// on its own cadence inside WritingCronBase; the daily ones run hourly and
// short-circuit unless the current UTC hour matches.
builder.Services.AddHostedService<OetLearner.Api.Services.Writing.Crons.WritingDailyPlanCron>();
builder.Services.AddHostedService<OetLearner.Api.Services.Writing.Crons.WritingReadinessCron>();
builder.Services.AddHostedService<OetLearner.Api.Services.Writing.Crons.WritingBatchGradingCron>();
builder.Services.AddHostedService<OetLearner.Api.Services.Writing.Crons.WritingAnalyticsAggregationCron>();
builder.Services.AddHostedService<OetLearner.Api.Services.Writing.Crons.WritingTutorQueueAlertCron>();
builder.Services.AddHostedService<OetLearner.Api.Services.Writing.Crons.WritingDraftCleanupCron>();
builder.Services.AddHostedService<OetLearner.Api.Services.Writing.Crons.WritingContentAuditCron>();

// ── Private Speaking Sessions ──
builder.Services.Configure<ZoomOptions>(builder.Configuration.GetSection("Zoom"));
builder.Services.AddHttpClient("ZoomApi");
builder.Services.AddHttpClient("ZoomAuth");
builder.Services.AddHttpClient("GoogleCalendar");
builder.Services.AddSingleton<ZoomMeetingService>();
builder.Services.AddScoped<PrivateSpeakingCalendarService>();
builder.Services.AddScoped<PrivateSpeakingService>();
builder.Services.AddScoped<LiveClassService>();
builder.Services.AddScoped<OetLearner.Api.Services.LiveClasses.LiveClassRecordingService>();
builder.Services.AddScoped<OetLearner.Api.Services.LiveClasses.LiveClassRecordingProcessingService>();

// Wave A1 — Zoom tutor stack: tutor profile + availability + earnings,
// and learner-facing class feedback. See OET_ZOOM_INTEGRATION_PLAN.md §7-§9.
builder.Services.AddScoped<OetLearner.Api.Services.Classes.ITutorService,
    OetLearner.Api.Services.Classes.TutorService>();
builder.Services.AddScoped<OetLearner.Api.Services.Classes.IClassFeedbackService,
    OetLearner.Api.Services.Classes.ClassFeedbackService>();
builder.Services.AddScoped<OetLearner.Api.Services.Classes.IClassNotificationService, OetLearner.Api.Services.Classes.ClassNotificationService>();

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
            await db.Database.ExecuteSqlRawAsync(OetLearner.Api.Services.Settings.RuntimeSettingsSchemaSelfHeal.Sql);
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
    var runtimeSettings = app.Services.GetRequiredService<OetLearner.Api.Services.Settings.IRuntimeSettingsProvider>();
    var scannerSettings = runtimeSettings.GetAsync().GetAwaiter().GetResult().UploadScanner;
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

        var scannerOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<OetLearner.Api.Configuration.UploadScannerOptions>>().Value;
        var endpointReason = OetLearner.Api.Services.Content.UploadScannerEndpointGuard.GetUnsafeEndpointReason(
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
                + "(e.g. /var/opt/oet-learner/storage).";

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
// Enable native WebSocket upgrades for the Writing Coach panel fallback
// (/ws/writing/coach/{sessionId}). SignalR has its own internal websocket
// handling; this only affects raw WS endpoints mapped via app.Map.
app.UseWebSockets();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok", service = "OET Learner API", timestamp = DateTimeOffset.UtcNow, check = "live" }))
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

        var result = new { status = healthy ? "ok" : "failed", service = "OET Learner API", checks, timestamp = DateTimeOffset.UtcNow, check = "ready" };
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
            service = "OET Learner API",
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
        ? Results.Ok(new { status = "ok", service = "OET Learner API", database = "ok", timestamp = DateTimeOffset.UtcNow })
        : Results.Json(new { status = "failed", service = "OET Learner API", database = "unavailable", timestamp = DateTimeOffset.UtcNow }, statusCode: StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();

app.MapAuthEndpoints();
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
OetLearner.Api.Endpoints.AiAssistantEndpoints.MapAiAssistantEndpoints(app);
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

app.MapHub<NotificationHub>("/v1/notifications/hub").RequireAuthorization();
app.MapHub<ConversationHub>("/v1/conversations/hub").RequireAuthorization();
app.MapHub<OetLearner.Api.Hubs.AiAssistantHub>("/v1/ai-assistant/hub").RequireAuthorization();
app.MapHub<OetLearner.Api.Services.Mocks.MockLiveRoomHub>("/v1/mocks/live-room/hub").RequireAuthorization();
app.MapHub<OetLearner.Api.Hubs.SpeakingLiveRoomHub>("/v1/speaking/live-rooms/hub").RequireAuthorization();
// Writing Module V2 hubs — submission grade events, coach hint streaming,
// today/plan + pathway recalculate broadcasts. See Hubs/Writing*Hub.cs.
app.MapHub<OetLearner.Api.Hubs.WritingSubmissionHub>("/hubs/writing-submissions").RequireAuthorization("LearnerOnly");
app.MapHub<OetLearner.Api.Hubs.WritingCoachHub>("/hubs/writing-coach").RequireAuthorization("LearnerOnly");
app.MapHub<OetLearner.Api.Hubs.WritingTodayHub>("/hubs/writing-today").RequireAuthorization("LearnerOnly");

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
    var storage = scope.ServiceProvider
        .GetRequiredService<OetLearner.Api.Services.Content.IFileStorage>();
    await DatabaseBootstrapper.InitializeAsync(db, app.Environment, bootstrapOptions, storageOptions, storage);

    // Sync AI provider key from env (AI__ApiKey, AI__BaseUrl, AI__DefaultModel)
    // into the AiProviders row so the registry-backed provider resolves it at
    // runtime. Encrypts with Data Protection. Idempotent.
    var dp = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
    var aiOpts = scope.ServiceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<OetLearner.Api.Configuration.AiProviderOptions>>()
        .Value;
    await DatabaseBootstrapper.SynchroniseAiProviderFromEnvAsync(db, dp, aiOpts);

    // Seed the RecallSetTags registry — DISABLED: admin manages recalls catalog manually.
    // var seedLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
    //     .CreateLogger("RecallSetTagRegistrySeeder");
    // try
    // {
    //     await OetLearner.Api.Services.Recalls.RecallSetTagRegistrySeeder.EnsureAsync(db, seedLogger);
    // }
    // catch (Exception ex)
    // {
    //     seedLogger.LogWarning(ex, "RecallSetTagRegistrySeeder failed at boot; continuing.");
    // }

    // Writing Module V2 content seed (OET_WRITING_MODULE_PATHWAY.md §13/14/15/16/17).
    // Loads 25 canon rules, 12 diagnostic scenarios, 6 exemplars, 16 lessons,
    // 30 sentence drills, 12 case-note drills, 6 mocks, 20 common mistakes from
    // Data/Seeds/WritingV2/*.json. Idempotent — re-runs are no-ops if rows present.
    // Gated by Writing:V2Seeder:Enabled (default true). Non-fatal on failure.
    var writingV2Logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("WritingV2ContentSeeder");
    try
    {
        var writingV2Config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        await OetLearner.Api.Services.Writing.WritingV2ContentSeeder.EnsureAsync(
            db, app.Environment, writingV2Config, writingV2Logger);
    }
    catch (Exception ex)
    {
        writingV2Logger.LogWarning(ex, "WritingV2ContentSeeder failed at boot; continuing.");
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
            .GetRequiredService<OetLearner.Api.Data.LearnerDbContext>();
        await OetLearner.Api.Services.Seeding.SpeakingDrillSeed.SeedAsync(speakingDrillDb, CancellationToken.None);
    }
    catch (Exception ex)
    {
        speakingDrillLogger.LogWarning(ex, "Speaking drill seeder failed (non-fatal)");
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
        .GetRequiredService<OetLearner.Api.Services.Listening.IListeningSampleSeeder>();
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
        .GetRequiredService<OetLearner.Api.Services.Listening.IListeningStarterContentSeeder>();
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

// Listening diagnostic seeder — Stage 2 of OET_LISTENING_MODULE_PATHWAY.md §6.1.
// Idempotently seeds the 23-item Phase 1 diagnostic mini-test (ContentPaper +
// 4 parts + 9 extracts + 19 questions + 4 accent-test questions). Disabled by
// default; opt in via `Seed:ListeningDiagnostic:Enabled=true`. Non-fatal.
using (var seedScope = app.Services.CreateScope())
{
    var diagnosticSeeder = seedScope.ServiceProvider
        .GetRequiredService<OetLearner.Api.Services.Listening.ListeningDiagnosticSeeder>();
    try
    {
        await diagnosticSeeder.SeedAsync(CancellationToken.None);
    }
    catch (Exception ex)
    {
        seedScope.ServiceProvider.GetRequiredService<ILogger<Program>>()
            .LogWarning(ex, "Listening diagnostic seeder failed (non-fatal)");
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
        .GetRequiredService<OetLearner.Api.Services.Seeding.MockSampleSeeder>();
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
        .GetRequiredService<OetLearner.Api.Services.Planner.StudyPlanTemplateSeeder>();
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
    return OetLearner.Api.Security.AdminPermissionEvaluator.HasAny(perms, anyOf);
}

public partial class Program;
