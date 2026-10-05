using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.RemoteJobs;

namespace OetLearner.Api.Tests.RemoteJobs;

/// <summary>
/// The hook <see cref="ContentTextExtractionWorker"/> consults before each in-process extraction (OET-RWP/1 section 6.1.2): with no
/// producer registered (every non-PostgreSQL host) the pass is exactly what it always was; a producer that answers Remote or Skip
/// takes the paper out of this tick's local pass, and one that answers Local changes nothing. The producer's own decisions are covered
/// on PostgreSQL elsewhere; here a fake drives the worker.
/// </summary>
public sealed class RemoteExtractionWorkerHookTests
{
    private sealed class RecordingService : IContentTextExtractionService
    {
        public List<string> PaperIds { get; } = [];

        public Task<int> ExtractForPaperAsync(string paperId, CancellationToken ct, bool force = false)
        {
            PaperIds.Add(paperId);
            return Task.FromResult(1);
        }
    }

    private sealed class FakeProducer(Func<string, RemoteExtractionDecision> decide) : IRemotePdfExtractionProducer
    {
        public List<string> Handled { get; } = [];

        public Task<RemoteExtractionDecision> HandlePaperAsync(string paperId, CancellationToken ct)
        {
            Handled.Add(paperId);
            return Task.FromResult(decide(paperId));
        }
    }

    private static async Task<ServiceProvider> BuildAsync(IContentTextExtractionService extraction, IRemotePdfExtractionProducer? producer)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase("extraction-hook-" + Guid.NewGuid().ToString("N"))
            .Options;

        await using (var seed = new LearnerDbContext(options))
        {
            var now = DateTimeOffset.UtcNow;
            seed.ContentPapers.AddRange(Enumerable.Range(0, 4).Select(index => new ContentPaper
            {
                Id = "paper-" + index,
                SubtestCode = "listening",
                Title = "paper-" + index,
                Slug = "paper-" + index,
                Status = ContentStatus.Published,
                CreatedAt = now.AddMinutes(index),
                UpdatedAt = now.AddMinutes(index),
            }));
            await seed.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddScoped(_ => new LearnerDbContext(options));
        services.AddSingleton(extraction);
        if (producer is not null) services.AddSingleton(producer);
        return services.BuildServiceProvider();
    }

    private static ContentTextExtractionWorker NewWorker(ServiceProvider provider)
        => new(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ContentTextExtractionWorker>.Instance);

    [Fact]
    public async Task WithoutAProducer_EveryPaperIsExtractedInProcess_ExactlyAsBefore()
    {
        var extraction = new RecordingService();
        using var provider = await BuildAsync(extraction, producer: null);

        var total = await NewWorker(provider).RunOnceAsync(CancellationToken.None);

        Assert.Equal(4, total);
        Assert.Equal(new[] { "paper-0", "paper-1", "paper-2", "paper-3" }, extraction.PaperIds);
    }

    [Fact]
    public async Task AProducerAnsweringRemoteOrSkip_TakesThePaperOutOfThisTicksLocalPass()
    {
        var extraction = new RecordingService();
        var producer = new FakeProducer(id => id switch
        {
            "paper-1" => RemoteExtractionDecision.Remote,
            "paper-2" => RemoteExtractionDecision.Skip,
            _ => RemoteExtractionDecision.Local,
        });
        using var provider = await BuildAsync(extraction, producer);

        var total = await NewWorker(provider).RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, total);
        Assert.Equal(new[] { "paper-0", "paper-3" }, extraction.PaperIds);
        Assert.Equal(new[] { "paper-0", "paper-1", "paper-2", "paper-3" }, producer.Handled); // asked about every paper, oldest first
    }

    [Fact]
    public async Task AProducerAnsweringLocal_ChangesNothing()
    {
        var extraction = new RecordingService();
        var producer = new FakeProducer(_ => RemoteExtractionDecision.Local);
        using var provider = await BuildAsync(extraction, producer);

        var total = await NewWorker(provider).RunOnceAsync(CancellationToken.None);

        Assert.Equal(4, total);
        Assert.Equal(new[] { "paper-0", "paper-1", "paper-2", "paper-3" }, extraction.PaperIds);
    }
}
