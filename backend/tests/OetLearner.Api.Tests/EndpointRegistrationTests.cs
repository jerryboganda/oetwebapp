using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class EndpointRegistrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

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

    private static string NormalizeRoutePattern(string? routePattern)
        => string.IsNullOrWhiteSpace(routePattern) ? string.Empty : routePattern.TrimEnd('/');
}
