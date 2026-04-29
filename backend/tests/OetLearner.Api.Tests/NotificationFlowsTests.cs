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
            "/v1/admin/notifications/policies/Learner/LearnerReviewCompleted",
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

        var resetResponse = await adminClient.DeleteAsync("/v1/admin/notifications/policies/LEARNER/LearnerReviewCompleted");
        resetResponse.EnsureSuccessStatusCode();

        var resetPolicy = await resetResponse.Content.ReadFromJsonAsync<AdminNotificationPolicyRow>(JsonSupport.Options);
        Assert.NotNull(resetPolicy);
        Assert.Equal("learner", resetPolicy!.AudienceRole);
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
    public async Task FrequencyCap_SuppressesRepeatedNonProtectedEmailDelivery()
    {
        var emailSender = new RecordingEmailSender();
        await using var factory = CreateFactoryWithOverrides(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(emailSender);
        });

        using var adminClient = await CreateAuthenticatedClientAsync(factory, SeedData.AdminEmail, SeedData.LocalSeedPassword);

        var updateResponse = await adminClient.PutAsJsonAsync(
            "/v1/admin/notifications/policies/learner/LearnerReviewCompleted",
            new AdminNotificationPolicyUpdateRequest(
                null,
                true,
                false,
                "immediate",
                MaxDeliveriesPerHour: 1,
                MaxDeliveriesPerDay: 1));
        updateResponse.EnsureSuccessStatusCode();

        var policy = await updateResponse.Content.ReadFromJsonAsync<AdminNotificationPolicyRow>(JsonSupport.Options);
        Assert.NotNull(policy);
        Assert.Equal(1, policy!.MaxDeliveriesPerHour);
        Assert.Equal(1, policy.MaxDeliveriesPerDay);

        var firstProof = await TriggerProofAsync(adminClient, new AdminNotificationProofTriggerRequest(
            "LearnerReviewCompleted",
            SeedData.LearnerEmail,
            new Dictionary<string, string?>
            {
                ["attemptId"] = "wa-cap-001",
                ["subtest"] = "writing"
            },
            EntityId: "frequency-cap-one"));

        var secondProof = await TriggerProofAsync(adminClient, new AdminNotificationProofTriggerRequest(
            "LearnerReviewCompleted",
            SeedData.LearnerEmail,
            new Dictionary<string, string?>
            {
                ["attemptId"] = "wa-cap-002",
                ["subtest"] = "writing"
            },
            EntityId: "frequency-cap-two"));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var firstEmailAttempt = await db.NotificationDeliveryAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(attempt =>
                attempt.NotificationEventId == firstProof.NotificationEventId
                && attempt.Channel == NotificationChannel.Email);
        var secondEmailAttempt = await db.NotificationDeliveryAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(attempt =>
                attempt.NotificationEventId == secondProof.NotificationEventId
                && attempt.Channel == NotificationChannel.Email);

        Assert.NotNull(firstEmailAttempt);
        Assert.Equal(NotificationDeliveryStatus.Sent, firstEmailAttempt!.Status);
        Assert.NotNull(secondEmailAttempt);
        Assert.Equal(NotificationDeliveryStatus.Suppressed, secondEmailAttempt!.Status);
        Assert.Equal("frequency_cap_exceeded", secondEmailAttempt.ErrorCode);
        Assert.Single(emailSender.Messages, message => message.To == SeedData.LearnerEmail && message.Subject.Contains("review", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AdminPolicyFrequencyCaps_CanBeCleared_AndContinueInheritingGlobalCaps()
    {
        await using var factory = new TestWebApplicationFactory();
        using var adminClient = await CreateAuthenticatedClientAsync(factory, SeedData.AdminEmail, SeedData.LocalSeedPassword);

        var globalCapResponse = await adminClient.PutAsJsonAsync(
            "/v1/admin/notifications/policies/learner/__global__",
            new AdminNotificationPolicyUpdateRequest(
                null,
                null,
                null,
                null,
                MaxDeliveriesPerHour: 1,
                MaxDeliveriesPerDay: 1));
        globalCapResponse.EnsureSuccessStatusCode();

        var eventCapResponse = await adminClient.PutAsJsonAsync(
            "/v1/admin/notifications/policies/learner/LearnerReviewCompleted",
            new AdminNotificationPolicyUpdateRequest(
                null,
                true,
                null,
                "immediate",
                MaxDeliveriesPerHour: 2,
                MaxDeliveriesPerDay: 2));
        eventCapResponse.EnsureSuccessStatusCode();

        var eventCap = await eventCapResponse.Content.ReadFromJsonAsync<AdminNotificationPolicyRow>(JsonSupport.Options);
        Assert.NotNull(eventCap);
        Assert.Equal(2, eventCap!.MaxDeliveriesPerHour);
        Assert.Equal(2, eventCap.MaxDeliveriesPerDay);

        var clearEventCapResponse = await adminClient.PutAsJsonAsync(
            "/v1/admin/notifications/policies/learner/LearnerReviewCompleted",
            new AdminNotificationPolicyUpdateRequest(
                null,
                null,
                null,
                null,
                ClearMaxDeliveriesPerHour: true,
                ClearMaxDeliveriesPerDay: true));
        clearEventCapResponse.EnsureSuccessStatusCode();

        var clearedEventCap = await clearEventCapResponse.Content.ReadFromJsonAsync<AdminNotificationPolicyRow>(JsonSupport.Options);
        Assert.NotNull(clearedEventCap);
        Assert.Equal(1, clearedEventCap!.MaxDeliveriesPerHour);
        Assert.Equal(1, clearedEventCap.MaxDeliveriesPerDay);

        var channelOnlyResponse = await adminClient.PutAsJsonAsync(
            "/v1/admin/notifications/policies/learner/LearnerReviewCompleted",
            new AdminNotificationPolicyUpdateRequest(null, false, null, null));
        channelOnlyResponse.EnsureSuccessStatusCode();

        var updatedGlobalCapResponse = await adminClient.PutAsJsonAsync(
            "/v1/admin/notifications/policies/learner/__global__",
            new AdminNotificationPolicyUpdateRequest(
                null,
                null,
                null,
                null,
                MaxDeliveriesPerHour: 3,
                MaxDeliveriesPerDay: 3));
        updatedGlobalCapResponse.EnsureSuccessStatusCode();

        var policies = await adminClient.GetFromJsonAsync<AdminNotificationPoliciesResponse>(
            "/v1/admin/notifications/policies",
            JsonSupport.Options);

        Assert.NotNull(policies);
        var reviewPolicy = Assert.Single(policies!.Rows, row => row.AudienceRole == "learner" && row.EventKey == "LearnerReviewCompleted");
        Assert.False(reviewPolicy.EmailEnabled);
        Assert.Equal(3, reviewPolicy.MaxDeliveriesPerHour);
        Assert.Equal(3, reviewPolicy.MaxDeliveriesPerDay);
    }

    [Fact]
    public async Task DailyDigestFrequencyCap_SuppressesOverflowWithinSameDigestBatch()
    {
        var emailSender = new RecordingEmailSender();
        await using var factory = CreateFactoryWithOverrides(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(emailSender);
        });

        using var adminClient = await CreateAuthenticatedClientAsync(factory, SeedData.AdminEmail, SeedData.LocalSeedPassword);

        var updateResponse = await adminClient.PutAsJsonAsync(
            "/v1/admin/notifications/policies/learner/LearnerStudyPlanDueReminder",
            new AdminNotificationPolicyUpdateRequest(
                null,
                true,
                false,
                "daily_digest",
                MaxDeliveriesPerHour: 1,
                MaxDeliveriesPerDay: 1));
        updateResponse.EnsureSuccessStatusCode();

        var firstProof = await TriggerProofAsync(adminClient, new AdminNotificationProofTriggerRequest(
            "LearnerStudyPlanDueReminder",
            SeedData.LearnerEmail,
            new Dictionary<string, string?>
            {
                ["itemTitle"] = "First timed writing task",
                ["dueLabel"] = "today at 8:00 PM"
            },
            EntityId: "digest-cap-one"));

        var secondProof = await TriggerProofAsync(adminClient, new AdminNotificationProofTriggerRequest(
            "LearnerStudyPlanDueReminder",
            SeedData.LearnerEmail,
            new Dictionary<string, string?>
            {
                ["itemTitle"] = "Second timed reading task",
                ["dueLabel"] = "today at 8:30 PM"
            },
            EntityId: "digest-cap-two",
            DispatchDigestImmediately: true));

        Assert.True(secondProof.DigestDispatchedImmediately);
        var digestMessage = Assert.Single(emailSender.Messages);
        Assert.Contains("First timed writing task", digestMessage.TextBody);
        Assert.DoesNotContain("Second timed reading task", digestMessage.TextBody);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var attempts = await db.NotificationDeliveryAttempts
            .AsNoTracking()
            .Where(attempt =>
                (attempt.NotificationEventId == firstProof.NotificationEventId || attempt.NotificationEventId == secondProof.NotificationEventId)
                && attempt.Channel == NotificationChannel.Email)
            .ToListAsync();

        var sentAttempt = Assert.Single(attempts, attempt => attempt.Status == NotificationDeliveryStatus.Sent);
        var suppressedAttempt = Assert.Single(attempts, attempt => attempt.Status == NotificationDeliveryStatus.Suppressed);
        Assert.Equal(firstProof.NotificationEventId, sentAttempt.NotificationEventId);
        Assert.Equal(secondProof.NotificationEventId, suppressedAttempt.NotificationEventId);
        Assert.Equal("frequency_cap_exceeded", suppressedAttempt.ErrorCode);
    }

    [Fact]
    public async Task AdminCatalog_CoversExpandedOetLifecycleEvents()
    {
        await using var factory = new TestWebApplicationFactory();
        using var adminClient = await CreateAuthenticatedClientAsync(factory, SeedData.AdminEmail, SeedData.LocalSeedPassword);

        var catalog = await adminClient.GetFromJsonAsync<IReadOnlyList<AdminNotificationCatalogEntry>>(
            "/v1/admin/notifications/catalog",
            JsonSupport.Options);

        Assert.NotNull(catalog);
        var eventKeys = catalog!.Select(entry => entry.EventKey).ToHashSet(StringComparer.Ordinal);
        Assert.All(Enum.GetNames<NotificationEventKey>(), eventKey => Assert.Contains(eventKey, eventKeys));

        Assert.Contains(catalog, entry => entry.EventKey == "LearnerMockReminder24h" && entry.Category == "mocks" && entry.DefaultPushEnabled);
        Assert.Contains(catalog, entry => entry.EventKey == "LearnerWritingFeedbackReady" && entry.Category == "writing" && entry.DefaultEmailEnabled);
        Assert.Contains(catalog, entry => entry.EventKey == "LearnerTrialConversionNudge" && entry.Category == "marketing" && entry.DefaultEmailMode == "daily_digest");
        Assert.Contains(catalog, entry => entry.EventKey == "AdminSuspiciousActivityAlert" && entry.Category == "security" && entry.DefaultSeverity == "critical" && entry.IsPolicyProtected);
        Assert.Contains(catalog, entry => entry.EventKey == "LearnerEmailVerificationRequested" && entry.IsPolicyProtected);
        Assert.Contains(catalog, entry => entry.EventKey == "LearnerInvoiceGenerated" && entry.IsPolicyProtected);
        Assert.Contains(catalog, entry => entry.EventKey == "LearnerPaymentSucceeded" && entry.IsPolicyProtected);

        Assert.Equal("Mock Reminder 24h", NotificationCatalog.BuildTitle(NotificationEventKey.LearnerMockReminder24h, new Dictionary<string, string?>()));
        Assert.Equal("/mocks", NotificationCatalog.BuildActionUrl(NotificationEventKey.LearnerMockReminder24h, new Dictionary<string, string?>()));
    }

    [Fact]
    public async Task ProtectedCriticalPolicies_CannotBeDisabled_ByAdminOrPreferenceOptOut()
    {
        await using var factory = new TestWebApplicationFactory();
        using var adminClient = await CreateAuthenticatedClientAsync(factory, SeedData.AdminEmail, SeedData.LocalSeedPassword);
        using var learnerClient = await CreateAuthenticatedClientAsync(factory, SeedData.LearnerEmail, SeedData.LocalSeedPassword);

        var invalidEventUpdate = await adminClient.PutAsJsonAsync(
            "/v1/admin/notifications/policies/learner/999",
            new AdminNotificationPolicyUpdateRequest(false, false, null, "off"));

        Assert.Equal(400, (int)invalidEventUpdate.StatusCode);

        foreach (var protectedEventKey in new[] { "LearnerEmailVerificationRequested", "LearnerInvoiceGenerated", "LearnerPaymentSucceeded" })
        {
            var rejectedUpdate = await adminClient.PutAsJsonAsync(
                $"/v1/admin/notifications/policies/learner/{protectedEventKey}",
                new AdminNotificationPolicyUpdateRequest(false, false, null, "off"));

            Assert.Equal(400, (int)rejectedUpdate.StatusCode);
        }

        var globalUpdate = await adminClient.PutAsJsonAsync(
            "/v1/admin/notifications/policies/learner/__global__",
            new AdminNotificationPolicyUpdateRequest(
                false,
                false,
                false,
                null,
                MaxDeliveriesPerHour: 1,
                MaxDeliveriesPerDay: 1));
        globalUpdate.EnsureSuccessStatusCode();

        var rejectedCapUpdate = await adminClient.PutAsJsonAsync(
            "/v1/admin/notifications/policies/learner/LearnerPaymentSucceeded",
            new AdminNotificationPolicyUpdateRequest(null, null, null, null, MaxDeliveriesPerHour: 1));

        Assert.Equal(400, (int)rejectedCapUpdate.StatusCode);

        var learnerPreferenceUpdate = await learnerClient.PatchAsJsonAsync("/v1/notifications/preferences", new NotificationPreferencePatchRequest(
            null,
            false,
            false,
            false,
            null,
            null,
            null,
            null));
        learnerPreferenceUpdate.EnsureSuccessStatusCode();

        var rejectedPreferenceUpdate = await learnerClient.PatchAsJsonAsync("/v1/notifications/preferences", new NotificationPreferencePatchRequest(
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            new Dictionary<string, NotificationEventPreferencePayload>
            {
                ["LearnerEmailVerificationRequested"] = new(false, false, null, "off")
            }));

        Assert.Equal(400, (int)rejectedPreferenceUpdate.StatusCode);

        var policies = await adminClient.GetFromJsonAsync<AdminNotificationPoliciesResponse>(
            "/v1/admin/notifications/policies",
            JsonSupport.Options);

        Assert.NotNull(policies);
        Assert.Contains(policies!.Rows, row => row.EventKey == "LearnerPaymentSucceeded" && row.IsPolicyProtected && row.InAppEnabled && row.EmailEnabled);

        var proof = await TriggerProofAsync(adminClient, new AdminNotificationProofTriggerRequest(
            "LearnerPaymentSucceeded",
            SeedData.LearnerEmail,
            new Dictionary<string, string?>
            {
                ["message"] = "Your payment receipt is ready."
            }));

        var feed = await learnerClient.GetFromJsonAsync<NotificationFeedResponse>("/v1/notifications?page=1&pageSize=20", JsonSupport.Options);
        Assert.NotNull(feed);
        Assert.Contains(feed!.Items, item => item.Id == proof.InboxItemId && item.EventKey == "LearnerPaymentSucceeded");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var emailAttempt = await db.NotificationDeliveryAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(attempt =>
                attempt.NotificationEventId == proof.NotificationEventId
                && attempt.Channel == NotificationChannel.Email);

        Assert.NotNull(emailAttempt);
        Assert.Equal(NotificationDeliveryStatus.Sent, emailAttempt!.Status);

        var secondProof = await TriggerProofAsync(adminClient, new AdminNotificationProofTriggerRequest(
            "LearnerPaymentSucceeded",
            SeedData.LearnerEmail,
            new Dictionary<string, string?>
            {
                ["message"] = "Your second payment receipt is ready."
            },
            EntityId: "protected-payment-two"));

        var secondEmailAttempt = await db.NotificationDeliveryAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(attempt =>
                attempt.NotificationEventId == secondProof.NotificationEventId
                && attempt.Channel == NotificationChannel.Email);

        Assert.NotNull(secondEmailAttempt);
        Assert.Equal(NotificationDeliveryStatus.Sent, secondEmailAttempt!.Status);
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

    [Fact]
    public async Task SmsConsent_CanBeStoredByCategory_AndReturnedWithGlobalDefaults()
    {
        await using var factory = new TestWebApplicationFactory();
        using var adminClient = await CreateAuthenticatedClientAsync(factory, SeedData.AdminEmail, SeedData.LocalSeedPassword);
        using var learnerClient = await CreateAuthenticatedClientAsync(factory, SeedData.LearnerEmail, SeedData.LocalSeedPassword);

        var initialConsents = await learnerClient.GetFromJsonAsync<IReadOnlyList<NotificationConsentItem>>(
            "/v1/notifications/consents",
            JsonSupport.Options);

        Assert.NotNull(initialConsents);
        var globalSmsConsent = initialConsents!.Single(item => item.Channel == "sms" && item.Category == "global");
        Assert.False(globalSmsConsent.IsGranted);
        Assert.True(globalSmsConsent.RequiresExplicitConsent);

        var updateResponse = await learnerClient.PutAsJsonAsync(
            "/v1/notifications/consents/sms",
            new NotificationConsentUpdateRequest(true, "user", "Text reminders enabled.", "reminders"));
        updateResponse.EnsureSuccessStatusCode();

        var updatedConsent = await updateResponse.Content.ReadFromJsonAsync<NotificationConsentItem>(JsonSupport.Options);
        Assert.NotNull(updatedConsent);
        Assert.Equal("sms", updatedConsent!.Channel);
        Assert.Equal("reminders", updatedConsent.Category);
        Assert.True(updatedConsent.IsGranted);

        var refreshedConsents = await learnerClient.GetFromJsonAsync<IReadOnlyList<NotificationConsentItem>>(
            "/v1/notifications/consents",
            JsonSupport.Options);

        Assert.NotNull(refreshedConsents);
        Assert.Contains(refreshedConsents!, item => item.Channel == "sms" && item.Category == "global" && !item.IsGranted);
        Assert.Contains(refreshedConsents!, item => item.Channel == "sms" && item.Category == "reminders" && item.IsGranted);

        var adminConsents = await adminClient.GetFromJsonAsync<AdminNotificationConsentResponse>(
            $"/v1/admin/notifications/consents?authAccountId={Uri.EscapeDataString(globalSmsConsent.AuthAccountId)}&pageSize=20",
            JsonSupport.Options);

        Assert.NotNull(adminConsents);
        Assert.Contains(adminConsents!.Items, item => item.Channel == "sms" && item.Category == "reminders" && item.IsGranted);
    }

    [Fact]
    public async Task AdminSuppression_DisablesEmailFanout_AndCanBeReleased()
    {
        await using var factory = new TestWebApplicationFactory();
        using var adminClient = await CreateAuthenticatedClientAsync(factory, SeedData.AdminEmail, SeedData.LocalSeedPassword);
        using var learnerClient = await CreateAuthenticatedClientAsync(factory, SeedData.LearnerEmail, SeedData.LocalSeedPassword);

        var initialConsents = await learnerClient.GetFromJsonAsync<IReadOnlyList<NotificationConsentItem>>(
            "/v1/notifications/consents",
            JsonSupport.Options);

        Assert.NotNull(initialConsents);
        var authAccountId = initialConsents!.Single(item => item.Channel == "sms" && item.Category == "global").AuthAccountId;

        var suppressionResponse = await adminClient.PostAsJsonAsync(
            "/v1/admin/notifications/suppressions",
            new AdminNotificationSuppressionCreateRequest(
                authAccountId,
                "email",
                "LearnerReviewCompleted",
                "manual_suppression",
                "Compliance hold for email delivery.",
                null,
                DateTimeOffset.UtcNow.AddHours(1)));
        suppressionResponse.EnsureSuccessStatusCode();

        var suppression = await suppressionResponse.Content.ReadFromJsonAsync<NotificationSuppressionItem>(JsonSupport.Options);
        Assert.NotNull(suppression);
        Assert.True(suppression!.IsActive);
        Assert.Equal("email", suppression.Channel);

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
        var emailAttempt = await db.NotificationDeliveryAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(attempt =>
                attempt.NotificationEventId == proof.NotificationEventId
                && attempt.Channel == NotificationChannel.Email);

        Assert.NotNull(emailAttempt);
        Assert.Equal(NotificationDeliveryStatus.Suppressed, emailAttempt!.Status);
        Assert.Equal("manual_suppression", emailAttempt.ErrorCode);
        Assert.Equal("Compliance hold for email delivery.", emailAttempt.ErrorMessage);

        var releaseResponse = await adminClient.DeleteAsync($"/v1/admin/notifications/suppressions/{suppression.Id}");
        releaseResponse.EnsureSuccessStatusCode();

        var releasedSuppression = await releaseResponse.Content.ReadFromJsonAsync<NotificationSuppressionItem>(JsonSupport.Options);
        Assert.NotNull(releasedSuppression);
        Assert.False(releasedSuppression!.IsActive);
        Assert.NotNull(releasedSuppression.ReleasedAt);
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
