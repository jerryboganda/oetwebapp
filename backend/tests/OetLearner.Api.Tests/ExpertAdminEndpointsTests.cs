using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OetLearner.Api.Endpoints;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

[Collection("AuthFlows")]
public class ExpertAdminEndpointsTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public ExpertAdminEndpointsTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListExperts_ReturnsExpertsWithSpecialties_ForAdmin()
    {
        using var client = CreateAdminClient(_factory);

        var response = await client.GetAsync("/v1/admin/experts");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
    }

    [Fact]
    public async Task PatchSpecialties_RejectsUnknownProfession()
    {
        using var client = CreateAdminClient(_factory);

        var response = await client.PatchAsJsonAsync(
            "/v1/admin/experts/expert-001/specialties",
            new ExpertSpecialtiesUpdateDto(new[] { "not_a_profession" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("unknown_profession", body);
    }

    [Fact]
    public async Task PatchSpecialties_PersistsCanonicalProfessions()
    {
        using var client = CreateAdminClient(_factory);

        var response = await client.PatchAsJsonAsync(
            "/v1/admin/experts/expert-001/specialties",
            new ExpertSpecialtiesUpdateDto(new[] { "medicine", "Nursing", "  pharmacy " }));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);

        using var doc = JsonDocument.Parse(body);
        var specs = doc.RootElement.GetProperty("specialties")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();
        Assert.Contains("medicine", specs);
        Assert.Contains("nursing", specs);
        Assert.Contains("pharmacy", specs);
    }

    [Fact]
    public async Task PatchSpecialties_AcceptsEmptyArrayToClearSpecialties()
    {
        using var client = CreateAdminClient(_factory);

        var response = await client.PatchAsJsonAsync(
            "/v1/admin/experts/expert-001/specialties",
            new ExpertSpecialtiesUpdateDto(Array.Empty<string>()));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PatchSpecialties_Returns404ForUnknownExpert()
    {
        using var client = CreateAdminClient(_factory);

        var response = await client.PatchAsJsonAsync(
            "/v1/admin/experts/does-not-exist/specialties",
            new ExpertSpecialtiesUpdateDto(new[] { "medicine" }));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static HttpClient CreateAdminClient(FirstPartyAuthTestWebApplicationFactory factory)
        => factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");
}
