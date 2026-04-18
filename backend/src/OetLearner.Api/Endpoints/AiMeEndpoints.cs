using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiManagement;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Learner-facing AI endpoints: BYOK custody, preferences, usage snapshot.
///
/// Authorisation: requires authenticated user. All endpoints are
/// per-user; UserId is taken from the JWT and never accepted from input.
/// </summary>
public static class AiMeEndpoints
{
    public static IEndpointRouteBuilder MapAiMeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/me/ai")
            .RequireAuthorization()
            .RequireRateLimiting("PerUser");

        group.MapGet("/credentials", async (IAiCredentialVault vault, HttpContext http, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();
            var rows = await vault.ListAsync(userId, ct);
            return Results.Ok(rows);
        });

        group.MapPost("/credentials", async (
            AiCredentialCreateDto dto,
            IAiCredentialVault vault,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();
            var authAccountId = http.User.FindFirstValue("aid");
            var result = await vault.UpsertAsync(
                userId, authAccountId, dto.ProviderCode, dto.ApiKey,
                dto.ModelAllowlistCsv, skipValidation: false, ct);
            if (!result.Success)
            {
                return Results.BadRequest(new
                {
                    errorCode = result.ErrorCode,
                    error = result.ErrorMessage,
                });
            }
            return Results.Ok(new
            {
                id = result.CredentialId,
                keyHint = result.KeyHint,
                providerCode = dto.ProviderCode,
            });
        }).RequireRateLimiting("AiCredentialValidate");

        group.MapDelete("/credentials/{id}", async (
            string id,
            IAiCredentialVault vault,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();
            var ok = await vault.RevokeAsync(userId, id, ct);
            return ok ? Results.NoContent() : Results.NotFound();
        }).RequireRateLimiting("PerUserWrite");

        group.MapGet("/usage", async (IAiQuotaService quota, HttpContext http, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();
            var snapshot = await quota.GetUserPolicyAsync(userId, ct);
            return Results.Ok(snapshot);
        });

        group.MapGet("/preferences", async (LearnerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();
            var row = await db.UserAiPreferences.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId, ct);
            return Results.Ok(row ?? new UserAiPreferences { UserId = userId });
        });

        group.MapPut("/preferences", async (
            AiPreferencesUpsertDto dto,
            LearnerDbContext db,
            HttpContext http,
            CancellationToken ct) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();
            var row = await db.UserAiPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
            if (row is null)
            {
                row = new UserAiPreferences { UserId = userId };
                db.UserAiPreferences.Add(row);
            }
            row.Mode = dto.Mode;
            row.AllowPlatformFallback = dto.AllowPlatformFallback;
            row.PerFeatureOverridesJson = dto.PerFeatureOverridesJson ?? "{}";
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(row);
        }).RequireRateLimiting("PerUserWrite");

        group.MapGet("/credits", async (IAiCreditService credits, HttpContext http, CancellationToken ct, int? page, int? pageSize) =>
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();
            var balance = await credits.GetBalanceAsync(userId, ct);
            var entries = await credits.ListAsync(userId, page ?? 1, pageSize ?? 25, ct);
            return Results.Ok(new { balance, entries });
        });

        return app;
    }
}

public sealed record AiCredentialCreateDto(
    string ProviderCode,
    string ApiKey,
    string? ModelAllowlistCsv);

public sealed record AiPreferencesUpsertDto(
    AiCredentialMode Mode,
    bool AllowPlatformFallback,
    string? PerFeatureOverridesJson);
