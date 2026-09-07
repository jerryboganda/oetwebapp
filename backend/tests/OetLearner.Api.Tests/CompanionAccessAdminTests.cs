using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

[Collection("AuthFlows")]
public class CompanionAccessAdminTests : IClassFixture<FirstPartyAuthTestWebApplicationFactory>
{
    private readonly FirstPartyAuthTestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CompanionAccessAdminTests(FirstPartyAuthTestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient(SeedData.AdminEmail, SeedData.LocalSeedPassword, expectedRole: "admin");
    }

    [Fact]
    public async Task AccessList_ReportsManifestGrants_ForFullCourses()
    {
        // The test host silences the catalog seeder, so seed the two plans
        // this suite reasons about (mirrors the manifest: full courses grant
        // the companion, crash courses do not).
        await SeedPlansAsync();

        var response = await _client.GetAsync("/v1/admin/companion/access");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();

        // Seeded from the catalog manifest: the 8 full courses grant the
        // companion, nothing else does.
        var nursing = items.Single(x => x.GetProperty("planCode").GetString() == "full-nursing");
        Assert.True(nursing.GetProperty("effective").GetBoolean());
        Assert.Equal("manifest", nursing.GetProperty("source").GetString());

        var crash = items.Single(x => x.GetProperty("planCode").GetString() == "crash-course");
        Assert.False(crash.GetProperty("effective").GetBoolean());
        Assert.Equal("none", crash.GetProperty("source").GetString());
    }

    [Fact]
    public async Task AccessGrantOverride_TogglesEffectiveState_AndResetsToManifest()
    {
        await SeedPlansAsync();
        var planCode = "crash-course";

        // Grant via override.
        var grant = await _client.PostAsJsonAsync("/v1/admin/companion/access", new
        {
            planCode,
            enabled = true
        });
        grant.EnsureSuccessStatusCode();

        var listed = await ReadItemAsync(planCode);
        Assert.True(listed.GetProperty("effective").GetBoolean());
        Assert.Equal("override", listed.GetProperty("source").GetString());

        // Revoke via override (explicit deny beats the manifest).
        var revoke = await _client.PostAsJsonAsync("/v1/admin/companion/access", new
        {
            planCode = "full-nursing",
            enabled = false
        });
        revoke.EnsureSuccessStatusCode();

        var revoked = await ReadItemAsync("full-nursing");
        Assert.False(revoked.GetProperty("effective").GetBoolean());
        Assert.Equal("override", revoked.GetProperty("source").GetString());

        // Reset removes the row: manifest truth returns.
        var reset = await _client.DeleteAsync($"/v1/admin/companion/access/{planCode}");
        reset.EnsureSuccessStatusCode();
        var afterReset = await ReadItemAsync(planCode);
        Assert.False(afterReset.GetProperty("effective").GetBoolean());
        Assert.Equal("none", afterReset.GetProperty("source").GetString());

        var resetNursing = await _client.DeleteAsync("/v1/admin/companion/access/full-nursing");
        resetNursing.EnsureSuccessStatusCode();
        var nursingReset = await ReadItemAsync("full-nursing");
        Assert.True(nursingReset.GetProperty("effective").GetBoolean());
        Assert.Equal("manifest", nursingReset.GetProperty("source").GetString());
    }

    [Fact]
    public async Task AccessGrant_UnknownPlan_ReturnsNotFound()
    {
        var response = await _client.PostAsJsonAsync("/v1/admin/companion/access", new
        {
            planCode = "no-such-plan",
            enabled = true
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AccessReset_WithoutOverride_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync("/v1/admin/companion/access/crash-course-no-row");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("FULL-NURSING", true)]
    [InlineData("full-nursing", true)]
    [InlineData("WRITING-CRASH", false)]
    [InlineData("crash-course", null)]
    public async Task ResolveCompanionPlanOverride_MatchesPlanCodeCaseInsensitively(
        string planCode, bool? expected)
    {
        var dbOptions = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new LearnerDbContext(dbOptions);
        db.PlanModuleOverrides.Add(new PlanModuleOverride
        {
            Id = "pmo-test-1",
            PlanCode = "full-nursing",
            ModuleKey = ModuleKeys.AiCompanion,
            Enabled = true,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.PlanModuleOverrides.Add(new PlanModuleOverride
        {
            Id = "pmo-test-2",
            PlanCode = "full-nursing",
            ModuleKey = ModuleKeys.Recalls,
            Enabled = true,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.PlanModuleOverrides.Add(new PlanModuleOverride
        {
            Id = "pmo-test-3",
            PlanCode = "writing-crash",
            ModuleKey = ModuleKeys.AiCompanion,
            Enabled = false,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        // Casing never matters; other modules' rows for the same plan code
        // must never leak into the companion decision; unknown plans yield
        // no override.
        Assert.Equal(
            expected,
            await CompanionLearnerEndpoints.ResolveCompanionPlanOverrideAsync(db, planCode, default));
    }

    private async Task SeedPlansAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = DateTimeOffset.UtcNow;
        foreach (var (code, name, modules) in new[]
        {
            ("full-nursing", "Full Nursing OET Course", "[\"Listening\",\"AiCompanion\"]"),
            ("crash-course", "Full Crash Course - General OET", "[\"Listening\"]"),
        })
        {
            if (!await db.BillingPlans.AnyAsync(p => p.Code == code))
            {
                db.BillingPlans.Add(new BillingPlan
                {
                    Id = $"plan-companion-{code}",
                    Code = code,
                    Name = name,
                    Price = 60m,
                    Currency = "GBP",
                    Interval = "one_time",
                    Status = BillingPlanStatus.Active,
                    DisplayOrder = 1,
                    DashboardModulesJson = modules,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task<JsonElement> ReadItemAsync(string planCode)
    {
        var response = await _client.GetAsync("/v1/admin/companion/access");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("planCode").GetString() == planCode)
            .Clone();
    }
}
