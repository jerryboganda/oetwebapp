using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Assessment;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Services;

public interface IWritingEntitlementService
{
    Task<WritingEntitlement> CheckAsync(string? userId, CancellationToken ct);

    /// <summary>
    /// The single canonical "Practice this" gate (Writing Rule Enforcement
    /// Addendum Rev5, 10 Sep 2026, §12): resolves the SAME entitlement decision
    /// as <see cref="CheckAsync"/> (so Dashboard, Submit-for-Grading and
    /// Practice-this can never disagree) and, only when that decision resolves
    /// to a finite AI-package credit balance, performs the actual debit.
    /// Unlimited and free-tier entitlements authorise with zero deduction — a
    /// zero balance in an unrelated pool (e.g. the AI-package ledger) must not
    /// block an Unlimited or free-tier learner. Idempotent on
    /// <paramref name="referenceId"/>: mirrors ReadingAttemptService's Gate 6 +
    /// CreditGateExtensions pattern, so a repeated tap / network retry /
    /// refresh for the SAME logical start action resumes the same
    /// authorisation and never charges twice. When the authorisation
    /// resolves to Allowed, an attempt-level entitlement/billing record
    /// (candidate, <paramref name="taskId"/>, entitlement source,
    /// unlimited/finite status, charged amount, timestamp, idempotency key)
    /// is persisted via the AnalyticsEvents audit trail (§12: "Persist an
    /// attempt-level entitlement/billing record at start").
    /// </summary>
    Task<WritingStartAuthorization> AuthorizeStartAsync(string? userId, string referenceId, string? taskId, CancellationToken ct);

    /// <summary>
    /// Deterministic per-(user, scenario) start reference for the writing-v2
    /// "Practice this" gate (§12.4). Folds in the count of already-graded,
    /// non-mock <see cref="OetLearner.Api.Domain.WritingSubmission"/> rows for
    /// this scenario: the count stays 0 while the learner's current attempt
    /// has no graded submission yet, so a retry / refresh / duplicate start
    /// before that point recomputes the SAME reference and
    /// <see cref="AuthorizeStartAsync"/> dedupes it for free (no regression of
    /// the already-fixed resume behaviour). The count advances the moment a
    /// submission for this scenario reaches Graded, so "Practice this again"
    /// afterwards gets a brand-new reference — and therefore a genuinely new,
    /// charged authorisation — instead of replaying the first attempt's
    /// already-spent credit transaction forever.
    /// </summary>
    Task<string> BuildScenarioStartReferenceIdAsync(string? userId, Guid scenarioId, CancellationToken ct);
}

public sealed record WritingEntitlement(
    bool Allowed,
    string Tier,
    int Remaining,
    int LimitPerWindow,
    int WindowDays,
    DateTimeOffset? ResetAt,
    string Reason);

/// <summary>
/// Outcome of <see cref="IWritingEntitlementService.AuthorizeStartAsync"/>.
/// <see cref="EntitlementSource"/> is one of "unlimited" | "free_tier" |
/// "free_sample" | "ai_package" | "none" (blocked). <see cref="Charged"/> is true
/// only for the "ai_package" source — Unlimited, free-tier and free-sample
/// authorisations never deduct.
/// </summary>
public sealed record WritingStartAuthorization(
    bool Allowed,
    string EntitlementSource,
    bool Charged,
    string? ErrorCode,
    string? ErrorMessage,
    string? FeedbackMessage);

public sealed record GeneratedDownloadFile(Stream Stream, string ContentType, string FileName);

public sealed record PaymentWebhookRetryResult(
    string EventId,
    string Status,
    string ProcessingStatus,
    string? ErrorMessage,
    int AttemptCount,
    int RetryCount,
    string? GatewayTransactionId,
    string? NormalizedStatus);

/// <summary>
/// Outcome of a synchronous, server-side order capture (PayPal Expanded checkout).
/// <see cref="Status"/> is one of "completed" | "failed" | "pending". <see cref="RedirectTo"/>
/// is a best-effort post-purchase destination; the client may override it.
/// </summary>
public sealed record PaymentCaptureResult(
    string Status,
    string OrderId,
    string? CaptureId,
    string? RedirectTo,
    string? FailureReason);
