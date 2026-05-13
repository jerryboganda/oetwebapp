using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Billing;

namespace OetLearner.Api.Tests;

/// <summary>
/// Billing-hardening I-9 closure (May 2026). Locks the tiered PII retention
/// contract for the new <see cref="WebhookPiiRetentionWorker"/>:
///
///   • Rows older than <c>PaymentWebhookPiiNullOutAge</c> get
///     <c>PayloadJson = "{}"</c> and <c>ErrorMessage = null</c>.
///   • Already-nulled rows are left alone (idempotent).
///   • Rows newer than the cutoff are untouched.
///   • Disabled when the option is <see cref="TimeSpan.Zero"/>.
///   • Batch size is honoured per tick.
/// </summary>
public class WebhookPiiRetentionWorkerTests
{
    private static (IServiceScopeFactory, LearnerDbContext) CreateScope(string dbName, out IServiceProvider provider)
    {
        var services = new ServiceCollection();
        services.AddDbContext<LearnerDbContext>(opts => opts.UseInMemoryDatabase(dbName));
        provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var scope = scopeFactory.CreateScope();
        return (scopeFactory, scope.ServiceProvider.GetRequiredService<LearnerDbContext>());
    }

    private static WebhookPiiRetentionWorker BuildWorker(IServiceScopeFactory scopeFactory, DataRetentionOptions opts)
        => new(scopeFactory, Options.Create(opts), NullLogger<WebhookPiiRetentionWorker>.Instance);

    [Fact]
    public async Task RunOnceAsync_NullsPayload_OnRowsOlderThanCutoff()
    {
        var (scopeFactory, db) = CreateScope(nameof(RunOnceAsync_NullsPayload_OnRowsOlderThanCutoff), out _);

        var now = DateTimeOffset.UtcNow;
        db.PaymentWebhookEvents.AddRange(
            new PaymentWebhookEvent
            {
                Id = Guid.NewGuid(),
                Gateway = "stripe",
                EventType = "charge.succeeded",
                GatewayEventId = "evt_old_1",
                PayloadJson = "{\"id\":\"evt_old_1\",\"amount\":1000}",
                ErrorMessage = "n/a",
                ReceivedAt = now - TimeSpan.FromDays(120),
            },
            new PaymentWebhookEvent
            {
                Id = Guid.NewGuid(),
                Gateway = "paypal",
                EventType = "PAYMENT.SALE.COMPLETED",
                GatewayEventId = "evt_old_2",
                PayloadJson = "{\"id\":\"evt_old_2\"}",
                ErrorMessage = null,
                ReceivedAt = now - TimeSpan.FromDays(91),
            });
        await db.SaveChangesAsync();

        var worker = BuildWorker(scopeFactory, new DataRetentionOptions
        {
            PaymentWebhookPiiNullOutAge = TimeSpan.FromDays(90),
        });

        var nulled = await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, nulled);

        var rows = await db.PaymentWebhookEvents.AsNoTracking().ToListAsync();
        Assert.All(rows, row =>
        {
            Assert.Equal("{}", row.PayloadJson);
            Assert.Null(row.ErrorMessage);
        });
    }

    [Fact]
    public async Task RunOnceAsync_DoesNotTouchRows_NewerThanCutoff()
    {
        var (scopeFactory, db) = CreateScope(nameof(RunOnceAsync_DoesNotTouchRows_NewerThanCutoff), out _);

        var now = DateTimeOffset.UtcNow;
        db.PaymentWebhookEvents.Add(new PaymentWebhookEvent
        {
            Id = Guid.NewGuid(),
            Gateway = "stripe",
            EventType = "charge.succeeded",
            GatewayEventId = "evt_recent",
            PayloadJson = "{\"id\":\"evt_recent\",\"amount\":500}",
            ErrorMessage = "transient retry",
            ReceivedAt = now - TimeSpan.FromDays(30),
        });
        await db.SaveChangesAsync();

        var worker = BuildWorker(scopeFactory, new DataRetentionOptions
        {
            PaymentWebhookPiiNullOutAge = TimeSpan.FromDays(90),
        });

        var nulled = await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(0, nulled);

        var row = await db.PaymentWebhookEvents.AsNoTracking().SingleAsync();
        Assert.Equal("{\"id\":\"evt_recent\",\"amount\":500}", row.PayloadJson);
        Assert.Equal("transient retry", row.ErrorMessage);
    }

    [Fact]
    public async Task RunOnceAsync_IsIdempotent_OnAlreadyNulledRows()
    {
        var (scopeFactory, db) = CreateScope(nameof(RunOnceAsync_IsIdempotent_OnAlreadyNulledRows), out _);

        var now = DateTimeOffset.UtcNow;
        db.PaymentWebhookEvents.Add(new PaymentWebhookEvent
        {
            Id = Guid.NewGuid(),
            Gateway = "stripe",
            EventType = "charge.succeeded",
            GatewayEventId = "evt_already_nulled",
            PayloadJson = "{}",
            ErrorMessage = null,
            ReceivedAt = now - TimeSpan.FromDays(200),
        });
        await db.SaveChangesAsync();

        var worker = BuildWorker(scopeFactory, new DataRetentionOptions
        {
            PaymentWebhookPiiNullOutAge = TimeSpan.FromDays(90),
        });

        var firstSweep = await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(0, firstSweep);

        var secondSweep = await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(0, secondSweep);
    }

    [Fact]
    public async Task RunOnceAsync_IsDisabled_WhenOptionIsZero()
    {
        var (scopeFactory, db) = CreateScope(nameof(RunOnceAsync_IsDisabled_WhenOptionIsZero), out _);

        db.PaymentWebhookEvents.Add(new PaymentWebhookEvent
        {
            Id = Guid.NewGuid(),
            Gateway = "stripe",
            EventType = "charge.succeeded",
            GatewayEventId = "evt_should_remain",
            PayloadJson = "{\"id\":\"evt_should_remain\"}",
            ReceivedAt = DateTimeOffset.UtcNow - TimeSpan.FromDays(500),
        });
        await db.SaveChangesAsync();

        var worker = BuildWorker(scopeFactory, new DataRetentionOptions
        {
            PaymentWebhookPiiNullOutAge = TimeSpan.Zero,
        });

        var nulled = await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(0, nulled);

        var row = await db.PaymentWebhookEvents.AsNoTracking().SingleAsync();
        Assert.Equal("{\"id\":\"evt_should_remain\"}", row.PayloadJson);
    }

    [Fact]
    public async Task RunOnceAsync_RespectsBatchSize()
    {
        var (scopeFactory, db) = CreateScope(nameof(RunOnceAsync_RespectsBatchSize), out _);

        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 7; i++)
        {
            db.PaymentWebhookEvents.Add(new PaymentWebhookEvent
            {
                Id = Guid.NewGuid(),
                Gateway = "stripe",
                EventType = "charge.succeeded",
                GatewayEventId = $"evt_batch_{i}",
                PayloadJson = $"{{\"id\":\"evt_batch_{i}\"}}",
                ReceivedAt = now - TimeSpan.FromDays(100 + i),
            });
        }
        await db.SaveChangesAsync();

        var worker = BuildWorker(scopeFactory, new DataRetentionOptions
        {
            PaymentWebhookPiiNullOutAge = TimeSpan.FromDays(90),
            BatchSize = 3,
        });

        var firstBatch = await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(3, firstBatch);

        var secondBatch = await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(3, secondBatch);

        var thirdBatch = await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, thirdBatch);

        var fourthBatch = await worker.RunOnceAsync(CancellationToken.None);
        Assert.Equal(0, fourthBatch);
    }
}
