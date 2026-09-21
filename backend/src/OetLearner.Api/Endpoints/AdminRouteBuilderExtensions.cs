namespace OetLearner.Api.Endpoints;

/// <summary>
/// Route-builder extensions that collapse the repeated auth + rate-limit
/// boilerplate applied to every admin endpoint handler.
///
/// Before:
/// <code>
/// admin.MapPost("/x", handler)
///      .RequireRateLimiting("PerUserWrite")
///      .RequireAuthorization("AdminContentWrite");
/// </code>
///
/// After:
/// <code>
/// admin.MapPost("/x", handler).WithAdminWrite("AdminContentWrite");
/// </code>
///
/// The admin <see cref="RouteGroupBuilder"/> still applies
/// <c>RequireAuthorization("AdminOnly")</c> and <c>RequireRateLimiting("PerUser")</c>
/// at group level; these helpers layer the per-endpoint permission (and, for
/// writes, the write-bucket rate limit) on top.
/// </summary>
internal static class AdminRouteBuilderExtensions
{
    /// <summary>
    /// Apply the standard admin write-endpoint policy: write-bucket rate limit
    /// plus the granular permission check.
    /// </summary>
    public static RouteHandlerBuilder WithAdminWrite(this RouteHandlerBuilder builder, string permission)
        => builder
            .RequireRateLimiting("PerUserWrite")
            .RequireAuthorization(permission);

    /// <summary>
    /// Apply the standard admin read-endpoint policy: the granular permission
    /// check only (the group-level <c>PerUser</c> read rate limit already applies).
    /// </summary>
    public static RouteHandlerBuilder WithAdminRead(this RouteHandlerBuilder builder, string permission)
        => builder.RequireAuthorization(permission);
}
