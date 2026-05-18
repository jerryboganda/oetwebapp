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

public sealed class WebPushDispatcher(IRuntimeSettingsProvider runtimeSettings) : IWebPushDispatcher
{
    public async Task SendAsync(OetLearner.Api.Domain.PushSubscription subscription, string payload, CancellationToken cancellationToken = default)
    {
        try
        {
            var pushSettings = (await runtimeSettings.GetAsync(cancellationToken)).Push;
            if (!pushSettings.WebPushEnabled
                || string.IsNullOrWhiteSpace(pushSettings.WebPushSubject)
                || string.IsNullOrWhiteSpace(pushSettings.WebPushPublicKey)
                || string.IsNullOrWhiteSpace(pushSettings.WebPushPrivateKey))
            {
                throw new InvalidOperationException("WebPush runtime settings must be configured before sending push notifications.");
            }

            var client = new WebPushClient();
            var webPushSubscription = new WebPush.PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth);
            var vapidDetails = new VapidDetails(pushSettings.WebPushSubject, pushSettings.WebPushPublicKey, pushSettings.WebPushPrivateKey);
            await client.SendNotificationAsync(webPushSubscription, payload, vapidDetails, cancellationToken);
        }
        catch (WebPushException ex)
        {
            throw new PushDispatchException((int)ex.StatusCode, ex.Message, ex);
        }
    }
}
