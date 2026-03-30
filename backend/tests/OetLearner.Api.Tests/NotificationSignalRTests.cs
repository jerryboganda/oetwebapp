using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class NotificationSignalRTests
{
    [Fact]
    public async Task SignalRHub_RoutesNotifications_ToMatchingAuthAccountGroupOnly()
    {
        await using var factory = new FirstPartyAuthTestWebApplicationFactory();
        using var adminClient = factory.CreateClient();
        using var signInClient = factory.CreateClient();
        using var expertSignInClient = factory.CreateClient();

        var adminSession = await SignInAsync(adminClient, SeedData.AdminEmail, SeedData.LocalSeedPassword);
        var learnerSession = await SignInAsync(signInClient, SeedData.LearnerEmail, SeedData.LocalSeedPassword);
        var expertSession = await SignInAsync(expertSignInClient, SeedData.ExpertEmail, SeedData.LocalSeedPassword);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminSession.AccessToken);

        var learnerReceived = new TaskCompletionSource<NotificationRealtimeEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expertReceived = new TaskCompletionSource<NotificationRealtimeEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var learnerConnection = CreateConnection(factory, learnerSession.AccessToken, envelope => learnerReceived.TrySetResult(envelope));
        await using var expertConnection = CreateConnection(factory, expertSession.AccessToken, envelope => expertReceived.TrySetResult(envelope));

        await learnerConnection.StartAsync();
        await expertConnection.StartAsync();

        var proofResponse = await adminClient.PostAsJsonAsync("/v1/admin/notifications/proof/trigger", new AdminNotificationProofTriggerRequest(
            "LearnerReviewCompleted",
            SeedData.LearnerEmail,
            new Dictionary<string, string?>
            {
                ["attemptId"] = "wa-001",
                ["subtest"] = "writing"
            }));
        proofResponse.EnsureSuccessStatusCode();

        var envelope = await learnerReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("notification.created", envelope.Type);
        Assert.Equal("LearnerReviewCompleted", envelope.Notification.EventKey);
        Assert.Contains("/submissions/wa-001", envelope.Notification.ActionUrl);

        var expertCompleted = await Task.WhenAny(expertReceived.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.NotSame(expertReceived.Task, expertCompleted);
    }

    private static HubConnection CreateConnection(
        FirstPartyAuthTestWebApplicationFactory factory,
        string accessToken,
        Action<NotificationRealtimeEnvelope> onEnvelope)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress!, "/v1/notifications/hub"), options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            })
            .Build();

        connection.On<NotificationRealtimeEnvelope>("notification", onEnvelope);
        return connection;
    }

    private static async Task<AuthSessionResponse> SignInAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/v1/auth/sign-in", new PasswordSignInRequest(email, password, true));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthSessionResponse>(JsonSupport.Options))
            ?? throw new InvalidOperationException("Expected a valid auth session response.");
    }
}
