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

var builder = WebApplication.CreateBuilder(args);
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
    if (brevoOptions.Enabled)
    {
        if (string.IsNullOrWhiteSpace(brevoOptions.ApiKey)
            || string.IsNullOrWhiteSpace(brevoOptions.FromEmail)
            || brevoOptions.EmailVerificationTemplateId is null
            || brevoOptions.PasswordResetTemplateId is null)
        {
            throw new InvalidOperationException(
                "Configure Brevo:Enabled=true, Brevo:ApiKey, Brevo:FromEmail, Brevo:EmailVerificationTemplateId, and Brevo:PasswordResetTemplateId outside the Development environment.");
        }
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
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSignalR();
builder.Services.AddSingleton<IWebPushDispatcher, WebPushDispatcher>();
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
        client.DefaultRequestHeaders.Add("api-key", configuredBrevoOptions.ApiKey);
        client.DefaultRequestHeaders.Add("accept", "application/json");
    });
    builder.Services.AddTransient<IEmailSender, BrevoEmailSender>();
}
else
{
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
}
builder.Services.AddScoped<EmailOtpService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ExternalAuthService>();

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
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authTokenOptions.AccessTokenSigningKey!))
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(accessToken)
                && (context.HttpContext.Request.Path.StartsWithSegments("/v1/notifications/hub")
                    || context.HttpContext.Request.Path.StartsWithSegments("/v1/conversations/hub")))
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
    options.AddPolicy("LearnerOnly", policy => policy.RequireAuthenticatedUser().RequireRole("learner"));
    options.AddPolicy("ExpertOnly", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("expert")
        .RequireClaim(AuthTokenService.IsEmailVerifiedClaimType, bool.TrueString.ToLowerInvariant()));
    options.AddPolicy("AdminOnly", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("admin")
        .RequireClaim(AuthTokenService.IsEmailVerifiedClaimType, bool.TrueString.ToLowerInvariant()));
    options.AddPolicy("SponsorOnly", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("sponsor")
        .RequireClaim(AuthTokenService.IsEmailVerifiedClaimType, bool.TrueString.ToLowerInvariant()));

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
    options.AddPolicy("AdminBillingRead", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "billing:read", "system_admin")));
    options.AddPolicy("AdminBillingWrite", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "billing:write", "system_admin")));
    options.AddPolicy("AdminUsersRead", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "users:read", "system_admin")));
    options.AddPolicy("AdminUsersWrite", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "users:write", "system_admin")));
    options.AddPolicy("AdminReviewOps", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "review_ops", "system_admin")));
    options.AddPolicy("AdminAiConfig", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "ai_config", "system_admin")));
    options.AddPolicy("AdminSystemAdmin", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "system_admin")));

    // ── Study Planner v2 ──
    options.AddPolicy("AdminStudyPlannerRead", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "study_planner:read", "study_planner:write", "system_admin")));
    options.AddPolicy("AdminStudyPlannerWrite", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "study_planner:write", "system_admin")));

    // ── Progress v2 ──
    options.AddPolicy("AdminProgressPolicyRead", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "progress_policy:read", "progress_policy:write", "system_admin")));
    options.AddPolicy("AdminProgressPolicyWrite", policy => policy
        .RequireAuthenticatedUser().RequireRole("admin")
        .RequireAssertion(ctx => HasAdminPermission(ctx, "progress_policy:write", "system_admin")));
});

builder.Services.AddScoped<LearnerService>();
builder.Services.AddScoped<ExpertService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<SponsorService>();
builder.Services.AddScoped<ContentHierarchyService>();
builder.Services.AddScoped<ContentDeduplicationService>();
builder.Services.AddScoped<ContentAccessService>();
builder.Services.AddScoped<MockDiagnosticService>();
builder.Services.AddScoped<ContentImportService>();
builder.Services.AddScoped<ContentSearchService>();
builder.Services.AddScoped<MediaNormalizationService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<AnalyticsIngestionService>();
builder.Services.AddSingleton<PlatformLinkService>();
builder.Services.AddSingleton<MediaStorageService>();
builder.Services.AddHttpClient<StripeGateway>();
builder.Services.AddHttpClient<PayPalGateway>();
builder.Services.AddScoped<PaymentGatewayService>();
builder.Services.AddScoped<WalletService>();
builder.Services.AddScoped<EngagementService>();
builder.Services.AddHostedService<BackgroundJobProcessor>();

// ── Phase 1 new services ──
builder.Services.AddScoped<GamificationService>();
builder.Services.AddScoped<SpacedRepetitionService>();
builder.Services.AddScoped<VocabularyService>();
builder.Services.AddScoped<AdaptiveDifficultyService>();

// ── Phase 2 new services ──
builder.Services.AddScoped<PredictionService>();
builder.Services.AddScoped<ScoringService>();
builder.Services.AddScoped<ContentGenerationService>();
builder.Services.AddSingleton<SpeechToTextService>();
builder.Services.AddScoped<ConversationService>();
builder.Services.AddScoped<PronunciationService>();
builder.Services.AddScoped<WritingCoachService>();
builder.Services.AddScoped<MarketplaceService>();

// OET rulebook engine + grounded AI gateway. These services are the single
// source of truth for rule enforcement and for every AI call: no code path
// invokes a model without a rulebook-grounded prompt built here.
builder.Services.AddSingleton<OetLearner.Api.Services.Rulebook.IRulebookLoader,
    OetLearner.Api.Services.Rulebook.RulebookLoader>();
builder.Services.AddScoped<OetLearner.Api.Services.Rulebook.WritingRuleEngine>();
builder.Services.AddScoped<OetLearner.Api.Services.Rulebook.SpeakingRuleEngine>();
builder.Services.AddHttpClient("AiOpenAiCompatible", client =>
{
    client.Timeout = TimeSpan.FromMinutes(30);
    if (!string.IsNullOrWhiteSpace(aiProviderOptions.BaseUrl))
    {
        client.BaseAddress = new Uri(aiProviderOptions.BaseUrl.TrimEnd('/') + "/");
    }
});
builder.Services.AddSingleton<OetLearner.Api.Services.Rulebook.IAiModelProvider,
    OetLearner.Api.Services.Rulebook.MockAiProvider>();
// Legacy config-only OpenAiCompatibleProvider is retained in-code for tests
// but no longer registered in DI — the registry-backed provider supersedes
// it. Admins configure the real provider via /admin/ai-usage → Providers.
builder.Services.AddScoped<OetLearner.Api.Services.Rulebook.IAiUsageRecorder,
    OetLearner.Api.Services.Rulebook.AiUsageRecorder>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<OetLearner.Api.Services.AiManagement.IAiQuotaService,
    OetLearner.Api.Services.AiManagement.AiQuotaService>();
builder.Services.AddHttpClient("AiCredentialValidator");
builder.Services.AddScoped<OetLearner.Api.Services.AiManagement.IAiCredentialVault,
    OetLearner.Api.Services.AiManagement.AiCredentialVault>();
builder.Services.AddScoped<OetLearner.Api.Services.AiManagement.IAiCredentialResolver,
    OetLearner.Api.Services.AiManagement.AiCredentialResolver>();
builder.Services.AddHttpClient("AiRegistryClient", c => c.Timeout = TimeSpan.FromMinutes(30));
builder.Services.AddScoped<OetLearner.Api.Services.Rulebook.IAiProviderRegistry,
    OetLearner.Api.Services.Rulebook.AiProviderRegistry>();
builder.Services.AddScoped<OetLearner.Api.Services.Rulebook.IAiModelProvider,
    OetLearner.Api.Services.Rulebook.RegistryBackedProvider>();
builder.Services.AddScoped<OetLearner.Api.Services.Rulebook.IAiModelProvider,
    OetLearner.Api.Services.Rulebook.AnthropicProvider>();
builder.Services.AddScoped<OetLearner.Api.Services.AiManagement.IAiCreditService,
    OetLearner.Api.Services.AiManagement.AiCreditService>();
builder.Services.AddHostedService<OetLearner.Api.Services.AiManagement.AiCreditRenewalWorker>();

// Content Upload subsystem (Slice 2). IFileStorage sits in front of disk
// access so future S3/R2 swap is a DI-only change.
builder.Services.AddSingleton<OetLearner.Api.Services.Content.IFileStorage,
    OetLearner.Api.Services.Content.LocalFileStorage>();
builder.Services.AddSingleton<OetLearner.Api.Services.Content.IUploadContentValidator,
    OetLearner.Api.Services.Content.MagicByteValidator>();
builder.Services.AddSingleton<OetLearner.Api.Services.Content.IUploadScanner,
    OetLearner.Api.Services.Content.NoOpUploadScanner>();
builder.Services.AddScoped<OetLearner.Api.Services.Content.IChunkedUploadService,
    OetLearner.Api.Services.Content.ChunkedUploadService>();
builder.Services.AddScoped<OetLearner.Api.Services.Content.IContentPaperService,
    OetLearner.Api.Services.Content.ContentPaperService>();
builder.Services.AddSingleton<OetLearner.Api.Services.Content.IContentConventionParser,
    OetLearner.Api.Services.Content.ContentConventionParser>();
builder.Services.AddScoped<OetLearner.Api.Services.Content.IContentBulkImportService,
    OetLearner.Api.Services.Content.ContentBulkImportService>();
builder.Services.AddSingleton<OetLearner.Api.Services.Content.IPdfTextExtractor,
    OetLearner.Api.Services.Content.NoOpPdfTextExtractor>();
builder.Services.AddScoped<OetLearner.Api.Services.Content.IContentTextExtractionService,
    OetLearner.Api.Services.Content.ContentTextExtractionService>();
builder.Services.AddHostedService<OetLearner.Api.Services.Content.ContentTextExtractionWorker>();

// Reading Authoring subsystem (Slices R1–R7).
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IReadingStructureService,
    OetLearner.Api.Services.Reading.ReadingStructureService>();
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IReadingPolicyService,
    OetLearner.Api.Services.Reading.ReadingPolicyService>();
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IReadingGradingService,
    OetLearner.Api.Services.Reading.ReadingGradingService>();
builder.Services.AddScoped<OetLearner.Api.Services.Reading.IReadingAttemptService,
    OetLearner.Api.Services.Reading.ReadingAttemptService>();
builder.Services.AddHostedService<OetLearner.Api.Services.Reading.ReadingAttemptExpireWorker>();
builder.Services.AddHostedService<OetLearner.Api.Services.Content.AdminUploadCleanupWorker>();
builder.Services.AddScoped<OetLearner.Api.Services.Rulebook.IAiGatewayService,
    OetLearner.Api.Services.Rulebook.AiGatewayService>();

// ── Private Speaking Sessions ──
builder.Services.Configure<ZoomOptions>(builder.Configuration.GetSection("Zoom"));
builder.Services.AddHttpClient("ZoomApi");
builder.Services.AddHttpClient("ZoomAuth");
builder.Services.AddSingleton<ZoomMeetingService>();
builder.Services.AddScoped<PrivateSpeakingService>();

// ── Study Planner v2 ──
builder.Services.AddScoped<OetLearner.Api.Services.StudyPlanner.IStudyPlannerAdminService,
    OetLearner.Api.Services.StudyPlanner.StudyPlannerAdminService>();
builder.Services.AddScoped<OetLearner.Api.Services.StudyPlanner.IStudyPlannerRuleEngine,
    OetLearner.Api.Services.StudyPlanner.StudyPlannerRuleEngine>();
builder.Services.AddScoped<OetLearner.Api.Services.StudyPlanner.IStudyPlannerAiReasoner,
    OetLearner.Api.Services.StudyPlanner.StudyPlannerAiReasoner>();
builder.Services.AddScoped<OetLearner.Api.Services.StudyPlanner.IStudyPlannerService,
    OetLearner.Api.Services.StudyPlanner.StudyPlannerService>();
builder.Services.Configure<OetLearner.Api.Services.StudyPlanner.GoogleCalendarOptions>(
    builder.Configuration.GetSection("GoogleCalendar"));
builder.Services.AddHttpClient();
builder.Services.AddDataProtection();
builder.Services.AddScoped<OetLearner.Api.Services.StudyPlanner.IGoogleCalendarService,
    OetLearner.Api.Services.StudyPlanner.GoogleCalendarService>();

// ── Progress v2 ──
builder.Services.AddScoped<OetLearner.Api.Services.Progress.IProgressService,
    OetLearner.Api.Services.Progress.ProgressService>();

var app = builder.Build();

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
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(self), geolocation=()";
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

        app.Logger.LogError(
            exception,
            "Unhandled exception while processing {Method} {Path}. CorrelationId: {CorrelationId}",
            context.Request.Method,
            context.Request.Path,
            correlationId ?? "missing");

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

app.MapGet("/health/live", () => Results.Ok(new { status = "ok", service = "OET Learner API", timestamp = DateTimeOffset.UtcNow, check = "live" }))
    .AllowAnonymous();
app.MapGet("/health/ready", async (LearnerDbContext db, IOptions<StorageOptions> storageOptions, CancellationToken ct) =>
{
    try
    {
        var checks = new Dictionary<string, string>();
        var healthy = true;

        // Database connectivity
        var dbOk = db.Database.IsInMemory() || await db.Database.CanConnectAsync(ct);
        checks["database"] = dbOk ? "ok" : "unavailable";
        if (!dbOk) healthy = false;

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
        return Results.Json(new { status = "failed", service = "OET Learner API", database = "error", message = ex.Message, timestamp = DateTimeOffset.UtcNow }, statusCode: StatusCodes.Status503ServiceUnavailable);
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
app.MapExpertEndpoints();
app.MapAdminEndpoints();
app.MapAiUsageAdminEndpoints();
app.MapAiMeEndpoints();
app.MapContentPapersAdminEndpoints();
app.MapContentPapersLearnerEndpoints();
app.MapReadingAuthoringAdminEndpoints();
app.MapReadingLearnerEndpoints();
app.MapReadingPolicyAdminEndpoints();
app.MapContentHierarchyEndpoints();
app.MapStudyPlannerAdminEndpoints();
app.MapStudyPlannerLearnerEndpoints();
app.MapProgressLearnerEndpoints();
app.MapProgressAdminEndpoints();

// ── Phase 1 new endpoints ──
app.MapGamificationEndpoints();
app.MapReviewItemEndpoints();
app.MapVocabularyEndpoints();
app.MapAdaptiveEndpoints();

// ── Phase 2 new endpoints ──
app.MapPredictionEndpoints();

// ── Phase 3 new endpoints ──
app.MapLearningContentEndpoints();
app.MapCommunityEndpoints();
app.MapSocialEndpoints();

// ── Phase 2 new endpoints ──
app.MapConversationEndpoints();
app.MapPronunciationEndpoints();
app.MapWritingCoachEndpoints();
app.MapMarketplaceEndpoints();

// ── Sponsor Dashboard ──
app.MapSponsorEndpoints();

// ── Private Speaking Sessions ──
app.MapPrivateSpeakingEndpoints();

// ── Rulebook + Writing linter + Speaking auditor + Grounded AI gateway ──
app.MapRulebookEndpoints();

// ── Media Management ──
app.MapMediaEndpoints();

app.MapHub<NotificationHub>("/v1/notifications/hub").RequireAuthorization();
app.MapHub<ConversationHub>("/v1/conversations/hub").RequireAuthorization();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
    await DatabaseBootstrapper.InitializeAsync(db, app.Environment, bootstrapOptions, storageOptions);
}

app.Run();

static bool HasAdminPermission(AuthorizationHandlerContext ctx, params string[] anyOf)
{
    var perms = ctx.User.FindFirstValue(AuthTokenService.AdminPermissionsClaimType);
    if (string.IsNullOrEmpty(perms)) return false;
    var granted = perms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return anyOf.Any(p => granted.Contains(p, StringComparer.OrdinalIgnoreCase));
}

public partial class Program;
