using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

/// <summary>
/// Regression coverage for device-trust OTP issuance integrity
/// (EmailOtpService.IssueChannelOtpLockedAsync, trust_device purpose).
///
/// Defect history: one logical verification attempt could produce many
/// delivered codes (remount/retry storms racing challenge creation), and a
/// request that died between provider-send and row-persist left a delivered
/// code with no live challenge — every code the learner typed then verified
/// as "invalid". These tests pin the closed behavior:
///   1. N concurrent identical requests produce ONE challenge and ONE send.
///   2. A failed send never orphans a delivered code: the next request
///      completes the SAME challenge and the delivered code verifies.
///   3. A persisted-but-unsent challenge (SentAt == null) is recovered by
///      completing its send, never by minting a duplicate challenge.
/// </summary>
public class DeviceTrustOtpRecoveryTests
{
    [Fact]
    public async Task ConcurrentDeviceTrustRequests_SendOnlyOneEmail_ForSameChallenge()
    {
        var dbName = $"recovery_concurrent_{Guid.NewGuid():N}";
        var sender = new ControllableEmailSender();
        var now = new MutableTimeProvider(new DateTimeOffset(2026, 03, 27, 12, 0, 0, TimeSpan.Zero));
        var authOptions = CreateAuthOptions();

        ApplicationUserAccount account;
        await using (var seedDb = new LearnerDbContext(CreateDbOptions(dbName)))
        {
            account = SeedAccount(seedDb, now.GetUtcNow(), "auth_recovery_concurrent_001");
            await seedDb.SaveChangesAsync();
        }

        // One service (own DbContext) per concurrent caller, all sharing the
        // same store — mirrors N app instances behind one database.
        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var db = new LearnerDbContext(CreateDbOptions(dbName));
            var service = new EmailOtpService(db, authOptions, sender, now);
            return await service.RequestDeviceTrustOtpAsync(account);
        }));

        Assert.Single(sender.SentMessages);
        Assert.Equal(10, responses.Length);
        Assert.Single(responses.Select(r => r.ChallengeId).Distinct());

        await using var assertDb = new LearnerDbContext(CreateDbOptions(dbName));
        var liveChallenges = await assertDb.EmailOtpChallenges
            .Where(x => x.ApplicationUserAccountId == account.Id
                && x.Purpose == EmailOtpService.DeviceTrustPurpose
                && x.VerifiedAt == null)
            .ToListAsync();
        Assert.Single(liveChallenges);
    }

    [Fact]
    public async Task FailedSend_DoesNotOrphanCode_NextRequestCompletesSameChallenge()
    {
        await using var harness = await CreateHarnessAsync();

        // The provider call blows up after the challenge row was already
        // persisted: nothing may reach the inbox from this attempt.
        harness.Sender.FailNextSend = true;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service.RequestDeviceTrustOtpAsync(harness.Account));
        Assert.Empty(harness.Sender.SentMessages);

        // The retry must complete the SAME challenge (not mint a second code)
        // and the delivered code must verify.
        var retry = await harness.Service.RequestDeviceTrustOtpAsync(harness.Account);
        Assert.Single(harness.Sender.SentMessages);

        await using var assertDb = new LearnerDbContext(harness.DbOptions);
        var completedRow = await assertDb.EmailOtpChallenges
            .Where(x => x.ApplicationUserAccountId == harness.Account.Id
                && x.Purpose == EmailOtpService.DeviceTrustPurpose)
            .SingleAsync();
        Assert.Equal(completedRow.Id.ToString(), retry.ChallengeId);
        Assert.NotNull(completedRow.SentAt);

        var deliveredCode = harness.Sender.SentMessages[0].TemplateParameters?["otpCode"] as string;
        Assert.False(string.IsNullOrWhiteSpace(deliveredCode));

        await harness.Service.VerifyDeviceTrustOtpAsync(harness.Account, deliveredCode!);

        assertDb.ChangeTracker.Clear();
        var liveChallenges = await assertDb.EmailOtpChallenges
            .Where(x => x.ApplicationUserAccountId == harness.Account.Id
                && x.Purpose == EmailOtpService.DeviceTrustPurpose
                && x.VerifiedAt == null)
            .ToListAsync();
        Assert.Empty(liveChallenges);
    }

    [Fact]
    public async Task UnsentChallengeRow_IsRecovered_NotDuplicated()
    {
        await using var harness = await CreateHarnessAsync();
        var now = harness.TimeProvider;

        // Simulate a previous request that persisted its row but died before
        // the email left: SentAt == null with a stale hash.
        var challengeId = Guid.NewGuid();
        await using (var seedDb = new LearnerDbContext(harness.DbOptions))
        {
            seedDb.EmailOtpChallenges.Add(new EmailOtpChallenge
            {
                Id = challengeId,
                ApplicationUserAccountId = harness.Account.Id,
                Purpose = EmailOtpService.DeviceTrustPurpose,
                CodeHash = EmailOtpService.HashOtp(challengeId, "000000", harness.Account.Id, EmailOtpService.DeviceTrustPurpose),
                AttemptCount = 0,
                CreatedAt = now.GetUtcNow().AddMinutes(-1),
                ExpiresAt = now.GetUtcNow().AddMinutes(9),
                Provider = EmailOtpProviders.BrevoEmail,
                DeliveryChannel = "email",
                DestinationHint = "l***@example.com",
                SentAt = null,
            });
            await seedDb.SaveChangesAsync();
        }

        var response = await harness.Service.RequestDeviceTrustOtpAsync(harness.Account);

        // Same challenge completed — no duplicate row, exactly one send.
        Assert.Equal(challengeId.ToString(), response.ChallengeId);
        Assert.Single(harness.Sender.SentMessages);

        await using var assertDb = new LearnerDbContext(harness.DbOptions);
        var rows = await assertDb.EmailOtpChallenges
            .Where(x => x.ApplicationUserAccountId == harness.Account.Id
                && x.Purpose == EmailOtpService.DeviceTrustPurpose)
            .ToListAsync();
        Assert.Single(rows);
        Assert.NotNull(rows[0].SentAt);

        var deliveredCode = harness.Sender.SentMessages[0].TemplateParameters?["otpCode"] as string;
        await harness.Service.VerifyDeviceTrustOtpAsync(harness.Account, deliveredCode!);
    }

    private static DbContextOptions<LearnerDbContext> CreateDbOptions(string dbName)
        => new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

    private static IOptions<AuthTokenOptions> CreateAuthOptions()
        => Options.Create(new AuthTokenOptions
        {
            OtpLifetime = TimeSpan.FromMinutes(10),
            AuthenticatorIssuer = "OET Learner"
        });

    private static ApplicationUserAccount SeedAccount(LearnerDbContext db, DateTimeOffset now, string id)
    {
        var account = new ApplicationUserAccount
        {
            Id = id,
            Email = "learner@example.com",
            NormalizedEmail = "LEARNER@EXAMPLE.COM",
            PasswordHash = "hashed-password",
            Role = ApplicationUserRoles.Learner,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ApplicationUserAccounts.Add(account);
        return account;
    }

    private static async Task<RecoveryHarness> CreateHarnessAsync()
    {
        var dbName = $"recovery_{Guid.NewGuid():N}";
        var dbOptions = CreateDbOptions(dbName);
        var sender = new ControllableEmailSender();
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 03, 27, 12, 0, 0, TimeSpan.Zero));

        await using (var seedDb = new LearnerDbContext(dbOptions))
        {
            SeedAccount(seedDb, timeProvider.GetUtcNow(), $"auth_recovery_{Guid.NewGuid():N}");
            await seedDb.SaveChangesAsync();
        }

        var serviceDb = new LearnerDbContext(dbOptions);
        // The service under test owns serviceDb for the life of the harness.
        var service = new EmailOtpService(serviceDb, CreateAuthOptions(), sender, timeProvider);
        var account = await serviceDb.ApplicationUserAccounts.SingleAsync(x => x.NormalizedEmail == "LEARNER@EXAMPLE.COM");

        return new RecoveryHarness(dbOptions, sender, timeProvider, service, account, serviceDb);
    }

    private sealed record RecoveryHarness(
        DbContextOptions<LearnerDbContext> DbOptions,
        ControllableEmailSender Sender,
        MutableTimeProvider TimeProvider,
        EmailOtpService Service,
        ApplicationUserAccount Account,
        LearnerDbContext ServiceDb) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await ServiceDb.DisposeAsync();
    }

    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _utcNow = start;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
    }

    private sealed class ControllableEmailSender : IEmailSender
    {
        private readonly List<EmailMessage> _sentMessages = [];

        public bool FailNextSend { get; set; }

        public IReadOnlyList<EmailMessage> SentMessages
        {
            get
            {
                lock (_sentMessages)
                {
                    return _sentMessages.ToArray();
                }
            }
        }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            lock (_sentMessages)
            {
                if (FailNextSend)
                {
                    FailNextSend = false;
                    throw new InvalidOperationException("SMTP transport unavailable.");
                }

                _sentMessages.Add(message);
            }

            return Task.CompletedTask;
        }
    }
}
