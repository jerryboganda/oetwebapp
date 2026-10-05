using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Fleet.Core.Policy;

namespace Fleet.Manager.Infrastructure;

/// <summary>A change notification pushed to the owner UI over SSE. <see cref="DataJson"/> never carries secrets.</summary>
public sealed record FleetEvent(string Type, string DataJson, DateTimeOffset At);

public interface IEventBus
{
    /// <summary>Fire-and-forget fan-out. <paramref name="data"/> must already be free of secrets.</summary>
    void Publish(string type, object? data);

    EventSubscription Subscribe();
}

public sealed class EventSubscription : IDisposable
{
    private readonly Action _onDispose;

    internal EventSubscription(ChannelReader<FleetEvent> reader, Action onDispose)
    {
        Reader = reader;
        _onDispose = onDispose;
    }

    public ChannelReader<FleetEvent> Reader { get; }

    public void Dispose() => _onDispose();
}

/// <summary>In-process, bounded (drop-oldest) fan-out to every connected SSE client.</summary>
public sealed class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<Guid, Channel<FleetEvent>> _subscribers = new();
    private readonly TimeProvider _time;

    public EventBus(TimeProvider time)
    {
        _time = time;
    }

    public int SubscriberCount => _subscribers.Count;

    public void Publish(string type, object? data)
    {
        var json = data is null ? "{}" : JsonSerializer.Serialize(data, FleetJson.Options);
        var fleetEvent = new FleetEvent(type, json, _time.GetUtcNow());
        foreach (var channel in _subscribers.Values)
        {
            channel.Writer.TryWrite(fleetEvent);
        }
    }

    public EventSubscription Subscribe()
    {
        var channel = Channel.CreateBounded<FleetEvent>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        var id = Guid.NewGuid();
        _subscribers[id] = channel;
        return new EventSubscription(channel.Reader, () =>
        {
            if (_subscribers.TryRemove(id, out var removed))
            {
                removed.Writer.TryComplete();
            }
        });
    }
}
