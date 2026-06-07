namespace OetLearner.Api.Contracts;

/// <summary>
/// Shared outcome shape for admin bulk-workflow operations that delegate to
/// per-item service methods (e.g. content-paper bulk publish/archive). Sibling
/// bulk endpoints reuse this so the admin UI gets a uniform count-style result.
/// </summary>
/// <param name="TotalRequested">Number of ids supplied by the caller (before de-duplication).</param>
/// <param name="Succeeded">Items that completed the requested action.</param>
/// <param name="Skipped">Items already in the target state (no-op).</param>
/// <param name="Failed">Items that failed a validation / status-gate check.</param>
/// <param name="Errors">Concise per-item error messages (capped — see implementation).</param>
public sealed record BulkActionResult(
    int TotalRequested,
    int Succeeded,
    int Skipped,
    int Failed,
    string[] Errors);

/// <summary>
/// Request body for <c>POST /v1/admin/papers/bulk</c>. <paramref name="Action"/>
/// must be one of: archive, publish, unpublish, submit-for-review,
/// approve-publish, reject. <paramref name="Reason"/> is required for reject.
/// </summary>
public sealed record ContentPaperBulkRequest(string Action, string[] Ids, string? Reason = null);
