using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Settings;
using WebPush;

namespace OetLearner.Api.Services;

public sealed class PushDispatchException : Exception
{
    public PushDispatchException(int? statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}

public interface IWebPushDispatcher
{
    Task SendAsync(OetLearner.Api.Domain.PushSubscription subscription, string payload, CancellationToken cancellationToken = default);
}

public sealed class WebPushDispatcher(IOptions<WebPushOptions> options, IRuntimeSettingsProvider runtimeSettingsProvider) : IWebPushDispatcher
{
    private readonly WebPushOptions _options = options.Value;

    public async Task SendAsync(OetLearner.Api.Domain.PushSubscription subscription, string payload, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = new WebPushClient();
            var webPushSubscription = new WebPush.PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth);
            var pushSettings = (await runtimeSettingsProvider.GetAsync(cancellationToken)).Push;
            var vapidDetails = new VapidDetails(
                Coalesce(pushSettings.VapidSubject, _options.Subject) ?? string.Empty,
                Coalesce(pushSettings.VapidPublicKey, _options.PublicKey) ?? string.Empty,
                Coalesce(pushSettings.VapidPrivateKey, _options.PrivateKey) ?? string.Empty);
            await client.SendNotificationAsync(webPushSubscription, payload, vapidDetails, cancellationToken);
        }
        catch (WebPushException ex)
        {
            throw new PushDispatchException((int)ex.StatusCode, ex.Message, ex);
        }
    }

    private static string? Coalesce(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
