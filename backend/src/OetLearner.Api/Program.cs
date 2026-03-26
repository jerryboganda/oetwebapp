using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Endpoints;
using OetLearner.Api.Security;
using OetLearner.Api.Services;

var builder = WebApplication.CreateBuilder(args);
var authOptions = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
var bootstrapOptions = builder.Configuration.GetSection("Bootstrap").Get<BootstrapOptions>() ?? new BootstrapOptions();
var storageOptions = builder.Configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();
var platformOptions = builder.Configuration.GetSection("Platform").Get<PlatformOptions>() ?? new PlatformOptions();
var billingOptions = builder.Configuration.GetSection("Billing").Get<BillingOptions>() ?? new BillingOptions();
var useDevelopmentAuth = builder.Environment.IsDevelopment() && authOptions.UseDevelopmentAuth;
var enableSwagger = builder.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Features:EnableSwagger");
var trustForwardHeaders = builder.Configuration.GetValue<bool?>("Proxy:TrustForwardHeaders") ?? !builder.Environment.IsDevelopment();
var enforceHttps = builder.Configuration.GetValue<bool?>("Proxy:EnforceHttps") ?? !builder.Environment.IsDevelopment();
var corsOrigins = (builder.Configuration["Cors:AllowedOriginsCsv"]
                   ?? (builder.Environment.IsDevelopment()
                       ? "http://localhost:3000,https://localhost:3000,http://127.0.0.1:3000,https://127.0.0.1:3000"
                       : string.Empty))
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

if (!builder.Environment.IsDevelopment() && authOptions.UseDevelopmentAuth)
{
    throw new InvalidOperationException("Development auth cannot be enabled outside the Development environment.");
}

if (!useDevelopmentAuth && string.IsNullOrWhiteSpace(authOptions.Authority))
{
    if (string.IsNullOrWhiteSpace(authOptions.Issuer)
        || string.IsNullOrWhiteSpace(authOptions.Audience)
        || string.IsNullOrWhiteSpace(authOptions.SigningKey)
        || authOptions.SigningKey.Length < 32)
    {
        throw new InvalidOperationException("Configure Auth:Authority or Auth:Issuer/Auth:Audience/Auth:SigningKey (32+ chars) for production JWT validation.");
    }
}

if (!builder.Environment.IsDevelopment())
{
    if (!Uri.TryCreate(platformOptions.PublicApiBaseUrl, UriKind.Absolute, out _))
    {
        throw new InvalidOperationException("Platform:PublicApiBaseUrl must be configured as an absolute URL outside the Development environment.");
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
    var resolvedConnectionString = configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(resolvedConnectionString))
    {
        if (environment.IsDevelopment())
        {
            resolvedConnectionString = "Host=localhost;Port=5432;Database=oet_learner_dev;Username=postgres;Password=postgres";
        }
        else
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured outside the Development environment.");
        }
    }

    if (resolvedConnectionString.StartsWith("InMemory:", StringComparison.OrdinalIgnoreCase))
    {
        options.UseInMemoryDatabase(resolvedConnectionString["InMemory:".Length..]);
    }
    else
    {
        options.UseNpgsql(resolvedConnectionString);
    }
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddProblemDetails();
if (enableSwagger)
{
    builder.Services.AddSwaggerGen();
    builder.Services.AddOpenApi();
}

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.Configure<BootstrapOptions>(builder.Configuration.GetSection("Bootstrap"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.Configure<PlatformOptions>(builder.Configuration.GetSection("Platform"));
builder.Services.Configure<BillingOptions>(builder.Configuration.GetSection("Billing"));

if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("Frontend", policy =>
        {
            policy
                .WithOrigins(corsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
    });
}

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = useDevelopmentAuth ? DevelopmentAuthHandler.SchemeName : JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = useDevelopmentAuth ? DevelopmentAuthHandler.SchemeName : JwtBearerDefaults.AuthenticationScheme;
});

if (useDevelopmentAuth)
{
    authBuilder.AddScheme<AuthenticationSchemeOptions, DevelopmentAuthHandler>(DevelopmentAuthHandler.SchemeName, _ => { });
}
else
{
    authBuilder.AddJwtBearer(options =>
    {
        if (!string.IsNullOrWhiteSpace(authOptions.Authority))
        {
            options.Authority = authOptions.Authority;
            options.RequireHttpsMetadata = authOptions.RequireHttpsMetadata;
        }

        if (!string.IsNullOrWhiteSpace(authOptions.Audience))
        {
            options.Audience = authOptions.Audience;
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(authOptions.Authority) || !string.IsNullOrWhiteSpace(authOptions.Issuer),
            ValidateAudience = !string.IsNullOrWhiteSpace(authOptions.Audience),
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = authOptions.Issuer,
            ValidAudience = authOptions.Audience,
            NameClaimType = "user_id",
            RoleClaimType = "role"
        };

        if (string.IsNullOrWhiteSpace(authOptions.Authority))
        {
            options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.SigningKey!));
        }
    });
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("LearnerOnly", policy => policy.RequireAuthenticatedUser().RequireRole("learner"));
    options.AddPolicy("ExpertOnly", policy => policy.RequireAuthenticatedUser().RequireRole("expert"));
    options.AddPolicy("AdminOnly", policy => policy.RequireAuthenticatedUser().RequireRole("admin"));
});

builder.Services.AddScoped<LearnerService>();
builder.Services.AddScoped<ExpertService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddSingleton<PlatformLinkService>();
builder.Services.AddSingleton<MediaStorageService>();
builder.Services.AddHostedService<BackgroundJobProcessor>();

var app = builder.Build();

if (trustForwardHeaders)
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler(handler =>
{
    handler.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var exception = feature?.Error;
        context.Response.ContentType = "application/problem+json";

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
                supportHint = apiException.SupportHint
            };
            await context.Response.WriteAsync(JsonSupport.Serialize(apiPayload));
            return;
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var payload = new
        {
            code = "internal_server_error",
            message = exception?.Message ?? "An unexpected server error occurred.",
            retryable = false,
            supportHint = "Check backend logs for the stack trace."
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
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok", service = "OET Learner API", timestamp = DateTimeOffset.UtcNow, check = "live" }))
    .AllowAnonymous();
app.MapGet("/health/ready", async (LearnerDbContext db, CancellationToken ct) =>
{
    try
    {
        var database = db.Database.IsInMemory() || await db.Database.CanConnectAsync(ct);
        if (!database)
        {
            return Results.Json(new { status = "failed", service = "OET Learner API", database = "unavailable", timestamp = DateTimeOffset.UtcNow }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(new { status = "ok", service = "OET Learner API", database = "ok", timestamp = DateTimeOffset.UtcNow, check = "ready" });
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

app.MapLearnerEndpoints();
app.MapExpertEndpoints();
app.MapAdminEndpoints();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
    await DatabaseBootstrapper.InitializeAsync(db, app.Environment, bootstrapOptions);
}

app.Run();

public partial class Program;
