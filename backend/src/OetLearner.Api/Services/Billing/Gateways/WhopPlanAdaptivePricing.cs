using System.Net.Http.Json;
using System.Text.Json;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Services.Billing.Gateways;

/// <summary>
/// Audits (and optionally fixes) Whop adaptive pricing — Whop's local-currency
/// re-pricing — on every plan of the company. Checkout plans are minted inline per
/// checkout, so they never show under the dashboard's Checkout Links and can only be
/// fixed through <c>PATCH /plans/{id}</c>. Only GBP plans are changed, and only the
/// <c>adaptive_pricing_enabled</c> flag: price, currency and plan id are never touched.
/// </summary>
public static class WhopPlanAdaptivePricing
{
    public sealed record PlanRow(
        string Id,
        string? ProductId,
        string? ProductTitle,
        decimal? InitialPrice,
        string? Currency,
        bool? AdaptivePricingEnabled,
        string? Visibility,
        string? CreatedAt,
        bool UsedByWebsiteCheckout);

    public sealed record Result(
        string AccountId,
        bool Applied,
        int TotalPlans,
        IReadOnlyDictionary<string, int> PlansByCurrency,
        IReadOnlyList<PlanRow> Targets,
        IReadOnlyList<PlanRow> SkippedNonGbp,
        IReadOnlyList<string> Failures,
        IReadOnlyList<PlanRow> AffectedAfter);

    // ponytail: hard page cap (100 x 100 plans); raise if the company ever exceeds 10k plans.
    private const int MaxPages = 100;

    public static async Task<Result> RunAsync(
        HttpClient http,
        string apiBaseUrl,
        string apiKey,
        string accountId,
        IReadOnlySet<string> websitePlanIds,
        bool apply,
        CancellationToken ct)
    {
        var before = await ListAllAsync(http, apiBaseUrl, apiKey, accountId, websitePlanIds, ct);
        var targets = before
            .Where(p => p.AdaptivePricingEnabled != false && string.Equals(p.Currency, "gbp", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var skipped = before
            .Where(p => p.AdaptivePricingEnabled != false && !string.Equals(p.Currency, "gbp", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var failures = new List<string>();
        IReadOnlyList<PlanRow> affectedAfter = targets;
        if (apply)
        {
            foreach (var plan in targets)
            {
                using var req = Request(HttpMethod.Patch, Combine(apiBaseUrl, $"plans/{Uri.EscapeDataString(plan.Id)}"), apiKey);
                req.Content = JsonContent.Create(new Dictionary<string, object?> { ["adaptive_pricing_enabled"] = false });
                using var resp = await http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    failures.Add($"{plan.Id}: HTTP {(int)resp.StatusCode}");
                }
            }

            var targetIds = targets.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            var after = await ListAllAsync(http, apiBaseUrl, apiKey, accountId, websitePlanIds, ct);
            affectedAfter = after.Where(p => targetIds.Contains(p.Id)).ToList();
        }

        return new Result(
            accountId,
            apply,
            before.Count,
            before.GroupBy(p => p.Currency ?? "?").ToDictionary(g => g.Key, g => g.Count()),
            targets,
            skipped,
            failures,
            affectedAfter);
    }

    private static async Task<List<PlanRow>> ListAllAsync(
        HttpClient http, string apiBaseUrl, string apiKey, string accountId, IReadOnlySet<string> websitePlanIds, CancellationToken ct)
    {
        var rows = new List<PlanRow>();
        string? after = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var url = Combine(apiBaseUrl, $"plans?account_id={Uri.EscapeDataString(accountId)}&first=100")
                + (after is null ? string.Empty : $"&after={Uri.EscapeDataString(after)}");
            using var req = Request(HttpMethod.Get, url, apiKey);
            using var resp = await http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                throw new PaymentGatewayApiException(PaymentGatewayNames.Whop, (int)resp.StatusCode, $"Whop list plans failed: {(int)resp.StatusCode}");
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                rows.AddRange(data.EnumerateArray().Select(p => ToRow(p, websitePlanIds)));
            }

            var hasNext = doc.RootElement.TryGetProperty("page_info", out var info)
                && info.TryGetProperty("has_next_page", out var next) && next.ValueKind == JsonValueKind.True;
            after = hasNext && info.TryGetProperty("end_cursor", out var cursor) ? cursor.GetString() : null;
            if (after is null) break;
        }

        return rows;
    }

    private static PlanRow ToRow(JsonElement p, IReadOnlySet<string> websitePlanIds)
    {
        var id = Str(p, "id") ?? string.Empty;
        var product = p.TryGetProperty("product", out var prod) && prod.ValueKind == JsonValueKind.Object ? prod : default;
        return new PlanRow(
            id,
            product.ValueKind == JsonValueKind.Object ? Str(product, "id") : null,
            product.ValueKind == JsonValueKind.Object ? Str(product, "title") : null,
            p.TryGetProperty("initial_price", out var price) && price.ValueKind == JsonValueKind.Number ? price.GetDecimal() : null,
            Str(p, "currency"),
            p.TryGetProperty("adaptive_pricing_enabled", out var ap) && ap.ValueKind is JsonValueKind.True or JsonValueKind.False ? ap.GetBoolean() : null,
            Str(p, "visibility"),
            Str(p, "created_at"),
            websitePlanIds.Contains(id));
    }

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static HttpRequestMessage Request(HttpMethod method, string url, string apiKey)
    {
        var req = new HttpRequestMessage(method, url)
        {
            Version = System.Net.HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey.Trim());
        req.Headers.TryAddWithoutValidation("User-Agent", "OetWithDrHesham/1.0");
        return req;
    }

    private static string Combine(string baseUrl, string path) => $"{baseUrl.TrimEnd('/')}/{path}";
}
