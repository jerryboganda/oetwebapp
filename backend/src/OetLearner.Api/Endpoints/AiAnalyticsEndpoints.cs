using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Endpoints for the 4 AI / FX / experiment features:
///  - GET /v1/ai-usage/me   (learner usage summary)
///  - GET /v1/ai-usage/me/forecast
///  - GET /v1/ai-usage/me/churn-risk
///  - GET /v1/admin/ai-analytics/summary
///  - GET /v1/admin/ai-analytics/churn (top risk users)
///  - GET /v1/admin/ai-analytics/forecast (aggregate)
///  - GET/POST /v1/admin/fx/rates (read + refresh)
///  - GET/POST/DELETE /v1/admin/pricing-experiments
/// </summary>
public static class AiAnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAiAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1");

        var learner = v1.MapGroup("/ai-usage").RequireAuthorization();
        learner.MapGet("/me", LearnerSummary);
        learner.MapGet("/me/forecast", LearnerForecast);
        learner.MapGet("/me/churn-risk", LearnerChurnRisk);

        var admin = v1.MapGroup("/admin/ai-analytics");
        admin.MapGet("/summary", AdminSummary).RequireAuthorization("AdminBillingRead");
        admin.MapGet("/churn", AdminChurnList).RequireAuthorization("AdminBillingRead");
        admin.MapPost("/churn/recompute", AdminRecomputeChurn).WithAdminWrite("AdminBillingRead");
        admin.MapGet("/forecast", AdminForecastList).RequireAuthorization("AdminBillingRead");
        admin.MapPost("/forecast/recompute", AdminRecomputeForecast).WithAdminWrite("AdminBillingRead");

        var fx = v1.MapGroup("/admin/fx");
        fx.MapGet("/rates", ListRates).RequireAuthorization("AdminBillingRead");
        fx.MapPost("/refresh", RefreshRates).WithAdminWrite("AdminBillingCatalogWrite");

        var experiments = v1.MapGroup("/admin/pricing-experiments");
        experiments.MapGet("/", ListExperiments).RequireAuthorization("AdminBillingRead");
        experiments.MapPost("/", UpsertExperiment).WithAdminWrite("AdminBillingCatalogWrite");
        experiments.MapPost("/{id}/start", StartExperiment).WithAdminWrite("AdminBillingCatalogWrite");
        experiments.MapPost("/{id}/stop", StopExperiment).WithAdminWrite("AdminBillingCatalogWrite");
        experiments.MapGet("/{id}/results", ExperimentResults).RequireAuthorization("AdminBillingRead");
        experiments.MapGet("/{id}/significance", ExperimentSignificance).RequireAuthorization("AdminBillingRead");
        experiments.MapDelete("/{id}", DeleteExperiment).WithAdminWrite("AdminBillingCatalogWrite");

        var payouts = v1.MapGroup("/admin/billing/affiliate-payouts");
        payouts.MapPost("/generate", GeneratePayouts).WithAdminWrite("AdminBillingCatalogWrite");
        payouts.MapGet("/{batchId}.csv", ExportPayoutCsv).RequireAuthorization("AdminBillingRead");

        return app;
    }

    private static async Task<Ok<AffiliatePayoutBatch>> GeneratePayouts(IAffiliateService svc, [FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, CancellationToken ct)
    {
        var batch = await svc.GeneratePayoutBatchAsync(from, to, ct);
        return TypedResults.Ok(batch);
    }

    private static async Task<IResult> ExportPayoutCsv(string batchId, IAffiliateService svc, CancellationToken ct)
    {
        var csv = await svc.ExportPayoutCsvAsync(batchId, ct);
        return Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"{batchId}.csv");
    }

    private static async Task<Ok<List<ExperimentStatistics.ZTestResult>>> ExperimentSignificance(string id, LearnerDbContext db, CancellationToken ct)
    {
        var perVariant = await db.PricingExperimentAssignments
            .Where(a => a.ExperimentId == id)
            .GroupBy(a => a.VariantCode)
            .Select(g => new
            {
                Code = g.Key,
                Assignments = g.Count(),
                Conversions = g.Count(a => a.Converted),
            })
            .ToListAsync(ct);

        var stats = perVariant.Select(p => new ExperimentStatistics.VariantStats(p.Code, p.Assignments, p.Conversions)).ToList();
        var control = stats.FirstOrDefault(s => s.VariantCode == "control") ?? stats.FirstOrDefault();
        if (control is null) return TypedResults.Ok(new List<ExperimentStatistics.ZTestResult>());

        var results = stats
            .Where(s => s.VariantCode != control.VariantCode)
            .Select(s => ExperimentStatistics.Compare(control, s))
            .ToList();
        return TypedResults.Ok(results);
    }

    // ── Learner ───────────────────────────────────────────────────────

    private static async Task<Ok<LearnerUsageSummary>> LearnerSummary(HttpContext http, IAiUsageAnalyticsService svc, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var f = from ?? today.AddDays(-30);
        var t = to ?? today;
        return TypedResults.Ok(await svc.GetLearnerSummaryAsync(http.UserId(), f, t, ct));
    }

    private static async Task<Ok<UsageForecastSnapshot>> LearnerForecast(HttpContext http, IUsageForecastService svc, [FromQuery] int? windowDays, CancellationToken ct)
    {
        return TypedResults.Ok(await svc.ForecastUserAsync(http.UserId(), windowDays ?? 30, ct));
    }

    private static async Task<Ok<ChurnRiskSnapshot>> LearnerChurnRisk(HttpContext http, IChurnPredictionService svc, CancellationToken ct)
    {
        // Score on demand so the learner sees the freshest value; daily rollup
        // worker keeps the historical trend.
        return TypedResults.Ok(await svc.ScoreUserAsync(http.UserId(), ct));
    }

    // ── Admin ─────────────────────────────────────────────────────────

    private static async Task<Ok<AdminUsageSummary>> AdminSummary(IAiUsageAnalyticsService svc, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? feature, [FromQuery] string? provider, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var f = from ?? today.AddDays(-30);
        var t = to ?? today;
        return TypedResults.Ok(await svc.GetAdminSummaryAsync(f, t, feature, provider, ct));
    }

    private static async Task<Ok<List<ChurnRiskSnapshot>>> AdminChurnList(LearnerDbContext db, [FromQuery] string? band, [FromQuery] int? limit, CancellationToken ct)
    {
        var latestDate = await db.ChurnRiskSnapshots.MaxAsync(s => (DateOnly?)s.SnapshotDate, ct);
        if (latestDate is null) return TypedResults.Ok(new List<ChurnRiskSnapshot>());

        var q = db.ChurnRiskSnapshots.Where(s => s.SnapshotDate == latestDate);
        if (!string.IsNullOrEmpty(band)) q = q.Where(s => s.RiskBand == band);

        return TypedResults.Ok(await q
            .OrderByDescending(s => s.RiskScore)
            .Take(limit ?? 100)
            .ToListAsync(ct));
    }

    private static async Task<Ok<string>> AdminRecomputeChurn(IChurnPredictionService svc, CancellationToken ct)
    {
        var n = await svc.RollupAllUsersAsync(ct);
        return TypedResults.Ok($"Scored {n} users.");
    }

    private static async Task<Ok<List<UsageForecastSnapshot>>> AdminForecastList(LearnerDbContext db, [FromQuery] int? limit, CancellationToken ct)
    {
        var latestDate = await db.UsageForecastSnapshots.MaxAsync(s => (DateOnly?)s.SnapshotDate, ct);
        if (latestDate is null) return TypedResults.Ok(new List<UsageForecastSnapshot>());
        return TypedResults.Ok(await db.UsageForecastSnapshots
            .Where(s => s.SnapshotDate == latestDate && s.FeatureCode == "*")
            .OrderByDescending(s => s.ForecastCostUsd)
            .Take(limit ?? 100)
            .ToListAsync(ct));
    }

    private static async Task<Ok<string>> AdminRecomputeForecast(IUsageForecastService svc, CancellationToken ct)
    {
        var n = await svc.RollupAllUsersAsync(ct);
        return TypedResults.Ok($"Forecasted {n} users.");
    }

    // ── FX rates ──────────────────────────────────────────────────────

    private static async Task<Ok<List<ExchangeRate>>> ListRates(LearnerDbContext db, [FromQuery] string? from, [FromQuery] string? to, CancellationToken ct)
    {
        var q = db.ExchangeRates.AsQueryable();
        if (!string.IsNullOrEmpty(from)) q = q.Where(r => r.FromCurrency == from.ToUpperInvariant());
        if (!string.IsNullOrEmpty(to)) q = q.Where(r => r.ToCurrency == to.ToUpperInvariant());
        return TypedResults.Ok(await q.OrderByDescending(r => r.EffectiveFrom).Take(500).ToListAsync(ct));
    }

    private static async Task<Ok<string>> RefreshRates(IFxRateService svc, CancellationToken ct)
    {
        var n = await svc.RefreshRatesAsync(ct);
        return TypedResults.Ok($"Refreshed {n} rate rows.");
    }

    // ── Pricing experiments ──────────────────────────────────────────

    private static async Task<Ok<List<PricingExperiment>>> ListExperiments(LearnerDbContext db, [FromQuery] string? status, CancellationToken ct)
    {
        var q = db.PricingExperiments.AsQueryable();
        if (!string.IsNullOrEmpty(status)) q = q.Where(e => e.Status == status);
        return TypedResults.Ok(await q.OrderByDescending(e => e.CreatedAt).ToListAsync(ct));
    }

    private static async Task<Results<Ok<PricingExperiment>, BadRequest<string>>> UpsertExperiment(HttpContext http, PricingExperimentUpsertRequest request, LearnerDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.TargetType) || string.IsNullOrWhiteSpace(request.TargetId))
        {
            return TypedResults.BadRequest("code, name, targetType, targetId are required.");
        }
        if (request.RolloutPercent is < 0 or > 100) return TypedResults.BadRequest("rolloutPercent must be 0-100.");

        var now = DateTimeOffset.UtcNow;
        var existing = await db.PricingExperiments.FirstOrDefaultAsync(e => e.Code == request.Code, ct);
        if (existing is null)
        {
            existing = new PricingExperiment
            {
                Id = Guid.NewGuid().ToString("N"),
                Code = request.Code,
                Name = request.Name,
                TargetType = request.TargetType,
                TargetId = request.TargetId,
                Region = request.Region ?? "*",
                Status = "draft",
                RolloutPercent = request.RolloutPercent,
                VariantsJson = request.VariantsJson ?? "[]",
                CreatedAt = now,
                UpdatedAt = now,
                CreatedByAdminId = http.UserId(),
            };
            db.PricingExperiments.Add(existing);
        }
        else
        {
            existing.Name = request.Name;
            existing.TargetType = request.TargetType;
            existing.TargetId = request.TargetId;
            existing.Region = request.Region ?? "*";
            existing.RolloutPercent = request.RolloutPercent;
            existing.VariantsJson = request.VariantsJson ?? existing.VariantsJson;
            existing.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(existing);
    }

    private static async Task<Results<Ok<PricingExperiment>, NotFound>> StartExperiment(string id, LearnerDbContext db, CancellationToken ct)
    {
        var row = await db.PricingExperiments.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (row is null) return TypedResults.NotFound();
        row.Status = "running";
        row.StartedAt ??= DateTimeOffset.UtcNow;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(row);
    }

    private static async Task<Results<Ok<PricingExperiment>, NotFound>> StopExperiment(string id, LearnerDbContext db, CancellationToken ct)
    {
        var row = await db.PricingExperiments.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (row is null) return TypedResults.NotFound();
        row.Status = "completed";
        row.EndedAt = DateTimeOffset.UtcNow;
        row.UpdatedAt = row.EndedAt.Value;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(row);
    }

    private static async Task<Ok<ExperimentResultsResponse>> ExperimentResults(string id, LearnerDbContext db, CancellationToken ct)
    {
        var perVariant = await db.PricingExperimentAssignments
            .Where(a => a.ExperimentId == id)
            .GroupBy(a => a.VariantCode)
            .Select(g => new VariantResults(
                g.Key,
                g.Count(),
                g.Count(a => a.Converted),
                g.Sum(a => (decimal?)a.ConvertedAmount) ?? 0m))
            .ToListAsync(ct);

        return TypedResults.Ok(new ExperimentResultsResponse(id, perVariant));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteExperiment(string id, LearnerDbContext db, CancellationToken ct)
    {
        var row = await db.PricingExperiments.FindAsync(new object?[] { id }, ct);
        if (row is null) return TypedResults.NotFound();
        db.PricingExperiments.Remove(row);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}

public sealed record PricingExperimentUpsertRequest(
    string Code,
    string Name,
    string TargetType,
    string TargetId,
    string? Region,
    int RolloutPercent,
    string? VariantsJson);

public sealed record VariantResults(string VariantCode, int Assignments, int Conversions, decimal ConversionRevenue);
public sealed record ExperimentResultsResponse(string ExperimentId, List<VariantResults> Variants);

file static class AiAnalyticsHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
