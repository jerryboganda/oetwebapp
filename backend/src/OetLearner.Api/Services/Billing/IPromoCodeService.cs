namespace OetLearner.Api.Services.Billing;

public interface IPromoCodeService
{
    Task<PromoCodeValidationResult> ValidateAsync(string code, string? userId = null, CancellationToken ct = default);
    Task<string> CreatePromoCodeAsync(CreatePromoCodeRequest request, CancellationToken ct = default);
    Task DeactivateAsync(string code, CancellationToken ct = default);
}

public sealed record PromoCodeValidationResult(
    bool IsValid,
    string? Reason,
    decimal? DiscountPercent,
    decimal? DiscountAmount,
    string? Currency
);

public sealed record CreatePromoCodeRequest(
    string Code,
    string? Name,
    decimal? PercentOff,
    decimal? AmountOff,
    string? Currency,
    DateTimeOffset? ExpiresAt
);
