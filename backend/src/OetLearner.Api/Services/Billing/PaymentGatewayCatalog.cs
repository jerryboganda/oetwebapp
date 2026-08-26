using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.Billing;

public interface IPaymentGatewayCatalog
{
    Task<IReadOnlyList<PaymentGatewayToggle>> EnsureSeededAsync(CancellationToken ct);
    Task<IReadOnlyList<LearnerPaymentMethodDto>> ListLearnerMethodsAsync(string region, CancellationToken ct);
    Task<bool> IsEnabledAsync(string gatewayName, CancellationToken ct);
    Task<IReadOnlyList<AdminPaymentGatewayDto>> ListAdminAsync(CancellationToken ct);
    Task<AdminPaymentGatewayDto> UpdateAsync(string name, AdminPaymentGatewayUpdateRequest request, string? adminId, CancellationToken ct);
}

public sealed record LearnerPaymentMethodDto(
    string Name,
    string Label,
    string IconName,
    string Mode,
    string? Badge,
    bool Recommended,
    string Region);

public sealed record AdminPaymentGatewayDto(
    string Name,
    string Label,
    string CandidateLabel,
    string Region,
    string Mode,
    string IconName,
    bool IsEnabled,
    bool IsPrimary,
    int DisplayOrder,
    bool IsConfigured,
    string ApiKeyMasked,
    string HashKeyMasked,
    string? ProviderKey,
    string WebhookSecretMasked,
    string? CompanyId);

public sealed record AdminPaymentGatewayUpdateRequest(
    bool? IsEnabled,
    bool? IsPrimary,
    int? DisplayOrder,
    string? ApiKey,
    string? HashApiKey,
    string? ProviderKey,
    string? WebhookSecret,
    string? CompanyId);

public sealed class PaymentGatewayCatalog : IPaymentGatewayCatalog
{
    public const string SecretMask = "********";

    private readonly LearnerDbContext _db;
    private readonly IRuntimeSettingsProvider _runtimeSettings;
    private readonly IOptions<BillingOptions> _billing;

    public PaymentGatewayCatalog(
        LearnerDbContext db,
        IRuntimeSettingsProvider runtimeSettings,
        IOptions<BillingOptions> billing)
    {
        _db = db;
        _runtimeSettings = runtimeSettings;
        _billing = billing;
    }

    public async Task<IReadOnlyList<PaymentGatewayToggle>> EnsureSeededAsync(CancellationToken ct)
    {
        var rows = await _db.PaymentGatewayToggles.ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var added = false;
        foreach (var seed in DefaultCatalog())
        {
            if (rows.Any(row => string.Equals(row.Name, seed.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            seed.CreatedAt = now;
            seed.UpdatedAt = now;
            _db.PaymentGatewayToggles.Add(seed);
            rows.Add(seed);
            added = true;
        }

        if (added)
        {
            await _db.SaveChangesAsync(ct);
        }

        return rows.OrderBy(r => r.DisplayOrder).ThenBy(r => r.Name).ToList();
    }

    public async Task<IReadOnlyList<LearnerPaymentMethodDto>> ListLearnerMethodsAsync(string region, CancellationToken ct)
    {
        var rows = await EnsureSeededAsync(ct);
        var effective = await _runtimeSettings.GetAsync(ct);
        var sandbox = _billing.Value.AllowSandboxFallbacks;
        var wanted = string.IsNullOrWhiteSpace(region) ? PaymentGatewayRegions.Global : region.Trim().ToLowerInvariant();

        return rows
            .Where(row => IsVisibleInRegion(row.Region, wanted))
            .Where(row => sandbox || row.IsEnabled)
            .Where(row => sandbox || IsConfigured(row.Name, effective, sandbox))
            .OrderBy(row => row.DisplayOrder)
            .ThenBy(row => row.Name)
            .Select(row => new LearnerPaymentMethodDto(
                row.Name,
                row.CandidateLabel,
                row.IconName,
                row.Mode,
                row.IsPrimary ? "MAIN" : null,
                row.IsPrimary,
                row.Region))
            .ToList();
    }

    public async Task<bool> IsEnabledAsync(string gatewayName, CancellationToken ct)
    {
        var rows = await EnsureSeededAsync(ct);
        var name = gatewayName.Trim();
        var row = rows.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            return false;
        }

        return row.IsEnabled || _billing.Value.AllowSandboxFallbacks;
    }

    public async Task<IReadOnlyList<AdminPaymentGatewayDto>> ListAdminAsync(CancellationToken ct)
    {
        var rows = await EnsureSeededAsync(ct);
        var effective = await _runtimeSettings.GetAsync(ct);
        return rows
            .OrderBy(row => row.DisplayOrder)
            .ThenBy(row => row.Name)
            .Select(row => ToAdminDto(row, effective))
            .ToList();
    }

    public async Task<AdminPaymentGatewayDto> UpdateAsync(string name, AdminPaymentGatewayUpdateRequest request, string? adminId, CancellationToken ct)
    {
        var rows = await EnsureSeededAsync(ct);
        var row = rows.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Unknown payment gateway '{name}'.");

        var now = DateTimeOffset.UtcNow;
        if (request.IsEnabled.HasValue) row.IsEnabled = request.IsEnabled.Value;
        if (request.DisplayOrder.HasValue) row.DisplayOrder = request.DisplayOrder.Value;
        if (request.IsPrimary.HasValue)
        {
            if (request.IsPrimary.Value)
            {
                foreach (var other in rows.Where(r => r.Region == row.Region))
                {
                    other.IsPrimary = false;
                    other.UpdatedAt = now;
                }
            }

            row.IsPrimary = request.IsPrimary.Value;
        }

        row.UpdatedAt = now;
        row.UpdatedByAdminId = adminId;

        var settingsChanged = await ApplySecretsAsync(row.Name, request, ct);
        await _db.SaveChangesAsync(ct);
        if (settingsChanged)
        {
            _runtimeSettings.Invalidate();
        }

        var effective = await _runtimeSettings.GetAsync(ct);
        return ToAdminDto(row, effective);
    }

    private async Task<bool> ApplySecretsAsync(string gatewayName, AdminPaymentGatewayUpdateRequest request, CancellationToken ct)
    {
        if (!NeedsSecretWrite(request))
        {
            return false;
        }

        var row = await _db.RuntimeSettings.FirstOrDefaultAsync(r => r.Id == "default", ct);
        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            row = new RuntimeSettingsRow { Id = "default", UpdatedAt = now };
            _db.RuntimeSettings.Add(row);
        }

        var changed = false;
        if (string.Equals(gatewayName, PaymentGatewayNames.Whop, StringComparison.OrdinalIgnoreCase))
        {
            changed |= TryProtect(request.ApiKey, value => row.WhopApiKeyEncrypted = value);
            changed |= TryProtect(request.WebhookSecret, value => row.WhopWebhookSecretEncrypted = value);
            if (request.CompanyId is not null && request.CompanyId != SecretMask)
            {
                row.WhopCompanyId = string.IsNullOrWhiteSpace(request.CompanyId) ? null : request.CompanyId.Trim();
                changed = true;
            }
        }
        else if (string.Equals(gatewayName, PaymentGatewayNames.Fawaterak, StringComparison.OrdinalIgnoreCase))
        {
            changed |= TryProtect(request.HashApiKey ?? request.ApiKey, value => row.FawaterakHashApiKeyEncrypted = value);
            if (request.ProviderKey is not null && request.ProviderKey != SecretMask)
            {
                row.FawaterakProviderKey = string.IsNullOrWhiteSpace(request.ProviderKey) ? null : request.ProviderKey.Trim();
                changed = true;
            }
        }

        if (changed)
        {
            row.UpdatedAt = now;
            row.UpdatedByUserId = null;
        }

        return changed;
    }

    private bool TryProtect(string? input, Action<string?> setter)
    {
        if (input is null || input == SecretMask)
        {
            return false;
        }

        var trimmed = input.Trim();
        setter(trimmed.Length == 0 ? null : _runtimeSettings.Protect(trimmed));
        return true;
    }

    private static bool NeedsSecretWrite(AdminPaymentGatewayUpdateRequest request)
        => request.ApiKey is not null
           || request.HashApiKey is not null
           || request.ProviderKey is not null
           || request.WebhookSecret is not null
           || request.CompanyId is not null;

    private AdminPaymentGatewayDto ToAdminDto(PaymentGatewayToggle row, EffectiveSettings effective)
        => new(
            row.Name,
            row.Label,
            row.CandidateLabel,
            row.Region,
            row.Mode,
            row.IconName,
            row.IsEnabled,
            row.IsPrimary,
            row.DisplayOrder,
            IsConfigured(row.Name, effective, sandbox: false),
            Mask(row.Name is PaymentGatewayNames.Whop ? effective.Whop.ApiKey : null),
            Mask(row.Name is PaymentGatewayNames.Fawaterak ? effective.Fawaterak.HashApiKey : null),
            row.Name is PaymentGatewayNames.Fawaterak ? effective.Fawaterak.ProviderKey : row.Name is PaymentGatewayNames.Whop ? effective.Whop.CompanyId : null,
            Mask(row.Name is PaymentGatewayNames.Whop ? effective.Whop.WebhookSecret : null),
            row.Name is PaymentGatewayNames.Whop ? effective.Whop.CompanyId : null);

    private static string Mask(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : SecretMask;

    private static bool IsVisibleInRegion(string rowRegion, string wanted)
    {
        if (string.Equals(rowRegion, PaymentGatewayRegions.All, StringComparison.OrdinalIgnoreCase)
            || string.Equals(rowRegion, PaymentGatewayRegions.Global, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(wanted, PaymentGatewayRegions.Egypt, StringComparison.OrdinalIgnoreCase)
            && string.Equals(rowRegion, PaymentGatewayRegions.Egypt, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConfigured(string name, EffectiveSettings effective, bool sandbox)
        => name switch
        {
            PaymentGatewayNames.Whop => sandbox || effective.Whop.IsConfigured,
            PaymentGatewayNames.Fawaterak => sandbox || effective.Fawaterak.IsConfigured,
            PaymentGatewayNames.Stripe => sandbox || !string.IsNullOrWhiteSpace(effective.Billing.StripeSecretKey),
            PaymentGatewayNames.PayPal => sandbox || (!string.IsNullOrWhiteSpace(effective.Billing.PayPalClientId)
                                                       && !string.IsNullOrWhiteSpace(effective.Billing.PayPalClientSecret)),
            PaymentGatewayNames.Paymob => sandbox || effective.Paymob.IsConfigured,
            PaymentGatewayNames.PayTabs => sandbox || effective.PayTabs.IsConfigured,
            PaymentGatewayNames.EasyKash => sandbox || effective.EasyKash.IsConfigured,
            PaymentGatewayNames.CheckoutCom => sandbox || effective.CheckoutCom.IsConfigured,
            _ => false,
        };

    public static IReadOnlyList<PaymentGatewayToggle> DefaultCatalog()
    {
        var now = DateTimeOffset.UtcNow;
        return
        [
            New("pgt-whop", PaymentGatewayNames.Whop, "Whop", "Pay with Whop — MAIN", PaymentGatewayRegions.Global, PaymentGatewayModes.Embedded, "credit-card", enabled: true, primary: true, order: 10, now),
            New("pgt-fawaterak", PaymentGatewayNames.Fawaterak, "Fawaterak", "Pay with Fawaterak", PaymentGatewayRegions.Global, PaymentGatewayModes.Iframe, "wallet", enabled: true, primary: false, order: 20, now),
            New("pgt-stripe", PaymentGatewayNames.Stripe, "Stripe", "Pay with Stripe", PaymentGatewayRegions.Global, PaymentGatewayModes.Redirect, "credit-card", enabled: false, primary: false, order: 30, now),
            New("pgt-paypal", PaymentGatewayNames.PayPal, "PayPal", "Pay with PayPal", PaymentGatewayRegions.Global, PaymentGatewayModes.Embedded, "paypal", enabled: false, primary: false, order: 40, now),
            New("pgt-checkoutcom", PaymentGatewayNames.CheckoutCom, "Checkout.com", "Pay with Checkout.com", PaymentGatewayRegions.Egypt, PaymentGatewayModes.Redirect, "credit-card", enabled: false, primary: false, order: 45, now),
            New("pgt-paymob", PaymentGatewayNames.Paymob, "Paymob", "Pay with Paymob", PaymentGatewayRegions.Egypt, PaymentGatewayModes.Redirect, "wallet", enabled: false, primary: false, order: 50, now),
            New("pgt-paytabs", PaymentGatewayNames.PayTabs, "PayTabs", "Pay with PayTabs", PaymentGatewayRegions.Egypt, PaymentGatewayModes.Redirect, "credit-card", enabled: false, primary: false, order: 60, now),
            New("pgt-easykash", PaymentGatewayNames.EasyKash, "Easy Cash", "Pay with Easy Cash", PaymentGatewayRegions.Egypt, PaymentGatewayModes.Redirect, "wallet", enabled: false, primary: false, order: 70, now),
        ];
    }

    private static PaymentGatewayToggle New(
        string id,
        string name,
        string label,
        string candidateLabel,
        string region,
        string mode,
        string icon,
        bool enabled,
        bool primary,
        int order,
        DateTimeOffset now)
        => new()
        {
            Id = id,
            Name = name,
            Label = label,
            CandidateLabel = candidateLabel,
            Region = region,
            Mode = mode,
            IconName = icon,
            IsEnabled = enabled,
            IsPrimary = primary,
            DisplayOrder = order,
            CreatedAt = now,
            UpdatedAt = now,
        };
}
