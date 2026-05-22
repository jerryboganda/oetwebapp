using System.Threading.Channels;

namespace OetLearner.Api.Services.Vocabulary;

/// <summary>
/// Singleton in-memory queue. Unbounded so a large import batch never
/// blocks the calling request. Pending count is tracked via Interlocked
/// because Channel{T}.Reader.Count is not available on all TFMs/channel
/// variants — keeping our own counter avoids surprises.
/// </summary>
public sealed class VocabularyAudioQueue : IVocabularyAudioQueue
{
    private readonly Channel<VocabularyAudioJob> _channel = Channel.CreateUnbounded<VocabularyAudioJob>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    private int _pending;

    public int PendingCount => Volatile.Read(ref _pending);

    public async ValueTask EnqueueAsync(VocabularyAudioJob job, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _pending);
        try
        {
            await _channel.Writer.WriteAsync(job, ct).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Decrement(ref _pending);
            throw;
        }
    }

    public async IAsyncEnumerable<VocabularyAudioJob> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var job in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            Interlocked.Decrement(ref _pending);
            yield return job;
        }
    }
}
