using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Billing;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.FreeSamples;

namespace OetLearner.Api.Tests.FreeSamples;

/// <summary>
/// Free Mocks — starting a single-card Speaking attempt. The learner's ONE free
/// AI-graded sample is the profession's designated card: it starts for a learner
/// who has no credits left and whose ACCOUNT profession is different (or unset),
/// binds the once-only claim, and changes nothing else — every other card, a
/// spent sample, and the dark-launch-off state keep the profession-isolation and
/// credit gates exactly as before.
/// </summary>
public sealed class FreeSampleSpeakingAttemptTests : IAsyncLifetime
{
    private LearnerDbContext _db = default!;
    private LearnerService _learnerService = default!;
    private AiPackageCreditService _credit = default!;
    private string _storageRoot = default!;

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"free-sample-speaking-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new LearnerDbContext(options);

        var billingOptions = Options.Create(new BillingOptions());
        var platformLinks = new PlatformLinkService(
            TestRuntimeSettingsProvider.FromPlatformOptions(new PlatformOptions { FallbackEmailDomain = "example.test" }),
            billingOptions);

        _storageRoot = Path.Combine(Path.GetTempPath(), $"oet-free-sample-speaking-tests-{Guid.NewGuid():N}");
        var storageOptions = Options.Create(new StorageOptions { LocalRootPath = _storageRoot });
        var fileStorage = new LocalFileStorage(new TestHostEnvironment(_storageRoot), storageOptions);
        var stripe = new OetLearner.Api.Services.StripeGateway(
            new HttpClient(),
            billingOptions,
            TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OetLearner.Api.Services.StripeGateway>.Instance);
        var paypal = new OetLearner.Api.Services.PayPalGateway(new HttpClient(), billingOptions);
        var paymentGateways = new OetLearner.Api.Services.PaymentGatewayService(
            stripe,
            paypal,
            new OetLearner.Api.Services.Billing.Gateways.PayTabsGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value)),
            new OetLearner.Api.Services.Billing.Gateways.PaymobGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value)),
            new OetLearner.Api.Services.Billing.Gateways.CheckoutComGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value)),
            new OetLearner.Api.Services.Billing.Gateways.EasyKashGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value)),
            new OetLearner.Api.Services.Billing.Gateways.WhopGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value)),
            new OetLearner.Api.Services.Billing.Gateways.FawaterakGateway(new HttpClient(), billingOptions, TestRuntimeSettingsProvider.FromBillingOptions(billingOptions.Value)));
        var walletService = new WalletService(_db, paymentGateways, platformLinks, billingOptions);

        _credit = new AiPackageCreditService(
            _db, Microsoft.Extensions.Logging.Abstractions.NullLogger<AiPackageCreditService>.Instance);
        _learnerService = new LearnerService(
            _db, fileStorage, new NoOpPdfTextExtractor(), platformLinks,
            notifications: null!, walletService, paymentGateways,
            disputeService: null!, billingOptions, storageOptions,
            aiPackageCreditService: _credit);

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        try
        {
            if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
        }
        catch { /* best-effort */ }
        return Task.CompletedTask;
    }

    private Task<object> StartAsync(string userId, string cardId)
        => _learnerService.CreateSpeakingAttemptAsync(
            userId,
            new CreateAttemptRequest(cardId, Context: null, Mode: "self", DeviceType: null, ParentAttemptId: null),
            CancellationToken.None);

    private static string AttemptIdOf(object result)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result));
        return doc.RootElement.GetProperty("attemptId").GetString()!;
    }

    private async Task<string> SeedLearnerAsync(string? activeProfessionId, bool exhaustedCredits = true)
    {
        var userId = $"learner-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        _db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = "Test Learner",
            Email = $"{userId}@example.test",
            ActiveProfessionId = activeProfessionId,
            AccountStatus = "active",
            CreatedAt = now,
            LastActiveAt = now,
        });
        await _db.SaveChangesAsync();

        if (exhaustedCredits)
        {
            // A learner who once bought ONE Speaking card (2 credits) and spent it: NOT a
            // never-purchased legacy account, so the credit pre-check really refuses.
            await _credit.GrantPackageAsync(
                userId,
                new BillingAddOn
                {
                    Id = $"addon_{userId}",
                    Code = "pkg_speaking_single",
                    Name = "Speaking single",
                    Price = 1m,
                    Currency = "GBP",
                    Interval = "one_time",
                    Status = BillingAddOnStatus.Active,
                    DurationDays = 30,
                    GrantCredits = 1,
                    GrantEntitlementsJson = """{"package_type":"speaking","speaking_only_credits":2}""",
                    AddonKind = "ai_package",
                    AppliesToAllPlans = true,
                    IsStackable = true,
                    QuantityStep = 1,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                1, $"cs-{userId}", null, CancellationToken.None);
            var spent = await _credit.DeductGradingCreditAsync(
                userId, "speaking", $"spent:{userId}", AiGradingCreditCost.SpeakingCard, CancellationToken.None);
            Assert.True(spent.Debited);
        }
        return userId;
    }

    [Fact]
    public async Task TheDesignatedCard_StartsFree_ForAnExhaustedLearnerOnAnotherProfession_AndBindsTheClaim()
    {
        await FreeSampleServiceTests.EnableAsync(_db);
        var (_, medCard) = await FreeSampleServiceTests.SeedCardAsync(_db, "medicine", cardNumber: 1);
        var learner = await SeedLearnerAsync(activeProfessionId: "nursing");

        var result = await StartAsync(learner, medCard);

        var attemptId = AttemptIdOf(result);
        var claim = await _db.FreeSampleClaims.SingleAsync(c => c.UserId == learner);
        Assert.Equal("speaking", claim.Subtest);
        Assert.Equal("medicine", claim.Profession);
        Assert.Equal(medCard, claim.ContentId);
        Assert.Equal(attemptId, claim.AttemptId);
        Assert.Contains(ContentEntitlementService.FreeSampleFeedback, JsonSerializer.Serialize(result));
        // The learner's ACCOUNT profession is never touched.
        Assert.Equal("nursing", (await _db.Users.SingleAsync(u => u.Id == learner)).ActiveProfessionId);
    }

    [Fact]
    public async Task TheDesignatedCard_AlsoWorksForALearnerWithNoAccountProfession()
    {
        await FreeSampleServiceTests.EnableAsync(_db);
        var (_, medCard) = await FreeSampleServiceTests.SeedCardAsync(_db, "medicine", cardNumber: 1);
        var learner = await SeedLearnerAsync(activeProfessionId: null);

        var attemptId = AttemptIdOf(await StartAsync(learner, medCard));

        Assert.True(await new FreeSampleService(_db).IsFreeAttemptAsync(learner, "speaking", attemptId, default));
    }

    [Fact]
    public async Task AnyOtherCard_KeepsTheProfessionAndCreditGates()
    {
        await FreeSampleServiceTests.EnableAsync(_db);
        await FreeSampleServiceTests.SeedCardAsync(_db, "medicine", cardNumber: 1);              // the designated one
        var (_, otherMed) = await FreeSampleServiceTests.SeedCardAsync(_db, "medicine", cardNumber: 2);
        var medLearner = await SeedLearnerAsync(activeProfessionId: "medicine");
        var nurseLearner = await SeedLearnerAsync(activeProfessionId: "nursing");

        var noCredits = await Assert.ThrowsAsync<ApiException>(() => StartAsync(medLearner, otherMed));
        Assert.Equal("no_ai_package_credits", noCredits.ErrorCode);
        var wrongProfession = await Assert.ThrowsAsync<ApiException>(() => StartAsync(nurseLearner, otherMed));
        Assert.Equal("content_not_found", wrongProfession.ErrorCode);
        Assert.Empty(_db.FreeSampleClaims);
    }

    [Fact]
    public async Task WithoutTheFlag_TheDesignatedCardIsAnOrdinaryPaidCard()
    {
        var (_, medCard) = await FreeSampleServiceTests.SeedCardAsync(_db, "medicine", cardNumber: 1);
        var learner = await SeedLearnerAsync(activeProfessionId: "medicine");

        var ex = await Assert.ThrowsAsync<ApiException>(() => StartAsync(learner, medCard));

        Assert.Equal("no_ai_package_credits", ex.ErrorCode);
        Assert.Empty(_db.FreeSampleClaims);
    }

    [Fact]
    public async Task ASpentSample_IsNotFreeAgain()
    {
        await FreeSampleServiceTests.EnableAsync(_db);
        var (_, medCard) = await FreeSampleServiceTests.SeedCardAsync(_db, "medicine", cardNumber: 1);
        var learner = await SeedLearnerAsync(activeProfessionId: "medicine");

        var attemptId = AttemptIdOf(await StartAsync(learner, medCard));
        (await _db.Attempts.SingleAsync(a => a.Id == attemptId)).State = AttemptState.Completed;
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApiException>(() => StartAsync(learner, medCard));
        Assert.Equal("no_ai_package_credits", ex.ErrorCode);
    }

    [Fact]
    public async Task ReopeningTheCardWhileTheFreeAttemptIsInProgress_ResumesIt()
    {
        await FreeSampleServiceTests.EnableAsync(_db);
        var (_, medCard) = await FreeSampleServiceTests.SeedCardAsync(_db, "medicine", cardNumber: 1);
        var learner = await SeedLearnerAsync(activeProfessionId: "nursing");

        var first = AttemptIdOf(await StartAsync(learner, medCard));
        var again = AttemptIdOf(await StartAsync(learner, medCard));

        Assert.Equal(first, again);
        Assert.Single(_db.FreeSampleClaims);
    }

    private sealed class TestHostEnvironment(string contentRootPath)
        : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = contentRootPath;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
