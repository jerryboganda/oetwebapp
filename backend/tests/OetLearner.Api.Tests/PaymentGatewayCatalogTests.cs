using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Tests;

public class PaymentGatewayCatalogTests
{
    [Fact]
    public async Task EnsureSeeded_EnablesWhopThenFawaterak_AndDisablesLegacyGateways()
    {
        await using var db = CreateDb();
        var catalog = CreateCatalog(db, sandbox: true);

        var rows = await catalog.EnsureSeededAsync(default);

        Assert.Equal("whop", rows[0].Name);
        Assert.True(rows[0].IsEnabled);
        Assert.True(rows[0].IsPrimary);
        Assert.Equal("fawaterak", rows[1].Name);
        Assert.True(rows[1].IsEnabled);
        Assert.All(rows.Where(r => r.Name is not "whop" and not "fawaterak"), row => Assert.False(row.IsEnabled));
    }

    [Fact]
    public async Task ListLearnerMethods_Sandbox_ReturnsWhopThenFawaterak()
    {
        await using var db = CreateDb();
        var catalog = CreateCatalog(db, sandbox: true);

        var methods = await catalog.ListLearnerMethodsAsync(PaymentGatewayRegions.Global, default);

        Assert.Equal(["whop", "fawaterak"], methods.Select(m => m.Name).ToArray());
        Assert.Equal("MAIN", methods[0].Badge);
        Assert.Equal(PaymentGatewayModes.Embedded, methods[0].Mode);
        Assert.Equal(PaymentGatewayModes.Iframe, methods[1].Mode);
    }

    [Fact]
    public async Task Update_CanDisableWhopWithoutDeletingTheRow()
    {
        await using var db = CreateDb();
        var catalog = CreateCatalog(db, sandbox: true);
        await catalog.EnsureSeededAsync(default);

        var updated = await catalog.UpdateAsync("whop", new AdminPaymentGatewayUpdateRequest(
            IsEnabled: false,
            IsPrimary: null,
            DisplayOrder: null,
            ApiKey: null,
            HashApiKey: null,
            ProviderKey: null,
            WebhookSecret: null,
            CompanyId: null), "admin-1", default);

        Assert.False(updated.IsEnabled);
        Assert.Equal(1, await db.PaymentGatewayToggles.CountAsync(r => r.Name == "whop"));
        var methods = await catalog.ListLearnerMethodsAsync(PaymentGatewayRegions.Global, default);
        Assert.DoesNotContain(methods, m => m.Name == "whop");
        Assert.Contains(methods, m => m.Name == "fawaterak");
    }

    [Fact]
    public async Task IsEnabled_FalseForDisabledStripe()
    {
        await using var db = CreateDb();
        var catalog = CreateCatalog(db, sandbox: true);
        await catalog.EnsureSeededAsync(default);

        Assert.False(await catalog.IsEnabledAsync("stripe", default));
        Assert.True(await catalog.IsEnabledAsync("whop", default));
    }

    private static LearnerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static PaymentGatewayCatalog CreateCatalog(LearnerDbContext db, bool sandbox)
        => new(
            db,
            TestRuntimeSettingsProvider.FromBillingOptions(new BillingOptions { AllowSandboxFallbacks = sandbox }),
            Options.Create(new BillingOptions { AllowSandboxFallbacks = sandbox }));
}
