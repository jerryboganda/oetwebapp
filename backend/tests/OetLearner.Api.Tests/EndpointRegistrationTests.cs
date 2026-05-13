using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class EndpointRegistrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    private static readonly AntiforgeryEndpointExpectation[] ExpectedAntiforgeryExceptions =
    [
        new("/v1/admin/users/import", "POST", "AdminUsersWrite", "PerUserWrite"),
        new("/v1/admin/vocabulary/import/preview", "POST", "AdminContentRead", "PerUserWrite"),
        new("/v1/admin/vocabulary/import", "POST", "AdminContentWrite", "PerUserWrite"),
        new("/v1/admin/vocabulary/import/batches/{importBatchId}/reconcile", "POST", "AdminContentRead", "PerUserWrite"),
        new("/v1/pronunciation/drills/{drillId}/attempt/{attemptId}/audio", "POST", "LearnerOnly", "PerUserWrite"),
        new("/v1/media/upload", "POST", null, "PerUserWrite"),
        new("/v1/admin/uploads/{uploadId}/parts/{partNumber:int}", "PUT", "AdminContentWrite", "PerUserWrite"),
        new("/v1/admin/imports/zip", "POST", "AdminContentWrite", "PerUserWrite"),
    ];

    public EndpointRegistrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/v1/recalls/queue")]
    [InlineData("/v1/recalls/audio/{termId}")]
    [InlineData("/v1/expert/calibration/speaking/samples")]
    [InlineData("/v1/expert/calibration/speaking/samples/{sampleId}/scores")]
    [InlineData("/v1/expert/speaking/attempts/{attemptId}/comments")]
    [InlineData("/v1/speaking/attempts/{attemptId}/comments")]
    [InlineData("/v1/admin/content/staleness")]
    [InlineData("/v1/admin/ai-config/escalation-stats")]
    [InlineData("/v1/admin/rulebooks")]
    [InlineData("/v1/admin/listening/analytics")]
    [InlineData("/v1/admin/listening/attempts/{attemptId}/export")]
    [InlineData("/v1/admin/listening/backfill")]
    [InlineData("/v1/admin/reading/analytics")]
    [InlineData("/v1/writing/attempts/{attemptId}/pdf")]
    [InlineData("/v1/mocks/attempts/{mockAttemptId}/sections/writing/pdf")]
    public void Program_RegistersFeatureRoutes(string routePattern)
    {
        using var client = _factory.CreateClient();
        var registeredRoutes = _factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => NormalizeRoutePattern(endpoint.RoutePattern.RawText))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(NormalizeRoutePattern(routePattern), registeredRoutes);
    }

    [Fact]
    public void Program_DisableAntiforgeryExceptions_AreExplicitlyControlled()
    {
        using var client = _factory.CreateClient();
        var disabledEndpoints = _factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(DisablesAntiforgery)
            .Select(endpoint => new
            {
                Endpoint = endpoint,
                Pattern = NormalizeRoutePattern(endpoint.RoutePattern.RawText),
            })
            .ToDictionary(endpoint => endpoint.Pattern, StringComparer.Ordinal);

        Assert.Equal(
            ExpectedAntiforgeryExceptions.Select(endpoint => endpoint.RoutePattern).Order(StringComparer.Ordinal),
            disabledEndpoints.Keys.Order(StringComparer.Ordinal));

        foreach (var expected in ExpectedAntiforgeryExceptions)
        {
            Assert.True(disabledEndpoints.TryGetValue(expected.RoutePattern, out var actual),
                $"Missing DisableAntiforgery endpoint {expected.RoutePattern}.");

            var methods = actual.Endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            Assert.Contains(expected.Method, methods, StringComparer.OrdinalIgnoreCase);

            var authMetadata = actual.Endpoint.Metadata.OfType<IAuthorizeData>().ToArray();
            Assert.NotEmpty(authMetadata);

            if (!string.IsNullOrWhiteSpace(expected.RequiredPolicy))
            {
                Assert.Contains(authMetadata, metadata =>
                    string.Equals(metadata.Policy, expected.RequiredPolicy, StringComparison.Ordinal));
            }

            Assert.Contains(expected.RateLimitPolicy, GetRateLimitPolicies(actual.Endpoint), StringComparer.Ordinal);
        }
    }

    private static string NormalizeRoutePattern(string? routePattern)
        => string.IsNullOrWhiteSpace(routePattern) ? string.Empty : routePattern.TrimEnd('/');

    private static bool DisablesAntiforgery(RouteEndpoint endpoint)
        => endpoint.Metadata.Any(metadata =>
        {
            var type = metadata.GetType();
            if (!type.Name.Contains("Antiforgery", StringComparison.Ordinal))
            {
                return false;
            }

            var property = type.GetProperty("RequiresValidation");
            return property?.PropertyType == typeof(bool)
                && property.GetValue(metadata) is false;
        });

    private static IEnumerable<string> GetRateLimitPolicies(RouteEndpoint endpoint)
        => endpoint.Metadata
            .Select(metadata => metadata.GetType().GetProperty("PolicyName")?.GetValue(metadata) as string)
            .Where(policyName => !string.IsNullOrWhiteSpace(policyName))!;

    private sealed record AntiforgeryEndpointExpectation(
        string RoutePattern,
        string Method,
        string? RequiredPolicy,
        string RateLimitPolicy);
}
