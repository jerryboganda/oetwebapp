using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.LiveClasses;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.LiveClasses;

public sealed class LiveClassRecordingProcessingServiceTests
{
    [Fact]
    public async Task Transcribe_uses_combined_streaming_read_and_disposes_the_owned_stream()
    {
        await using var db = NewDb();
        var audio = Enumerable.Range(0, 2 * 1024 * 1024)
            .Select(index => (byte)(index % 251))
            .ToArray();
        var storage = new TrackingStorage(audio);
        var gateway = new TrackingGateway((_, _) => Task.FromResult("Transcript text"));
        await SeedRecordingAsync(db, "recording-stream", "recording.m4a");
        var service = CreateService(db, gateway, storage);

        await service.ProcessTranscribeAsync(
            "recording-stream", CancellationToken.None);

        Assert.Equal(1, storage.CombinedReadCalls);
        Assert.Equal(0, storage.LengthCalls);
        Assert.Equal(0, storage.PlainReadCalls);
        Assert.True(storage.LastStream!.IsDisposed);
        Assert.InRange(storage.LastStream.MaxReadRequest, 1, 81920);
        var attachment = Assert.Single(gateway.Requests).AudioAttachments;
        var audioAttachment = Assert.Single(attachment!);
        Assert.Equal("audio/mp4", audioAttachment.MimeType);
        Assert.Equal(audio, audioAttachment.Data);
        Assert.Equal(
            "Transcript text",
            (await db.LiveClassRecordings.SingleAsync()).TranscriptText);
    }

    [Fact]
    public async Task Transcribe_cancellation_from_gateway_disposes_storage_stream()
    {
        await using var db = NewDb();
        var storage = new TrackingStorage(new byte[1024 * 1024]);
        var enteredGateway = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new TrackingGateway(async (_, ct) =>
        {
            enteredGateway.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return string.Empty;
        });
        await SeedRecordingAsync(db, "recording-cancel", "recording.wav");
        var service = CreateService(db, gateway, storage);
        using var cts = new CancellationTokenSource();

        var processing = service.ProcessTranscribeAsync(
            "recording-cancel", cts.Token);
        await enteredGateway.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await processing);
        Assert.True(storage.LastStream!.IsDisposed);
    }

    [Fact]
    public async Task Embed_without_gateway_batch_contract_keeps_concurrency_at_one()
    {
        await using var db = NewDb();
        var active = 0;
        var maxActive = 0;
        var gateway = new TrackingGateway(async (request, ct) =>
        {
            if (request.FeatureCode == AiFeatureCodes.ClassAssistantQna)
            {
                var current = Interlocked.Increment(ref active);
                maxActive = Math.Max(maxActive, current);
                try
                {
                    await Task.Delay(10, ct);
                    return "[0.1,0.2]";
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            }
            return string.Empty;
        });
        db.LiveClassRecordings.Add(new LiveClassRecording
        {
            Id = "recording-embed",
            ClassSessionId = "session-embed",
            TranscriptText = new string('x', 5_000),
            DurationSeconds = 600,
            RecordedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var service = CreateService(
            db, gateway, new TrackingStorage(Array.Empty<byte>()));

        await service.ProcessEmbedAsync(
            "recording-embed", CancellationToken.None);

        Assert.Equal(1, maxActive);
        Assert.Equal(
            LiveClassRecordingProcessingService
                .ChunkTranscript(new string('x', 5_000), 600).Count,
            gateway.Requests.Count);
        Assert.All(
            await db.ClassRecordingEmbeddings.ToListAsync(),
            row => Assert.Equal("[0.1,0.2]", row.EmbeddingJson));
    }

    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static LiveClassRecordingProcessingService CreateService(
        LearnerDbContext db,
        IAiGatewayService gateway,
        IFileStorage storage)
        => new(
            db,
            gateway,
            storage,
            TestRuntimeSettingsProvider.WithLiveClassAi(enabled: true),
            TimeProvider.System,
            NullLogger<LiveClassRecordingProcessingService>.Instance);

    private static async Task SeedRecordingAsync(
        LearnerDbContext db,
        string id,
        string audioKey)
    {
        db.LiveClassRecordings.Add(new LiveClassRecording
        {
            Id = id,
            ClassSessionId = $"session-{id}",
            S3AudioKey = audioKey,
            RecordedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private sealed class TrackingGateway(
        Func<AiGatewayRequest, CancellationToken, Task<string>> complete)
        : IAiGatewayService
    {
        public List<AiGatewayRequest> Requests { get; } = [];

        public async Task<AiGatewayResult> CompleteAsync(
            AiGatewayRequest request,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            var completion = await complete(request, ct);
            return new AiGatewayResult
            {
                Completion = completion,
                RulebookVersion = "test",
                Metadata = new AiGroundedPromptMetadata
                {
                    RulebookKind = RuleKind.Grammar,
                    Profession = ExamProfession.Medicine,
                    RulebookVersion = "test",
                },
            };
        }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "OET AI — Rulebook-Grounded System Prompt",
                TaskInstruction = "Test",
                Metadata = new AiGroundedPromptMetadata
                {
                    RulebookKind = context.Kind,
                    Profession = context.Profession,
                    RulebookVersion = "test",
                },
            };
    }

    private sealed class TrackingStorage(byte[] bytes) : IFileStorage
    {
        public int CombinedReadCalls { get; private set; }
        public int PlainReadCalls { get; private set; }
        public int LengthCalls { get; private set; }
        public TrackingReadStream? LastStream { get; private set; }

        public Task<FileStorageReadResult> OpenReadWithMetadataAsync(
            string key,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            CombinedReadCalls++;
            LastStream = new TrackingReadStream(
                new MemoryStream(bytes, writable: false));
            return Task.FromResult(
                new FileStorageReadResult(LastStream, bytes.LongLength));
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
        {
            PlainReadCalls++;
            throw new InvalidOperationException(
                "Combined read should be used for recording transcription.");
        }

        public Task<long> LengthAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            LengthCalls++;
            throw new InvalidOperationException(
                "Separate metadata reads should not be used.");
        }

        public Task<long> WriteAsync(
            string key, Stream source, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<Stream> OpenWriteAsync(
            string key, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<bool> ExistsAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }

        public Task<bool> DeleteAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }

        public Task MoveAsync(string sourceKey, string destKey, bool overwrite, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            throw new NotSupportedException();
        }

        public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(0);
        }
        public string? TryResolveLocalPath(string key) => null;
        public Uri? ResolveReadUrl(string key, TimeSpan ttl) => null;
    }

    private sealed class TrackingReadStream(Stream inner) : Stream
    {
        public bool IsDisposed { get; private set; }
        public int MaxReadRequest { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            MaxReadRequest = Math.Max(MaxReadRequest, buffer.Length);
            return await inner.ReadAsync(buffer, cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            MaxReadRequest = Math.Max(MaxReadRequest, count);
            return inner.Read(buffer, offset, count);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                IsDisposed = true;
                inner.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            IsDisposed = true;
            await inner.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}
