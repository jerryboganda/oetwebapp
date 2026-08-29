namespace OetLearner.Api.Contracts;

public sealed record AiBudgetOverrideRequest(
    string? Scope,
    decimal AmountUsd,
    string? Reason,
    DateTimeOffset ExpiresAt);
