using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
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

public sealed class WebPushDispatcher(IOptions<WebPushOptions> options) : IWebPushDispatcher
{
    private readonly WebPushOptions _options = options.Value;

    public async Task SendAsync(OetLearner.Api.Domain.PushSubscription subscription, string payload, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = new WebPushClient();
            var webPushSubscription = new WebPush.PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth);
            var vapidDetails = new VapidDetails(_options.Subject, _options.PublicKey, _options.PrivateKey);
            await client.SendNotificationAsync(webPushSubscription, payload, vapidDetails, cancellationToken);
        }
        catch (WebPushException ex)
        {
            throw new PushDispatchException((int)ex.StatusCode, ex.Message, ex);
        }
    }
}
