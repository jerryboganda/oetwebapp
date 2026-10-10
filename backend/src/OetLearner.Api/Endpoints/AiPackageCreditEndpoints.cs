using System.Security.Claims;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Endpoints;

public static class AiPackageCreditEndpoints
{
    public static IEndpointRouteBuilder MapAiPackageCreditEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1");

        v1.MapGet("/me/ai-package-credits", async (HttpContext http, IAiPackageCreditService service, CancellationToken ct, int? pageSize) =>
            Results.Ok(RedactAdminDetails(await service.GetSnapshotAsync(http.UserId(), pageSize ?? 50, ct))))
            .RequireAuthorization();

        var admin = v1.MapGroup("/admin/ai-package-credits");
        admin.MapGet("/{userId}", async (string userId, IAiPackageCreditService service, CancellationToken ct, int? pageSize) =>
            Results.Ok(await service.GetSnapshotAsync(userId, pageSize ?? 100, ct)))
            .RequireAuthorization("AdminBillingRead");

        admin.MapPost("/{userId}/adjust", async (
            string userId,
            AiPackageCreditAdjustmentRequest request,
            HttpContext http,
            IAiPackageCreditService service,
            CancellationToken ct) =>
                Results.Ok(await service.AdjustAsync(userId, request, http.AdminId(), ct)))
            .WithAdminWrite("AdminBillingSubscriptionWrite");

        admin.MapPost("/{userId}/exam-outcomes", async (
            string userId,
            LearnerExamOutcomeRequest request,
            HttpContext http,
            IAiPackageCreditService service,
            CancellationToken ct) =>
                Results.Ok(await service.RecordExamOutcomeAsync(userId, request, http.AdminId(), http.AdminName(), ct)))
            .WithAdminWrite("AdminBillingSubscriptionWrite");

        return app;
    }

    private const string SupportAdjustmentLabel = "Support adjustment";
    private const string AdminAdjustmentReason = nameof(OetLearner.Api.Domain.AiPackageCreditReason.AdminAdjustment);

    // Learner view only. An AdminAdjustment ledger row carries the acting admin's id
    // ("admin:{adminId}:{guid}") and the admin's free-text audit reason, and the snapshot
    // copies that reason into grant descriptions and bucket sourcePackages. None of it
    // may reach the learner; the admin endpoints keep the full snapshot. Balances and
    // every other number are passed through untouched.
    private static AiPackageCreditSnapshot RedactAdminDetails(AiPackageCreditSnapshot snapshot) => snapshot with
    {
        Transactions = snapshot.Transactions.Select(RedactTransaction).ToList(),
        Buckets = snapshot.Buckets?.Select(RedactBucket).ToList(),
        Shared = RedactNamedBucket(snapshot.Shared),
        Flexible = RedactNamedBucket(snapshot.Flexible),
        Writing = RedactNamedBucket(snapshot.Writing),
        Speaking = RedactNamedBucket(snapshot.Speaking),
        Listening = RedactNamedBucket(snapshot.Listening),
        Reading = RedactNamedBucket(snapshot.Reading),
        Mocks = RedactNamedBucket(snapshot.Mocks),
    };

    private static AiPackageCreditTransactionDto RedactTransaction(AiPackageCreditTransactionDto transaction)
        => string.Equals(transaction.Reason, AdminAdjustmentReason, StringComparison.Ordinal)
            ? transaction with { ReferenceId = null, SourceReferenceId = null, Description = SupportAdjustmentLabel }
            : transaction;

    // Admin adjustments are stored without a PackageId (their admin lots use "admin").
    private static bool IsAdminPackageId(string? packageId)
        => string.IsNullOrWhiteSpace(packageId) || string.Equals(packageId, "admin", StringComparison.OrdinalIgnoreCase);

    private static bool IsAdminGrant(AiPackageCreditGrantSourceDto grant) => IsAdminPackageId(grant.PackageId);

    private static AiPackageCreditBucketDto RedactBucket(AiPackageCreditBucketDto bucket)
    {
        if (!bucket.Grants.Any(IsAdminGrant))
        {
            return bucket;
        }

        // SourcePackages is the service's joined, de-duplicated grant names (reversed
        // grants excluded); rebuild it the same way from the redacted grants.
        var names = bucket.Grants
            .Where(grant => grant.Status != "reversed")
            .Select(SourceLabel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return bucket with
        {
            Grants = bucket.Grants
                .Select(grant => IsAdminGrant(grant) ? (grant with { Description = SupportAdjustmentLabel }) : grant)
                .ToList(),
            SourcePackages = names.Count == 0 ? null : string.Join(", ", names),
        };
    }

    // Mirrors AiPackageCreditService.HumanizePackageName (private there) for non-admin grants.
    private static string SourceLabel(AiPackageCreditGrantSourceDto grant)
    {
        var id = grant.PackageId;
        if (IsAdminGrant(grant) || string.IsNullOrWhiteSpace(id))
        {
            return SupportAdjustmentLabel;
        }

        if (id.StartsWith("pkg_", StringComparison.OrdinalIgnoreCase))
        {
            id = id["pkg_".Length..];
        }

        return id.Replace('_', ' ').Replace('-', ' ');
    }

    private static AiPackageCreditBucketSnapshot? RedactNamedBucket(AiPackageCreditBucketSnapshot? bucket)
        => bucket is null || !bucket.SourcePackages.Any(IsAdminPackageId)
            ? bucket
            : bucket with
            {
                SourcePackages = bucket.SourcePackages
                    .Select(id => IsAdminPackageId(id) ? SupportAdjustmentLabel : id)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            };

    private static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? httpContext.User.FindFirstValue("sub")
           ?? httpContext.User.Identity?.Name
           ?? string.Empty;

    private static string AdminId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? httpContext.User.FindFirstValue("sub")
           ?? httpContext.User.Identity?.Name
           ?? "admin";

    private static string AdminName(this HttpContext httpContext)
        => httpContext.User.Identity?.Name ?? AdminId(httpContext);
}
