using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class FreezeWorkflowTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FreezeWorkflowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task LearnerConfirm_DoesNotBypassAdminApproval()
    {
        var userId = $"freeze-pending-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);
        await ConfigureFreezePolicyAsync(approvalMode: FreezeApprovalMode.AdminApprovalRequired);

        var requestResponse = await client.PostAsJsonAsync("/v1/freeze/request", new
        {
            reason = "Travel pause",
            pauseEntitlementClock = true
        });
        requestResponse.EnsureSuccessStatusCode();
        var freezeId = await ReadCurrentFreezeIdAsync(requestResponse);

        var confirmResponse = await client.PostAsync($"/v1/freeze/{Uri.EscapeDataString(freezeId)}/confirm", null);

        Assert.Equal(HttpStatusCode.Conflict, confirmResponse.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var record = await db.AccountFreezeRecords.AsNoTracking().FirstAsync(x => x.Id == freezeId);
        Assert.Equal(FreezeStatus.PendingApproval, record.Status);
        Assert.False(record.EntitlementConsumed);
    }

    [Fact]
    public async Task LearnerCancel_ReleasesUnusedSelfServiceEntitlement()
    {
        var userId = $"freeze-cancel-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);
        await ConfigureFreezePolicyAsync(approvalMode: FreezeApprovalMode.AdminApprovalRequired);

        var requestResponse = await client.PostAsJsonAsync("/v1/freeze/request", new
        {
            reason = "Temporary pause",
            pauseEntitlementClock = true
        });
        requestResponse.EnsureSuccessStatusCode();
        var freezeId = await ReadCurrentFreezeIdAsync(requestResponse);

        var cancelResponse = await client.PostAsync($"/v1/freeze/{Uri.EscapeDataString(freezeId)}/cancel", null);

        cancelResponse.EnsureSuccessStatusCode();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var record = await db.AccountFreezeRecords.AsNoTracking().FirstAsync(x => x.Id == freezeId);
        var entitlement = await db.AccountFreezeEntitlements.AsNoTracking().FirstAsync(x => x.UserId == userId);
        Assert.Equal(FreezeStatus.Cancelled, record.Status);
        Assert.False(record.EntitlementConsumed);
        Assert.Null(entitlement.ConsumedAt);
        Assert.NotNull(entitlement.ResetAt);
    }

    [Fact]
    public async Task ScheduledFreeze_DoesNotBlockLearnerMutationsBeforeStart()
    {
        var userId = $"freeze-scheduled-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.AccountFreezeRecords.Add(new AccountFreezeRecord
            {
                Id = $"freeze-{Guid.NewGuid():N}",
                UserId = userId,
                RequestedByLearnerId = userId,
                Status = FreezeStatus.Scheduled,
                IsCurrent = true,
                IsSelfService = true,
                RequestedAt = now,
                ScheduledStartAt = now.AddDays(3),
                EndedAt = now.AddDays(10),
                DurationDays = 7,
                Reason = "Future pause",
                PolicySnapshotJson = "{}",
                EligibilitySnapshotJson = "{}",
                PolicyVersionSnapshot = 1,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync("/v1/learner/onboarding/start", null);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task FreezeRequest_RejectsFutureStartWhenSchedulingDisabled()
    {
        var userId = $"freeze-scheduling-{Guid.NewGuid():N}";
        using var client = await CreateClientForUserAsync(userId);
        await ConfigureFreezePolicyAsync(allowScheduling: false);

        var response = await client.PostAsJsonAsync("/v1/freeze/request", new
        {
            startAt = DateTimeOffset.UtcNow.AddDays(3),
            endAt = DateTimeOffset.UtcNow.AddDays(8),
            reason = "Future pause",
            pauseEntitlementClock = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdminManualFreeze_ReturnsValidationInsteadOfServerErrorForMissingSubscription()
    {
        var userId = $"freeze-nosub-{Guid.NewGuid():N}";
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var subscriptions = await db.Subscriptions.Where(x => x.UserId == userId).ToListAsync();
            db.Subscriptions.RemoveRange(subscriptions);
            await db.SaveChangesAsync();
        }

        using var client = CreateAdminClient(AdminPermissions.BillingWrite);
        var response = await client.PostAsJsonAsync("/v1/admin/freeze/manual", new
        {
            userId,
            reason = "Staff pause",
            internalNotes = "Missing subscription validation test",
            pauseEntitlementClock = true,
            overrideEligibility = false
        });

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("freeze_ineligible", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminFreezeOverview_UsesLifecyclePermissions()
    {
        using var billingClient = CreateAdminClient(AdminPermissions.BillingRead);
        var allowedResponse = await billingClient.GetAsync("/v1/admin/freeze/overview");
        allowedResponse.EnsureSuccessStatusCode();

        using var contentClient = CreateAdminClient(AdminPermissions.ContentPublish);
        var forbiddenResponse = await contentClient.GetAsync("/v1/admin/freeze/overview");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
    }

    private async Task<HttpClient> CreateClientForUserAsync(string userId)
    {
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }

    private HttpClient CreateAdminClient(params string[] permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
        client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", string.Join(',', permissions));
        return client;
    }

    private async Task ConfigureFreezePolicyAsync(
        FreezeApprovalMode approvalMode = FreezeApprovalMode.AutoApprove,
        bool allowScheduling = true)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        var nextVersion = (await db.AccountFreezePolicies.Select(x => (int?)x.Version).MaxAsync() ?? 0) + 1;
        var policy = await db.AccountFreezePolicies.FirstOrDefaultAsync(x => x.Id == "global");
        if (policy is null)
        {
            db.AccountFreezePolicies.Add(new AccountFreezePolicy
            {
                Id = "global",
                IsEnabled = true,
                SelfServiceEnabled = true,
                ApprovalMode = approvalMode,
                MinDurationDays = 1,
                MaxDurationDays = 30,
                AllowScheduling = allowScheduling,
                AccessMode = FreezeAccessMode.ReadOnly,
                EntitlementPauseMode = FreezeEntitlementPauseMode.InternalClock,
                RequireReason = true,
                RequireInternalNotes = false,
                AllowActivePaid = true,
                AllowGracePeriod = true,
                EligibilityReasonCodesJson = "[]",
                UpdatedAt = now,
                Version = nextVersion
            });
        }
        else
        {
            policy.IsEnabled = true;
            policy.SelfServiceEnabled = true;
            policy.ApprovalMode = approvalMode;
            policy.MinDurationDays = 1;
            policy.MaxDurationDays = 30;
            policy.AllowScheduling = allowScheduling;
            policy.RequireReason = true;
            policy.RequireInternalNotes = false;
            policy.AllowActivePaid = true;
            policy.AllowGracePeriod = true;
            policy.EligibilityReasonCodesJson = "[]";
            policy.UpdatedAt = now;
            policy.Version = nextVersion;
        }

        await db.SaveChangesAsync();
    }

    private static async Task<string> ReadCurrentFreezeIdAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("currentFreeze").GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Expected current freeze id in response.");
    }
}