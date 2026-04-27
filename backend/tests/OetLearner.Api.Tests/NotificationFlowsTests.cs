using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class NotificationFlowsTests
{
    [Fact]
    public async Task ProofTrigger_CreatesInboxItems_AndSupportsReadFlows()
    {
        await using var factory = new TestWebApplicationFactory();
        using var adminClient = await CreateAuthenticatedClientAsync(factory, SeedData.AdminEmail, SeedData.LocalSeedPassword);
        using var learnerClient = await CreateAuthenticatedClientAsync(factory, SeedData.LearnerEmail, SeedData.LocalSeedPassword);

        var proof = await TriggerProofAsync(adminClient, new AdminNotificationProofTriggerRequest(
            "LearnerReviewCompleted",
            SeedData.LearnerEmail,
            new Dictionary<string, string?>
            {
                ["attemptId"] = "wa-001",
                ["subtest"] = "writing",
                ["message"] = "Your expert writing review is ready."
            }));

        Assert.True(proof.ProcessedImmediately);
        Assert.False(string.IsNullOrWhiteSpace(proof.InboxItemId));
        Assert.Equal("LearnerReviewCompleted", proof.EventKey);
        Assert.Equal("learner", proof.AudienceRole);

        var unreadFeed = await learnerClient.GetFromJsonAsync<NotificationFeedResponse>(
            "/v1/notifications?page=1&pageSize=50&unreadOnly=true&category=reviews&channel=in_app",
            JsonSupport.Options);

        Assert.NotNull(unreadFeed);
        Assert.Contains(unreadFeed!.Items, item => item.Id == proof.InboxItemId && !item.IsRead);

        var markReadResponse = await learnerClient.PostAsJsonAsync($"/v1/notifications/{proof.InboxItemId}/read", new { });
        markReadResponse.EnsureSuccessStatusCode();

        var secondProof = await TriggerProofAsync(adminClient, new AdminNotificationProofTriggerRequest(
            "LearnerEvaluationCompleted",
            SeedData.LearnerEmail,
            new Dictionary<string, string?>
            {
                ["attemptId"] = "wa-001",
                ["subtest"] = "writing"
            }));

        Assert.False(string.IsNullOrWhiteSpace(secondProof.InboxItemId));

        var markAllResponse = await learnerClient.PostAsJsonAsync("/v1/notifications/read-all", new { });
        markAllResponse.EnsureSuccessStatusCode();

        var finalFeed = await learnerClient.GetFromJsonAsync<NotificationFeedResponse>(
            "/v1/notifications?page=1&pageSize=50",
            JsonSupport.Options);

        Assert.NotNull(finalFeed);
        Assert.Equal(0, finalFeed!.UnreadCount);
        Assert.All(finalFeed.Items.Where(item => item.Id == proof.InboxItemId || item.Id == secondProof.InboxItemId), item => Assert.True(item.IsRead));

        var deliveries = await adminClient.GetFromJsonAsync<NotificationDeliveryAttemptResponse>(
            "/v1/admin/notifications/deliveries?page=1&pageSize=50",
            JsonSupport.Options);

        Assert.NotNull(deliveries);
        Assert.Contains(deliveries!.Items, item => item.EventId == proof.NotificationEventId && item.Channel == "email");

        var filteredDeliveries = await adminClient.GetFromJsonAsync<NotificationDeliveryAttemptResponse>(
            "/v1/admin/notifications/deliveries?page=1&pageSize=50&channel=email&audienceRole=learner&eventKey=LearnerReviewCompleted",
            JsonSupport.Options);

        Assert.NotNull(filteredDeliveries);
        Assert.Contains(filteredDeliveries!.Items, item => item.EventId == proof.NotificationEventId);
        Assert.All(filteredDeliveries.Items, item =>
        {
            Assert.Equal("email", item.Channel);
            Assert.Equal("learner", item.AudienceRole);
            Assert.Equal("LearnerReviewCompleted", item.EventKey);
        });

        var health = await adminClient.GetFromJsonAsync<AdminNotificationHealthSnapshot>(
            "/v1/admin/notifications/health",
            JsonSupport.Options);

        Assert.NotNull(health);
        Assert.True(health!.UnreadInboxItems >= 0);
    }

    [Fact]
    public async Task FeedEndpoint_RemainsQueryable_WhenSqliteBacksDesktopRuntime()
    {
        var sqlitePath = Path.Combine(Path.GetTempPath(), $"oet-notifications-{Guid.NewGuid():N}.db");

        try
        {
            await using var factory = CreateFactoryWithOverrides(
                _ => { },
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = $"Data Source={sqlitePath}"
                });
            using var adminClient = await CreateAuthenticatedClientAsync(factory, SeedData.AdminEmail, SeedData.LocalSeedPassword);
            using var learnerClient = await CreateAuthenticatedClientAsync(factory, SeedData.LearnerEmail, SeedData.LocalSeedPassword);

            var proof = await TriggerProofAsync(adminClient, new AdminNotificationProofTriggerRequest(
                "LearnerReviewCompleted",
                SeedData.LearnerEmail,
                new Dictionary<string, string?>
                {
                    ["attemptId"] = "wa-001",
                    ["subtest"] = "writing",
                    ["message"] = "SQLite-backed feed should stay queryable."
                }));

            var response = await learnerClient.GetAsync("/v1/notifications?page=1&pageSize=20");
            var responseBody = await response.Content.ReadAsStringAsync();

            response.EnsureSuccessStatusCode();

            var feed = JsonSerializer.Deserialize<NotificationFeedResponse>(responseBody, JsonSupport.Options);
            Assert.NotNull(feed);
            Assert.Contains(feed!.Items, item => item.Id == proof.InboxItemId);
        }
        finally
        {
            foreach (var path in new[] { sqlitePath, $"{sqlitePath}-wal", $"{sqlitePath}-shm" })
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // Windows can briefly retain SQLite file handles after host disposal.
                }
            }
        }
    }

    [Fact]
    public async Task AdminPolicyOverride_DisablesEmail_ButKeepsInAppDelivery()
    {
        await using var factory = new TestWebApplicationFactory();
        using var adminClient = await CreateAuthenticatedClientAsync(factory, SeedData.AdminEmail, SeedData.LocalSeedPassword);
        using var learnerClient = await CreateAuthenticatedClientAsync(factory, SeedData.LearnerEmail, SeedData.LocalSeedPassword);

        var updateResponse = await adminClient.PutAsJsonAsync(
            "/v1/admin/notifications/policies/learner/LearnerReviewCompleted",
            new AdminNotificationPolicyUpdateRequest(null, false, null, null));
        updateResponse.EnsureSuccessStatusCode();

        var policies = await adminClient.GetFromJsonAsync<AdminNotificationPoliciesResponse>(
            "/v1/admin/notifications/policies",
            JsonSupport.Options);

        Assert.NotNull(policies);
        Assert.Contains(policies!.Rows, row => row.AudienceRole == "learner" && row.EventKey == "LearnerReviewCompleted" && !row.EmailEnabled);
        Assert.True(policies.GlobalChannelEnabledByAudience["learner"].InAppEnabled);
        Assert.True(policies.GlobalChannelEnabledByAudience["learner"].PushEnabled);

        var proof = await TriggerProofAsync(adminClient, new AdminNotificationProofTriggerRequest(
            "LearnerReviewCompleted",
            SeedData.LearnerEmail,
            new Dictionary<string, string?>
            {
                ["attemptId"] = "wa-001",
                ["subtest"] = "writing"
            }));

        var feed = await learnerClient.GetFromJsonAsync<NotificationFeedResponse>("/v1/notifications?page=1&pageSize=20", JsonSupport.Options);
        Assert.NotNull(feed);
        Assert.Contains(feed!.Items, item => item.Id == proof.InboxItemId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var emailAttempt = await db.NotificationDeliveryAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(attempt =>
                attempt.NotificationEventId == proof.NotificationEventId
                && attempt.Channel == NotificationChannel.Email);

        Assert.NotNull(emailAttempt);
        Assert.Equal(NotificationDeliveryStatus.Suppressed, emailAttempt!.Status);
        Assert.Equal("email_disabled", emailAttempt.ErrorCode);

        var resetResponse = await adminClient.DeleteAsync("/v1/admin/notifications/policies/learner/LearnerReviewCompleted");
        resetResponse.EnsureSuccessStatusCode();

        var resetPolicy = await resetResponse.Content.ReadFromJsonAsync<AdminNotificationPolicyRow>(JsonSupport.Options);
        Assert.NotNull(resetPolicy);
        Assert.False(resetPolicy!.IsOverride);
        Assert.True(resetPolicy.EmailEnabled);
    }

    [Fact]
    public async Task AdminGlobalChannelSwitches_AreReturnedForEachAudience()
    {
        await using var factory = new TestWebApplicationFactory();
        using var adminClient = await CreateAuthenticatedClientAsync(factory, SeedData.AdminEmail, SeedData.LocalSeedPassword);

        var updateResponse = await adminClient.PutAsJsonAsync(
            "/v1/admin/notifications/policies/admin/__global__",
            new AdminNotificationPolicyUpdateRequest(false, true, false, null));
        updateResponse.EnsureSuccessStatusCode();

        var policies = await adminClient.GetFromJsonAsync<AdminNotificationPoliciesResponse>(
            "/v1/admin/notifications/policies",
            JsonSupport.Options);

        Assert.NotNull(policies);
        Assert.False(policies!.GlobalChannelEnabledByAudience["admin"].InAppEnabled);
        Assert.True(policies.GlobalChannelEnabledByAudience["admin"].EmailEnabled);
        Assert.False(policies.GlobalChannelEnabledByAudience["admin"].PushEnabled);
        Assert.True(policies.GlobalChannelEnabledByAudience["learner"].InAppEnabled);

        var partialUpdateResponse = await adminClient.PutAsJsonAsync(
            "/v1/admin/notifications/policies/admin/__global__",
            new AdminNotificationPolicyUpdateRequest(null, false, null, null));
        partialUpdateResponse.EnsureSuccessStatusCode();

        var updatedPolicies = await adminClient.GetFromJsonAsync<AdminNotificationPoliciesResponse>(
            "/v1/admin/notifications/policies",
            JsonSupport.Options);

        Assert.NotNull(updatedPolicies);
        Assert.False(updatedPolicies!.GlobalChannelEnabledByAudience["admin"].InAppEnabled);
        Assert.False(updatedPolicies.GlobalChannelEnabledByAudience["admin"].EmailEnabled);
        Assert.False(updatedPolicies.GlobalChannelEnabledByAudience["admin"].PushEnabled);
    }

    [Fact]
    public async Task DailyDigestProofDispatch_SendsDigestEmail_AndRecordsDelivery()
    {
        var emailSender = new RecordingEmailSender();
        await using var factory = CreateFactoryWithOverrides(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(emailSender);
        });

        using var adminClient = await CreateAuthenticatedClientAsync(factory, SeedData.AdminEmail, SeedData.LocalSeedPassword);

        var proof = await TriggerProofAsync(adminClient, new AdminNotificationProofTriggerRequest(
            "LearnerStudyPlanDueReminder",
            SeedData.LearnerEmail,
            new Dictionary<string, string?>
            {
                ["itemTitle"] = "Finish the timed writing task",
                ["dueLabel"] = "today at 8:00 PM"
            },
            DispatchDigestImmediately: true));

        Assert.True(proof.DigestDispatchedImmediately);
        Assert.Contains(emailSender.Messages, message => message.To == SeedData.LearnerEmail && message.Subject.Contains("daily digest", StringComparison.OrdinalIgnoreCase));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var emailAttempt = await db.NotificationDeliveryAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(attempt =>
                attempt.NotificationEventId == proof.NotificationEventId
                && attempt.Channel == NotificationChannel.Email
                && attempt.Status == NotificationDeliveryStatus.Sent);

        Assert.NotNull(emailAttempt);
    }

    [Fact]
    public async Task QuietHoursPreference_DefersPushIntoQueuedFanoutJob()
    {
        await using var factory = new TestWebApplicationFactory();
        using var adminClient = await CreateAuthenticatedClientAsync(factory, SeedData.AdminEmail, SeedData.LocalSeedPassword);
        using var learnerClient = await CreateAuthenticatedClientAsync(factory, SeedData.LearnerEmail, SeedData.LocalSeedPassword);

        var nowUtc = DateTimeOffset.UtcNow;
        var quietStart = nowUtc.AddMinutes(-5).ToString("HH:mm");
        var quietEnd = nowUtc.AddMinutes(5).ToString("HH:mm");

        var preferencePatchResponse = await learnerClient.PatchAsJsonAsync("/v1/notifications/preferences", new NotificationPreferencePatchRequest(
            "UTC",
            null,
            null,
            true,
            true,
            quietStart,
            quietEnd,
            null));
        preferencePatchResponse.EnsureSuccessStatusCode();

        var proof = await TriggerProofAsync(adminClient, new AdminNotificationProofTriggerRequest(
            "LearnerReviewCompleted",
            SeedData.LearnerEmail,
            new Dictionary<string, string?>
            {
                ["attemptId"] = "wa-001",
                ["subtest"] = "writing"
            }));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var deferredJob = await db.BackgroundJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(job =>
                job.Type == JobType.NotificationFanout
                && job.ResourceId == proof.NotificationEventId
                && job.State == AsyncState.Queued
                && job.StatusReasonCode == "quiet_hours_deferred");

        Assert.NotNull(deferredJob);
        Assert.True(deferredJob!.AvailableAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ExpiredPushSubscription_IsDisabled_WhenDispatcherReturns410()
    {
        var webPushDispatcher = new FakeWebPushDispatcher
        {
            OnSendAsync = (_, _, _) => throw new PushDispatchException(410, "Subscription is gone."),
        };

        await using var factory = CreateFactoryWithOverrides(
            services =>
            {
                services.RemoveAll<IWebPushDispatcher>();
                services.AddSingleton<IWebPushDispatcher>(webPushDispatcher);
            },
            new Dictionary<string, string?>
            {
                ["WebPush:Enabled"] = "true",
                ["WebPush:Subject"] = "mailto:test@example.test",
                ["WebPush:PublicKey"] = "public-test-key",
                ["WebPush:PrivateKey"] = "private-test-key"
            });

        using var adminClient = await CreateAuthenticatedClientAsync(factory, SeedData.AdminEmail, SeedData.LocalSeedPassword);
        using var learnerClient = await CreateAuthenticatedClientAsync(factory, SeedData.LearnerEmail, SeedData.LocalSeedPassword);

        var createSubscriptionResponse = await learnerClient.PostAsJsonAsync("/v1/notifications/push-subscriptions", new PushSubscriptionPayload(
            "https://push.example.test/subscription/expired",
            "test-p256dh",
            "test-auth",
            null,
            "xunit"));
        createSubscriptionResponse.EnsureSuccessStatusCode();

        var proof = await TriggerProofAsync(adminClient, new AdminNotificationProofTriggerRequest(
            "LearnerReviewCompleted",
            SeedData.LearnerEmail,
            new Dictionary<string, string?>
            {
                ["attemptId"] = "wa-001",
                ["subtest"] = "writing"
            }));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var subscription = await db.PushSubscriptions.AsNoTracking().SingleAsync();
        var pushAttempt = await db.NotificationDeliveryAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(attempt =>
                attempt.NotificationEventId == proof.NotificationEventId
                && attempt.Channel == NotificationChannel.Push);

        Assert.False(subscription.IsActive);
        Assert.NotNull(pushAttempt);
        Assert.Equal(NotificationDeliveryStatus.Expired, pushAttempt!.Status);
        Assert.Equal("subscription_expired", pushAttempt.ErrorCode);
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(WebApplicationFactory<Program> factory, string email, string password)
    {
        var client = factory.CreateClient();
        var session = await SignInAsync(client, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return client;
    }

    private static async Task<AuthSessionResponse> SignInAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/v1/auth/sign-in", new PasswordSignInRequest(email, password, true));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthSessionResponse>(JsonSupport.Options))
            ?? throw new InvalidOperationException("Expected a valid auth session response.");
    }

    private static async Task<AdminNotificationProofTriggerResponse> TriggerProofAsync(HttpClient adminClient, AdminNotificationProofTriggerRequest request)
    {
        var response = await adminClient.PostAsJsonAsync("/v1/admin/notifications/proof/trigger", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AdminNotificationProofTriggerResponse>(JsonSupport.Options))
            ?? throw new InvalidOperationException("Expected a valid notification proof response.");
    }

    private static WebApplicationFactory<Program> CreateFactoryWithOverrides(
        Action<IServiceCollection> configureServices,
        IReadOnlyDictionary<string, string?>? configurationValues = null)
    {
        return new TestWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            if (configurationValues is not null)
            {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configurationValues));
            }

            builder.ConfigureServices(configureServices);
        });
    }
}
