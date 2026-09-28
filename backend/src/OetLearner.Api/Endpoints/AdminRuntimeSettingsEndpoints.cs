using System.Security.Claims;
using System.Text.Json;
using System.Net.Sockets;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Pronunciation;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Admin endpoints for managing runtime infrastructure settings (Email/Brevo,
/// Stripe, Sentry, Backup S3, OAuth providers, Push, Zoom). These map onto the
/// <see cref="RuntimeSettingsRow"/> singleton (Id = "default") and let
/// platform admins rotate secrets without editing <c>.env.production</c>.
///
/// Contract:
/// <list type="bullet">
///   <item>GET returns the merged effective view with secrets <b>masked</b>
///   as <c>"********"</c> when present, empty string when absent. Plaintext
///   secret material never leaves the host process.</item>
///   <item>PUT accepts a partial sectioned payload. Per-field semantics:
///   <c>null</c> = leave unchanged; <c>"********"</c> sentinel = leave secret
///   unchanged; empty string = clear the DB override; any other value =
///   set (and encrypt if it is a secret).</item>
///   <item>Both routes require the <c>AdminSystemAdmin</c> policy.</item>
///   <item>PUT writes one <see cref="AuditEvent"/> with action
///   <c>RuntimeSettingsUpdated</c>. The audit payload lists the <i>keys</i>
///   that changed only — it MUST NOT contain secret values.</item>
/// </list>
/// </summary>
public static partial class AdminRuntimeSettingsEndpoints
{
    private const string SecretMask = "********";

    private static readonly HashSet<string> ValidRiskModes = new(StringComparer.Ordinal)
    {
        SecurityRiskModes.Off,
        SecurityRiskModes.LogOnly,
        SecurityRiskModes.Enforce,
    };

    private static readonly HashSet<string> ValidCountryAllowListModes =
    [
        SecurityCountryAllowListModes.Off,
        SecurityCountryAllowListModes.StepUp,
        SecurityCountryAllowListModes.Block,
    ];

    /// <summary>
    /// Wire <c>GET</c> and <c>PUT /runtime-settings</c> into the supplied admin
    /// route group. The group is expected to already enforce <c>AdminOnly</c>
    /// + per-user rate limiting (matches the contract of the group built by
    /// <see cref="AdminEndpoints.MapAdminEndpoints"/>).
    /// </summary>
    public static IEndpointRouteBuilder MapAdminRuntimeSettings(this IEndpointRouteBuilder admin)
    {
        admin.MapGet("/runtime-settings", async (
                IRuntimeSettingsProvider provider,
                CancellationToken ct) =>
            {
                var settings = await provider.GetAsync(ct);
                return Results.Ok(BuildResponse(settings));
            })
            .WithAdminRead("AdminSystemAdmin");

        admin.MapPut("/runtime-settings", async Task<IResult> (
                HttpContext http,
                RuntimeSettingsUpdateRequest request,
                LearnerDbContext db,
                IRuntimeSettingsProvider provider,
                IWebHostEnvironment env,
                IOptions<UploadScannerOptions> scannerOptions,
                CancellationToken ct) =>
            {
                var row = await db.RuntimeSettings.FirstOrDefaultAsync(r => r.Id == "default", ct);
                var now = DateTimeOffset.UtcNow;
                if (row is null)
                {
                    row = new RuntimeSettingsRow { Id = "default", UpdatedAt = now };
                    db.RuntimeSettings.Add(row);
                }

                var changedKeys = new List<string>();
                try
                {
                    ApplyEmail(row, request.Email, provider, changedKeys);
                    ApplyBilling(row, request.Billing, provider, changedKeys);
                    ApplySentry(row, request.Sentry, changedKeys);
                    ApplyBackup(row, request.Backup, provider, changedKeys);
                    ApplyOAuth(row, request.OAuth, provider, changedKeys);
                    ApplyPush(row, request.Push, provider, changedKeys);
                    ApplyUploadScanner(row, request.UploadScanner, env, scannerOptions.Value, changedKeys);
                    ApplyZoom(row, request.Zoom, provider, env, changedKeys);
                    ApplyStripe(row, request.Stripe, provider, changedKeys);
                    ApplySpeakingWhisper(row, request.SpeakingWhisper, provider, changedKeys);
                    ApplySpeakingLiveKit(row, request.SpeakingLiveKit, provider, changedKeys);
                    ApplySpeakingAi(row, request.SpeakingAi, provider, changedKeys);
                    ApplySpeakingStorage(row, request.SpeakingStorage, provider, changedKeys);
                    ApplySpeakingCompliance(row, request.SpeakingCompliance, changedKeys);
                    ApplySpeakingFeatures(row, request.SpeakingFeatures, changedKeys);
                    ApplyPlacement(row, request.Placement, changedKeys);
                    ApplyCheckoutCom(row, request.CheckoutCom, provider, changedKeys);
                    ApplyBunnyStream(row, request.BunnyStream, provider, changedKeys);
                    ApplyVideoProtection(row, request.VideoProtection, changedKeys);
                    ApplySecurity(row, request.Security, provider, changedKeys);
                    ApplyPaymob(row, request.Paymob, provider, changedKeys);
                    ApplyPayTabs(row, request.PayTabs, provider, changedKeys);
                    ApplyEasyKash(row, request.EasyKash, provider, changedKeys);
                    ApplySoketi(row, request.Soketi, provider, changedKeys);
                    ApplyDataRetention(row, request.DataRetention, changedKeys);
                    ApplyExpertAutoAssignment(row, request.ExpertAutoAssignment, changedKeys);
                    ApplyPasswordPolicy(row, request.PasswordPolicy, changedKeys);
                    ApplyAiAssistant(row, request.AiAssistant, changedKeys);
                    ApplyAiGateway(row, request.AiGateway, changedKeys);
                    ApplyWriting(row, request.Writing, provider, changedKeys);
                    ApplyPlatform(row, request.Platform, changedKeys);
                    ApplyMessaging(row, request.Messaging, provider, changedKeys);
                    ApplyFx(row, request.Fx, provider, changedKeys);
                    ApplyBillingCore(row, request.BillingCore, changedKeys);
                    ApplyStorage(row, request.Storage, provider, changedKeys);
                    ApplyPdfExtraction(row, request.PdfExtraction, provider, changedKeys);
                    ApplyPronunciation(row, request.Pronunciation, changedKeys);
                    ApplyAuthTokens(row, request.AuthTokens, changedKeys);
                    ApplyWebPush(row, request.WebPush, changedKeys);
                    ApplySupport(row, request.Support, changedKeys);
                    ApplyFirebaseOtp(row, request.FirebaseOtp, provider, changedKeys);
                }
                catch (RuntimeSettingsValidationException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }

                row.UpdatedAt = now;
                row.UpdatedByUserId = http.AdminId();
                row.UpdatedByUserName = http.AdminName();

                db.AuditEvents.Add(new AuditEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ActorId = row.UpdatedByUserId ?? "system",
                    ActorName = row.UpdatedByUserName ?? "system",
                    Action = "RuntimeSettingsUpdated",
                    ResourceType = "RuntimeSettings",
                    ResourceId = row.Id,
                    // SECURITY: only the changed key names are persisted,
                    // never the values. Secret material must never appear in
                    // audit storage.
                    Details = JsonSupport.Serialize(new { changedKeys }),
                    OccurredAt = now,
                });

                await db.SaveChangesAsync(ct);
                provider.Invalidate();

                return Results.Ok(BuildResponse(await provider.GetAsync(ct)));
            })
            .WithAdminWrite("AdminSystemAdmin");

        admin.MapPost("/runtime-settings/test/{sectionId}", async Task<IResult> (
                string sectionId,
                HttpContext http,
                LearnerDbContext db,
                IRuntimeSettingsProvider provider,
                IWebHostEnvironment env,
                IOptions<UploadScannerOptions> scannerOptions,
                IPronunciationCredentialResolver whisperRegistry,
                IHttpClientFactory httpClientFactory,
                TimeProvider clock,
                CancellationToken ct) =>
            {
                var normalized = NormalizeSectionId(sectionId);
                if (normalized is null)
                    return Results.BadRequest(new { message = $"Unknown integration section '{sectionId}'." });

                var testedAt = clock.GetUtcNow();
                var result = await TestSectionAsync(normalized, provider, env, scannerOptions.Value, whisperRegistry, httpClientFactory, testedAt, ct);
                db.AuditEvents.Add(new AuditEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ActorId = http.AdminId(),
                    ActorName = http.AdminName(),
                    Action = "RuntimeSettingsTested",
                    ResourceType = "RuntimeSettings",
                    ResourceId = "default",
                    Details = JsonSupport.Serialize(new { section = normalized, status = result.Status }),
                    OccurredAt = testedAt,
                });
                await db.SaveChangesAsync(ct);
                return Results.Ok(result);
            })
            .WithAdminWrite("AdminSystemAdmin");

        return admin;
    }

    private static string AdminId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated admin id is required.");

    private static string AdminName(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.Name) ?? "Admin";

    private sealed class RuntimeSettingsValidationException : InvalidOperationException
    {
        public RuntimeSettingsValidationException(string message) : base(message) { }
        public RuntimeSettingsValidationException(string message, Exception innerException) : base(message, innerException) { }
    }
}
