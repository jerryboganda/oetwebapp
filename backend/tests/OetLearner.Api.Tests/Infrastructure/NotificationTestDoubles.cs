using System.Collections.Concurrent;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests.Infrastructure;

public sealed class RecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    public IReadOnlyCollection<EmailMessage> Messages => _messages.ToArray();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _messages.Enqueue(message);
        return Task.CompletedTask;
    }
}

public sealed class FakeWebPushDispatcher : IWebPushDispatcher
{
    private readonly ConcurrentQueue<(PushSubscription Subscription, string Payload)> _dispatches = new();

    public Func<PushSubscription, string, CancellationToken, Task>? OnSendAsync { get; set; }

    public IReadOnlyCollection<(PushSubscription Subscription, string Payload)> Dispatches => _dispatches.ToArray();

    public async Task SendAsync(PushSubscription subscription, string payload, CancellationToken cancellationToken = default)
    {
        _dispatches.Enqueue((subscription, payload));
        if (OnSendAsync is not null)
        {
            await OnSendAsync(subscription, payload, cancellationToken);
        }
    }
}
