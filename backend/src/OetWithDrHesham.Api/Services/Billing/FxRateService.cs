using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Settings;

namespace OetWithDrHesham.Api.Services.Billing;

/// <summary>
/// FX rate provider. Resolves the most-recent stored rate, refreshing
/// from the configured upstream (default: openexchangerates.org-compatible
/// JSON endpoint) on demand.
/// </summary>
public interface IFxRateService
{
    Task<decimal> GetRateAsync(string fromCurrency, string toCurrency, CancellationToken ct);
    Task<decimal> ConvertAsync(decimal amount, string fromCurrency, string toCurrency, CancellationToken ct);
    Task<int> RefreshRatesAsync(CancellationToken ct);
}

public sealed class FxRateService : IFxRateService
{
    private readonly LearnerDbContext _db;
    private readonly HttpClient _http;
    private readonly IOptions<FxOptions> _options;
    private readonly IRuntimeSettingsProvider _runtimeSettings;
    private readonly ILogger<FxRateService> _logger;

    // Currencies we care about for the launch regions + global fallback.
    private static readonly string[] SupportedCurrencies =
        new[] { "USD", "GBP", "EUR", "AED", "SAR", "OMR", "QAR", "KWD", "BHD", "EGP", "PKR", "AUD", "INR", "CAD", "JPY", "CHF", "SGD" };

    public FxRateService(LearnerDbContext db, HttpClient http, IOptions<FxOptions> options, IRuntimeSettingsProvider runtimeSettings, ILogger<FxRateService> logger)
    {
        _db = db;
        _http = http;
        _options = options;
        _runtimeSettings = runtimeSettings;
        _logger = logger;
    }

    public async Task<decimal> GetRateAsync(string fromCurrency, string toCurrency, CancellationToken ct)
    {
        var from = fromCurrency.ToUpperInvariant();
        var to = toCurrency.ToUpperInvariant();
        if (from == to) return 1m;

        var row = await _db.ExchangeRates
            .Where(r => r.FromCurrency == from && r.ToCurrency == to)
            .OrderByDescending(r => r.EffectiveFrom)
            .Select(r => new { r.Rate, r.EffectiveFrom })
            .FirstOrDefaultAsync(ct);

        // Stale (>24h) or missing → trigger refresh and re-read.
        if (row is null || (DateTimeOffset.UtcNow - row.EffectiveFrom).TotalHours > 24)
        {
            try { await RefreshRatesAsync(ct); } catch (Exception ex) { _logger.LogWarning(ex, "FX refresh failed"); }
            row = await _db.ExchangeRates
                .Where(r => r.FromCurrency == from && r.ToCurrency == to)
                .OrderByDescending(r => r.EffectiveFrom)
                .Select(r => new { r.Rate, r.EffectiveFrom })
                .FirstOrDefaultAsync(ct);
        }

        if (row is null)
        {
            throw new InvalidOperationException($"No FX rate available for {from}->{to}.");
        }
        return row.Rate;
    }

    public async Task<decimal> ConvertAsync(decimal amount, string fromCurrency, string toCurrency, CancellationToken ct)
    {
        var rate = await GetRateAsync(fromCurrency, toCurrency, ct);
        return decimal.Round(amount * rate, 4);
    }

    public async Task<int> RefreshRatesAsync(CancellationToken ct)
    {
        // Wave 4: FX base currency / API key / base URL are DB-overridable.
        var fx = (await _runtimeSettings.GetAsync(ct)).Fx;
        var opts = new FxOptions
        {
            BaseCurrency = string.IsNullOrWhiteSpace(fx.BaseCurrency) ? _options.Value.BaseCurrency : fx.BaseCurrency,
            ApiKey = fx.ApiKey,
            ApiBaseUrl = fx.ApiBaseUrl,
            DynamicPricingEnabled = fx.DynamicPricingEnabled,
        };
        var baseCurrency = opts.BaseCurrency.ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;
        Dictionary<string, decimal> latest;

        if (!string.IsNullOrWhiteSpace(opts.ApiKey) && !string.IsNullOrWhiteSpace(opts.ApiBaseUrl))
        {
            try
            {
                var url = $"{opts.ApiBaseUrl.TrimEnd('/')}/latest.json?app_id={opts.ApiKey}&base={baseCurrency}";
                using var resp = await _http.GetAsync(url, ct);
                resp.EnsureSuccessStatusCode();
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var rates = doc.RootElement.GetProperty("rates");
                latest = rates.EnumerateObject()
                    .Where(p => SupportedCurrencies.Contains(p.Name))
                    .ToDictionary(p => p.Name, p => p.Value.GetDecimal());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FX provider call failed — falling back to static seed.");
                latest = SeedFallback(baseCurrency);
            }
        }
        else
        {
            latest = SeedFallback(baseCurrency);
        }

        int inserted = 0;
        foreach (var (currency, rate) in latest)
        {
            if (currency == baseCurrency) continue;
            _db.ExchangeRates.Add(new ExchangeRate
            {
                Id = Guid.NewGuid().ToString("N"),
                FromCurrency = baseCurrency,
                ToCurrency = currency,
                Rate = rate,
                EffectiveFrom = now,
                Source = string.IsNullOrEmpty(opts.ApiKey) ? "seed" : "open_exchange_rates",
                CreatedAt = now,
            });

            // Insert inverse for convenience.
            if (rate != 0m)
            {
                _db.ExchangeRates.Add(new ExchangeRate
                {
                    Id = Guid.NewGuid().ToString("N"),
                    FromCurrency = currency,
                    ToCurrency = baseCurrency,
                    Rate = decimal.Round(1m / rate, 6),
                    EffectiveFrom = now,
                    Source = string.IsNullOrEmpty(opts.ApiKey) ? "seed" : "open_exchange_rates",
                    CreatedAt = now,
                });
            }
            inserted += 2;
        }
        await _db.SaveChangesAsync(ct);
        return inserted;
    }

    /// <summary>
    /// Offline fallback rates (May 2026 reference, USD base). Used when no
    /// upstream FX provider is configured so the system stays functional in dev.
    /// </summary>
    private static Dictionary<string, decimal> SeedFallback(string baseCurrency)
    {
        var usdBase = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 1m,
            ["GBP"] = 0.79m,
            ["EUR"] = 0.92m,
            ["AED"] = 3.67m,
            ["SAR"] = 3.75m,
            ["OMR"] = 0.385m,
            ["QAR"] = 3.64m,
            ["KWD"] = 0.307m,
            ["BHD"] = 0.376m,
            ["EGP"] = 47.85m,
            ["PKR"] = 280m,
            ["AUD"] = 1.51m,
            ["INR"] = 83.5m,
            ["CAD"] = 1.36m,
            ["JPY"] = 156m,
            ["CHF"] = 0.91m,
            ["SGD"] = 1.34m,
        };

        if (baseCurrency == "USD") return usdBase;

        if (!usdBase.TryGetValue(baseCurrency, out var basePerUsd) || basePerUsd == 0m)
        {
            return usdBase; // best effort
        }

        return usdBase.ToDictionary(
            kvp => kvp.Key,
            kvp => decimal.Round(kvp.Value / basePerUsd, 6));
    }
}

/// <summary>Background worker — refreshes FX rates daily.</summary>
public sealed class FxRateRefreshWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<FxRateRefreshWorker> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(24);

    public FxRateRefreshWorker(IServiceProvider services, ILogger<FxRateRefreshWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken); } catch { }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<IFxRateService>();
                var n = await svc.RefreshRatesAsync(stoppingToken);
                _logger.LogInformation("Refreshed {N} FX rates.", n);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FxRateRefreshWorker iteration failed.");
            }

            try { await Task.Delay(_interval, stoppingToken); } catch { }
        }
    }
}
