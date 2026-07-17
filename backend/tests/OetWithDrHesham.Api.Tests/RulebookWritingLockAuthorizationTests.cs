using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OetWithDrHesham.Api.Services;
using OetWithDrHesham.Api.Tests.Infrastructure;

namespace OetWithDrHesham.Api.Tests;

/// <summary>
/// The Writing rulebook is an internal authoring/AI-grading asset and must NOT
/// be readable by learners (it is hidden from the learner UI too). These tests
/// lock the contract: learners are forbidden from the Writing rulebook GETs,
/// teaching staff (Expert/Admin) keep access, and the learner-facing
/// <c>/v1/writing/lint</c> live checker is unaffected.
/// </summary>
[Collection("AuthFlows")]
public class RulebookWritingLockAuthorizationTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;

    public RulebookWritingLockAuthorizationTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Learner_CannotReadWritingRulebook()
    {
        using var learner = _factory.CreateAuthenticatedClient(SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");

        var response = await learner.GetAsync("/v1/rulebooks/writing/medicine");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Learner_CannotReadWritingRule()
    {
        using var learner = _factory.CreateAuthenticatedClient(SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");

        var response = await learner.GetAsync("/v1/rulebooks/writing/medicine/rule/R01.1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanReadWritingRulebook()
    {
        using var admin = _factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");

        var response = await admin.GetAsync("/v1/rulebooks/writing/medicine");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Expert_CanReadWritingRulebook()
    {
        using var expert = _factory.CreateAuthenticatedClient(SeedData.ExpertEmail, SeedData.LocalSeedPassword, expectedRole: "expert");

        var response = await expert.GetAsync("/v1/rulebooks/writing/medicine");

        response.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("occupational-therapy")]
    [InlineData("speech-pathology")]
    [InlineData("other-allied-health")]
    public async Task Admin_ReadWritingRulebook_AcceptsKebabProfessionSlugs(string profession)
    {
        // Kebab-case profession slugs must still resolve on the (now staff-only)
        // Writing rulebook endpoint. Relocated here from LearnerSpecRegressionTests
        // because reading the rulebook now requires a teaching-staff client.
        using var admin = _factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");

        var response = await admin.GetAsync($"/v1/rulebooks/writing/{profession}");

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("writing", json.RootElement.GetProperty("kind").GetString(), ignoreCase: true);
    }

    [Fact]
    public async Task Learner_CanStillUseWritingLint()
    {
        using var learner = _factory.CreateAuthenticatedClient(SeedData.LearnerEmail, SeedData.LocalSeedPassword, expectedRole: "learner");

        // The live checker stays learner-accessible (it returns rule findings,
        // never the rulebook itself). A minimal body with no attempt/content id
        // lints the supplied letter text directly.
        var response = await learner.PostAsJsonAsync("/v1/writing/lint", new
        {
            letterText = "Dear Dr Smith,\nRe: Ms A\n\nI am writing to refer Ms A for your assessment.\n\nYours sincerely,\nDoctor",
            letterType = "routine_referral",
            profession = "medicine",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
