namespace OetLearner.Api.Contracts;

/// <summary>Request body for <c>POST /v1/auth/step-up</c>. <see cref="Scope"/> is a
/// short action key such as <c>billing.mark_paid</c> or <c>billing.refund</c>.</summary>
public sealed record StepUpRequest(string? Code, string? Scope);

/// <summary>Short-lived, single-scope proof-of-recent-TOTP presented on the
/// <c>X-OET-Step-Up</c> header of a money-moving admin call.</summary>
public sealed record StepUpResponse(string StepUpToken, DateTimeOffset ExpiresAt, string Scope);
