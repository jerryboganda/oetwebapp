using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public sealed class NotificationCampaignServicePerformanceTests : IAsyncLifetime
{
    private const int RecipientCount = 1_201;
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly SqlCaptureInterceptor _sql = new();
    private readonly RecipientBatchInterceptor _batches = new();
    private DbContextOptions<LearnerDbContext> _options = default!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_sql, _batches)
            .Options;

        await using var db = new LearnerDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task EvaluateAndSend_KeepRecipientWorkBoundedAndAuditIdempotent()
    {
        var campaignId = Guid.NewGuid();
        await using (var seed = new LearnerDbContext(_options))
        {
            seed.NotificationCampaigns.Add(new NotificationCampaign
            {
                Id = campaignId,
                Name = "Large campaign",
                Subject = "Subject",
                Body = "Body",
                Channel = NotificationChannel.Email,
                Status = NotificationCampaignStatus.Sending,
                SegmentJson = "{}",
                CreatedByAdminId = "admin",
                CreatedAt = Now.AddHours(-1),
                UpdatedAt = Now.AddHours(-1),
            });

            seed.NotificationConsents.AddRange(Enumerable.Range(0, RecipientCount).Select(index =>
                new NotificationConsent
                {
                    Id = Guid.NewGuid(),
                    AuthAccountId = $"user-{index:D5}",
                    Channel = NotificationChannel.Email,
                    Category = "global",
                    IsGranted = true,
                    Source = "user",
                    CreatedAt = Now.AddDays(-1),
                    UpdatedAt = Now.AddDays(-1),
                }));
            seed.NotificationConsents.Add(new NotificationConsent
            {
                Id = Guid.NewGuid(),
                AuthAccountId = "user-00000",
                Channel = NotificationChannel.Email,
                Category = "marketing",
                IsGranted = true,
                Source = "user",
                CreatedAt = Now.AddDays(-1),
                UpdatedAt = Now.AddDays(-1),
            });
            await seed.SaveChangesAsync();
        }

        await using var db = new LearnerDbContext(_options);
        var service = new NotificationCampaignService(
            db,
            new FixedClock(Now),
            NullLogger<NotificationCampaignService>.Instance);

        _sql.Commands.Clear();
        var evaluated = await service.EvaluateSegmentAsync(campaignId, default);

        Assert.Equal(RecipientCount, evaluated);
        Assert.Single(_sql.Commands.Where(command => command.Contains("\"NotificationConsents\"")));
        Assert.Single(db.ChangeTracker.Entries<NotificationCampaign>());
        Assert.Empty(db.ChangeTracker.Entries<NotificationCampaignRecipient>());

        db.ChangeTracker.Clear();
        _sql.Commands.Clear();
        _batches.RecipientBatchSizes.Clear();

        var result = await service.SendAsync(campaignId, default);

        Assert.Equal(RecipientCount, result.Delivered);
        Assert.Equal(0, result.Failed);
        Assert.Equal(new[] { 500, 500, 201 }, _batches.RecipientBatchSizes);
        Assert.Equal(
            4,
            _sql.Commands.Count(command => command.Contains("\"NotificationConsents\"")));
        Assert.Empty(db.ChangeTracker.Entries<NotificationCampaignRecipient>());

        db.ChangeTracker.Clear();
        var campaign = await db.NotificationCampaigns.AsNoTracking().SingleAsync(c => c.Id == campaignId);
        var audit = await db.NotificationCampaignRecipients
            .AsNoTracking()
            .Where(r => r.CampaignId == campaignId)
            .ToListAsync();

        Assert.Equal(NotificationCampaignStatus.Sent, campaign.Status);
        Assert.Equal(RecipientCount, campaign.RecipientCount);
        Assert.Equal(Now, campaign.SentAt);
        Assert.Equal(RecipientCount, audit.Count);
        Assert.Equal(RecipientCount, audit.Select(r => r.RecipientUserId).Distinct().Count());
        Assert.All(audit, row =>
        {
            Assert.Equal(NotificationDeliveryStatus.Pending, row.DeliveryStatus);
            Assert.Equal("", row.RecipientEmail);
            Assert.Equal(Now, row.CreatedAt);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendAsync(campaignId, default));
        Assert.Equal(
            RecipientCount,
            await db.NotificationCampaignRecipients.CountAsync(r => r.CampaignId == campaignId));
    }

    [Fact]
    public async Task Send_TwoContextsCreateOneRecipientAuditPerConsentedUser()
    {
        const int concurrentRecipientCount = 501;
        var campaignId = Guid.NewGuid();
        var connectionString =
            $"Data Source=notification-send-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=30";

        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();
        var seedOptions = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connectionString)
            .Options;

        await using (var seed = new LearnerDbContext(seedOptions))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.NotificationCampaigns.Add(new NotificationCampaign
            {
                Id = campaignId,
                Name = "Concurrent campaign",
                Subject = "Subject",
                Body = "Body",
                Channel = NotificationChannel.Email,
                Status = NotificationCampaignStatus.Sending,
                SegmentJson = "{}",
                CreatedByAdminId = "admin",
                CreatedAt = Now.AddHours(-1),
                UpdatedAt = Now.AddHours(-1),
            });
            seed.NotificationConsents.AddRange(Enumerable.Range(0, concurrentRecipientCount).Select(index =>
                new NotificationConsent
                {
                    Id = Guid.NewGuid(),
                    AuthAccountId = $"concurrent-user-{index:D4}",
                    Channel = NotificationChannel.Email,
                    Category = "global",
                    IsGranted = true,
                    Source = "user",
                    CreatedAt = Now.AddDays(-1),
                    UpdatedAt = Now.AddDays(-1),
                }));
            await seed.SaveChangesAsync();
        }

        var gate = new CampaignReadGateInterceptor();
        var firstOptions = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(gate)
            .Options;
        var secondOptions = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(gate)
            .Options;

        await using (var firstDb = new LearnerDbContext(firstOptions))
        await using (var secondDb = new LearnerDbContext(secondOptions))
        {
            var firstService = new NotificationCampaignService(
                firstDb,
                new FixedClock(Now),
                NullLogger<NotificationCampaignService>.Instance);
            var secondService = new NotificationCampaignService(
                secondDb,
                new FixedClock(Now),
                NullLogger<NotificationCampaignService>.Instance);

            var outcomes = await Task.WhenAll(
                CaptureSendAsync(firstService, campaignId),
                CaptureSendAsync(secondService, campaignId));

            Assert.Single(outcomes, outcome => outcome.Result is not null);
            Assert.Single(outcomes, outcome => outcome.Error is not null);
        }

        await using var verify = new LearnerDbContext(seedOptions);
        Assert.Equal(
            concurrentRecipientCount,
            await verify.NotificationCampaignRecipients
                .CountAsync(recipient => recipient.CampaignId == campaignId));
        Assert.Equal(
            concurrentRecipientCount,
            await verify.NotificationCampaignRecipients
                .Where(recipient => recipient.CampaignId == campaignId)
                .Select(recipient => recipient.RecipientUserId)
                .Distinct()
                .CountAsync());
        Assert.Equal(
            NotificationCampaignStatus.Sent,
            await verify.NotificationCampaigns
                .Where(campaign => campaign.Id == campaignId)
                .Select(campaign => campaign.Status)
                .SingleAsync());
    }

    [Fact]
    public async Task Cancel_WhenSendCommitsFirst_DoesNotOverwriteSentOrRecipientAudit()
    {
        var campaignId = Guid.NewGuid();
        var connectionString =
            $"Data Source=notification-cancel-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=30";

        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();
        var baseOptions = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connectionString)
            .Options;

        await using (var seed = new LearnerDbContext(baseOptions))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.NotificationCampaigns.Add(new NotificationCampaign
            {
                Id = campaignId,
                Name = "Cancel race campaign",
                Subject = "Subject",
                Body = "Body",
                Channel = NotificationChannel.Email,
                Status = NotificationCampaignStatus.Sending,
                SegmentJson = "{}",
                CreatedByAdminId = "admin",
                CreatedAt = Now.AddHours(-1),
                UpdatedAt = Now.AddHours(-1),
            });
            seed.NotificationConsents.Add(CreateConsent("cancel-race-user", NotificationChannel.Email));
            await seed.SaveChangesAsync();
        }

        var sendGate = new CampaignUpdateGateInterceptor();
        var cancelGate = new CampaignUpdateGateInterceptor();
        var sendOptions = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(sendGate)
            .Options;
        var cancelOptions = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(cancelGate)
            .Options;

        await using (var sendDb = new LearnerDbContext(sendOptions))
        await using (var cancelDb = new LearnerDbContext(cancelOptions))
        {
            var sendService = new NotificationCampaignService(
                sendDb,
                new FixedClock(Now),
                NullLogger<NotificationCampaignService>.Instance);
            var cancelService = new NotificationCampaignService(
                cancelDb,
                new FixedClock(Now.AddMinutes(1)),
                NullLogger<NotificationCampaignService>.Instance);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var sendTask = sendService.SendAsync(campaignId, timeout.Token);
            await sendGate.WaitUntilBlockedAsync(timeout.Token);
            var cancelTask = cancelService.CancelAsync("admin", campaignId, timeout.Token);

            try
            {
                await cancelGate.WaitUntilBlockedAsync(timeout.Token);
                sendGate.Release();

                var sendResult = await sendTask;
                Assert.Equal(1, sendResult.Delivered);

                cancelGate.Release();
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => cancelTask);
                Assert.Equal("Cannot cancel campaign in Sent status.", error.Message);
            }
            finally
            {
                sendGate.Release();
                cancelGate.Release();
            }
        }

        await using var verify = new LearnerDbContext(baseOptions);
        var campaign = await verify.NotificationCampaigns
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == campaignId);
        var audit = await verify.NotificationCampaignRecipients
            .AsNoTracking()
            .Where(recipient => recipient.CampaignId == campaignId)
            .ToListAsync();

        Assert.Equal(NotificationCampaignStatus.Sent, campaign.Status);
        Assert.Equal(Now, campaign.SentAt);
        Assert.Equal(Now, campaign.UpdatedAt);
        Assert.Equal(1, campaign.RecipientCount);
        Assert.Single(audit);
        Assert.Equal("cancel-race-user", audit[0].RecipientUserId);
        Assert.Equal(NotificationDeliveryStatus.Pending, audit[0].DeliveryStatus);
    }

    [Fact]
    public async Task Send_ReloadsSegmentChannelChangedAfterInitialRead()
    {
        var campaignId = Guid.NewGuid();
        var connectionString =
            $"Data Source=notification-update-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=30";
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();
        var baseOptions = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connectionString)
            .Options;

        await using (var seed = new LearnerDbContext(baseOptions))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.NotificationCampaigns.Add(new NotificationCampaign
            {
                Id = campaignId,
                Name = "Updated segment campaign",
                Subject = "Subject",
                Body = "Body",
                Channel = NotificationChannel.Email,
                Status = NotificationCampaignStatus.Scheduled,
                SegmentJson = "{}",
                CreatedByAdminId = "admin",
                CreatedAt = Now.AddHours(-1),
                UpdatedAt = Now.AddHours(-1),
            });
            seed.NotificationConsents.AddRange(
                CreateConsent("email-user", NotificationChannel.Email),
                CreateConsent("sms-user", NotificationChannel.Sms));
            await seed.SaveChangesAsync();
        }

        var beforeClaim = new BeforeCampaignClaimInterceptor(NotificationChannel.Sms);
        var sendOptions = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(beforeClaim)
            .Options;

        await using var sendDb = new LearnerDbContext(sendOptions);
        var sendService = new NotificationCampaignService(
            sendDb,
            new FixedClock(Now),
            NullLogger<NotificationCampaignService>.Instance);

        var result = await sendService.SendAsync(campaignId, default);

        Assert.Equal(1, result.Delivered);
        sendDb.ChangeTracker.Clear();
        Assert.Equal(
            "sms-user",
            await sendDb.NotificationCampaignRecipients
                .Where(recipient => recipient.CampaignId == campaignId)
                .Select(recipient => recipient.RecipientUserId)
                .SingleAsync());
        Assert.Equal(
            NotificationChannel.Sms,
            await sendDb.NotificationCampaigns
                .Where(campaign => campaign.Id == campaignId)
                .Select(campaign => campaign.Channel)
                .SingleAsync());
    }

    [Fact]
    public async Task Send_FinalSaveFailureRollsBackAndSameServiceCanRetry()
    {
        var campaignId = Guid.NewGuid();
        var failOnce = new FailFinalCampaignSaveOnceInterceptor();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(failOnce)
            .Options;

        await using var db = new LearnerDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.NotificationCampaigns.Add(new NotificationCampaign
        {
            Id = campaignId,
            Name = "Retry campaign",
            Subject = "Subject",
            Body = "Body",
            Channel = NotificationChannel.Email,
            Status = NotificationCampaignStatus.Sending,
            SegmentJson = "{}",
            CreatedByAdminId = "admin",
            CreatedAt = Now.AddHours(-1),
            UpdatedAt = Now.AddHours(-1),
        });
        db.NotificationConsents.AddRange(Enumerable.Range(0, 3).Select(index =>
            new NotificationConsent
            {
                Id = Guid.NewGuid(),
                AuthAccountId = $"retry-user-{index}",
                Channel = NotificationChannel.Email,
                Category = "global",
                IsGranted = true,
                Source = "user",
                CreatedAt = Now.AddDays(-1),
                UpdatedAt = Now.AddDays(-1),
            }));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = new NotificationCampaignService(
            db,
            new FixedClock(Now),
            NullLogger<NotificationCampaignService>.Instance);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendAsync(campaignId, default));
        Assert.Equal("Injected final campaign save failure.", failure.Message);
        Assert.Empty(db.ChangeTracker.Entries<NotificationCampaign>());
        Assert.Empty(db.ChangeTracker.Entries<NotificationCampaignRecipient>());

        var retry = await service.SendAsync(campaignId, default);

        Assert.Equal(3, retry.Delivered);
        db.ChangeTracker.Clear();
        Assert.Equal(
            3,
            await db.NotificationCampaignRecipients.CountAsync(r => r.CampaignId == campaignId));
        Assert.Equal(
            NotificationCampaignStatus.Sent,
            await db.NotificationCampaigns
                .Where(c => c.Id == campaignId)
                .Select(c => c.Status)
                .SingleAsync());
    }

    private static async Task<SendOutcome> CaptureSendAsync(
        NotificationCampaignService service,
        Guid campaignId)
    {
        try
        {
            return new SendOutcome(await service.SendAsync(campaignId, default), null);
        }
        catch (Exception error)
        {
            return new SendOutcome(null, error);
        }
    }

    private sealed record SendOutcome(CampaignSendResult? Result, Exception? Error);

    private static NotificationConsent CreateConsent(string userId, NotificationChannel channel)
        => new()
        {
            Id = Guid.NewGuid(),
            AuthAccountId = userId,
            Channel = channel,
            Category = "global",
            IsGranted = true,
            Source = "user",
            CreatedAt = Now.AddDays(-1),
            UpdatedAt = Now.AddDays(-1),
        };

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SqlCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecipientBatchInterceptor : SaveChangesInterceptor
    {
        public List<int> RecipientBatchSizes { get; } = [];

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var batchSize = eventData.Context?.ChangeTracker
                .Entries<NotificationCampaignRecipient>()
                .Count(entry => entry.State == EntityState.Added) ?? 0;

            if (batchSize > 0)
            {
                RecipientBatchSizes.Add(batchSize);
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class CampaignReadGateInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"NotificationCampaigns\"", StringComparison.Ordinal)
                && Interlocked.Increment(ref _arrivals) <= 2)
            {
                if (Volatile.Read(ref _arrivals) == 2)
                {
                    _release.TrySetResult();
                }

                await _release.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }

    private sealed class CampaignUpdateGateInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _arrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _hasRun;

        public Task WaitUntilBlockedAsync(CancellationToken cancellationToken)
            => _arrived.Task.WaitAsync(cancellationToken);

        public void Release() => _release.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(
                    "UPDATE \"NotificationCampaigns\"",
                    StringComparison.Ordinal)
                && Interlocked.Exchange(ref _hasRun, 1) == 0)
            {
                _arrived.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }

    private sealed class FailFinalCampaignSaveOnceInterceptor : SaveChangesInterceptor
    {
        private int _hasFailed;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var isFinalCampaignSave = eventData.Context?.ChangeTracker
                .Entries<NotificationCampaign>()
                .Any(entry =>
                    entry.State == EntityState.Modified
                    && entry.Entity.Status == NotificationCampaignStatus.Sent) == true;

            if (isFinalCampaignSave && Interlocked.Exchange(ref _hasFailed, 1) == 0)
            {
                throw new InvalidOperationException("Injected final campaign save failure.");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class BeforeCampaignClaimInterceptor(
        NotificationChannel channel) : DbCommandInterceptor
    {
        private int _hasRun;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(
                    "UPDATE \"NotificationCampaigns\"",
                    StringComparison.Ordinal)
                && Interlocked.Exchange(ref _hasRun, 1) == 0)
            {
                await using var update = command.Connection!.CreateCommand();
                update.Transaction = command.Transaction;
                update.CommandText =
                    "UPDATE \"NotificationCampaigns\" SET \"Channel\" = @channel";

                var channelParameter = update.CreateParameter();
                channelParameter.ParameterName = "@channel";
                channelParameter.Value = (int)channel;
                update.Parameters.Add(channelParameter);

                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }
    }
}
