using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class AdminEndpointAuthorizationInventoryTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly string[] AllowedAnyAdminRoutes =
    [
        "/v1/admin/dashboard",
        "/v1/admin/revenue",
    ];

    private static readonly string[] MutatingMethods = ["POST", "PUT", "PATCH", "DELETE"];

    private readonly TestWebApplicationFactory _factory;

    public AdminEndpointAuthorizationInventoryTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void AdminEndpoints_RequireGranularPolicyBeyondAdminOnly()
    {
        using var client = _factory.CreateClient();
        var offenders = AdminEndpoints()
            .Where(endpoint => !IsAllowedAnyAdminRoute(endpoint.RoutePattern.RawText))
            .Select(endpoint => new
            {
                Route = NormalizeRoutePattern(endpoint.RoutePattern.RawText),
                Policies = AuthorizationPolicies(endpoint),
            })
            .Where(endpoint => !endpoint.Policies.Any(policy =>
                policy.StartsWith("Admin", StringComparison.Ordinal) && policy != "AdminOnly"))
            .Select(endpoint => $"{endpoint.Route} [{string.Join(", ", endpoint.Policies)}]")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void AdminMutations_RequirePerUserWriteRateLimit()
    {
        using var client = _factory.CreateClient();
        var offenders = AdminEndpoints()
            .Where(endpoint => HttpMethods(endpoint).Overlaps(MutatingMethods))
            .Where(endpoint => !RateLimitPolicies(endpoint).Contains("PerUserWrite", StringComparer.Ordinal))
            .Select(endpoint => NormalizeRoutePattern(endpoint.RoutePattern.RawText))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("/v1/admin/private-speaking/config", "GET", "AdminReviewOps")]
    [InlineData("/v1/admin/private-speaking/tutors", "GET", "AdminReviewOps")]
    [InlineData("/v1/admin/private-speaking/bookings", "GET", "AdminReviewOps")]
    [InlineData("/v1/admin/alerts", "GET", "AdminSystemAdmin")]
    [InlineData("/v1/admin/launch-readiness/settings", "GET", "AdminSystemAdmin")]
    [InlineData("/v1/admin/flags", "GET", "AdminFeatureFlags")]
    [InlineData("/v1/admin/audit-logs", "GET", "AdminAuditLogs")]
    [InlineData("/v1/admin/programs", "GET", "AdminContentRead")]
    [InlineData("/v1/admin/content/inventory", "GET", "AdminContentRead")]
    public void SensitiveAdminRoutes_UseExpectedGranularPolicies(string routePattern, string method, string policy)
    {
        using var client = _factory.CreateClient();
        var endpoints = AdminEndpoints()
            .Where(endpoint => NormalizeRoutePattern(endpoint.RoutePattern.RawText) == NormalizeRoutePattern(routePattern))
            .Where(endpoint => HttpMethods(endpoint).Contains(method, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, endpoint =>
            Assert.Contains(policy, AuthorizationPolicies(endpoint), StringComparer.Ordinal));
    }

    private IEnumerable<RouteEndpoint> AdminEndpoints()
        => _factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => NormalizeRoutePattern(endpoint.RoutePattern.RawText).StartsWith("/v1/admin", StringComparison.Ordinal));

    private static string NormalizeRoutePattern(string? routePattern)
        => string.IsNullOrWhiteSpace(routePattern) ? string.Empty : routePattern.TrimEnd('/');

    private static bool IsAllowedAnyAdminRoute(string? routePattern)
        => AllowedAnyAdminRoutes.Contains(NormalizeRoutePattern(routePattern), StringComparer.Ordinal);

    private static string[] AuthorizationPolicies(RouteEndpoint endpoint)
        => endpoint.Metadata.OfType<IAuthorizeData>()
            .Select(metadata => metadata.Policy)
            .Where(policy => !string.IsNullOrWhiteSpace(policy))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static HashSet<string> HttpMethods(RouteEndpoint endpoint)
        => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string[] RateLimitPolicies(RouteEndpoint endpoint)
        => endpoint.Metadata
            .Select(metadata => metadata.GetType().GetProperty("PolicyName")?.GetValue(metadata) as string)
            .Where(policyName => !string.IsNullOrWhiteSpace(policyName))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
