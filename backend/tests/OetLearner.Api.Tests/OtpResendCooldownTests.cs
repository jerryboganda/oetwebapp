using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

/// <summary>
/// Regression coverage for the hard, unconditional 60-second OTP resend
/// cooldown (EmailOtpService.EnforceResendCooldownOrThrow).
///
/// Before this fix, IssueChannelOtpLockedAsync (backing both
/// reset_password and trust_device) only checked the cooldown INSIDE the
/// "reuse a still-valid challenge" branch. Once a challenge exhausted
/// MaxOtpAttempts from wrong-code guesses (or expired), that branch no
/// longer applied and the very next resend request minted and sent a
/// brand-new code immediately — no cooldown check at all. These tests pin
/// the closed behavior: a resend attempt within 60s of the last actual
/// send must always be rejected, for every OTP purpose, regardless of
/// whether an existing challenge is still "reusable".
/// </summary>
public class OtpResendCooldownTests
{
    [Fact]
    public async Task PasswordReset_ExhaustingAttempts_StillEnforcesCooldown_DoesNotSendSecondCode()
    {
        await using var harness = CreateHarness();
        await harness.SeedLearnerAsync();

        var first = await harness.Service.RequestPasswordResetOtpAsync("learner@example.com");
        Assert.Single(harness.Sender.SentMessages);

        // Burn through every wrong-code attempt so the challenge is no
        // longer "reusable" (AttemptCount >= MaxOtpAttempts), all still
        // inside the 60s cooldown window since the first send.
        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<ApiException>(
                () => harness.Service.VerifyPasswordResetOtpAsync("learner@example.com", "000000"));
        }

        // The very next resend request, still within 60s of the first send,
        // must be rejected — not silently issue and send a second code.
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => harness.Service.RequestPasswordResetOtpAsync("learner@example.com"));
        Assert.Equal("otp_send_cooldown", ex.ErrorCode);
        Assert.Single(harness.Sender.SentMessages);

        // Past the cooldown, a fresh code is allowed.
        harness.TimeProvider.Advance(TimeSpan.FromSeconds(61));
        var replacement = await harness.Service.RequestPasswordResetOtpAsync("learner@example.com");
        Assert.NotEqual(first.ChallengeId, replacement.ChallengeId);
        Assert.Equal(2, harness.Sender.SentMessages.Count);
    }

    [Fact]
    public async Task DeviceTrust_ExhaustingAttempts_StillEnforcesCooldown_DoesNotSendSecondCode()
    {
        await using var harness = CreateHarness();
        var account = await harness.SeedLearnerAsync();

        var first = await harness.Service.RequestDeviceTrustOtpAsync(account);
        Assert.Single(harness.Sender.SentMessages);

        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<ApiException>(
                () => harness.Service.VerifyDeviceTrustOtpAsync(account, "000000"));
        }

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => harness.Service.RequestDeviceTrustOtpAsync(account));
        Assert.Equal("otp_send_cooldown", ex.ErrorCode);
        Assert.Single(harness.Sender.SentMessages);

        harness.TimeProvider.Advance(TimeSpan.FromSeconds(61));
        var replacement = await harness.Service.RequestDeviceTrustOtpAsync(account);
        Assert.NotEqual(first.ChallengeId, replacement.ChallengeId);
        Assert.Equal(2, harness.Sender.SentMessages.Count);
    }

    [Fact]
    public async Task EmailVerification_ResendWithinCooldown_IsRejected()
    {
        await using var harness = CreateHarness();
        await harness.SeedLearnerAsync();

        await harness.Service.RequestEmailVerificationOtpAsync("learner@example.com");
        Assert.Single(harness.Sender.SentMessages);

        // forceNew only skips handing back a still-valid code without
        // sending anything new — it must never skip the cooldown itself.
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => harness.Service.RequestEmailVerificationOtpAsync("learner@example.com", forceNew: true));
        Assert.Equal("otp_send_cooldown", ex.ErrorCode);
        Assert.Single(harness.Sender.SentMessages);
    }

    private static OtpCooldownHarness CreateHarness()
    {
        var dbOptions = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var sender = new RecordingEmailSender();
        var now = new MutableTimeProvider(new DateTimeOffset(2026, 03, 27, 12, 0, 0, TimeSpan.Zero));
        var authOptions = Options.Create(new AuthTokenOptions
        {
            OtpLifetime = TimeSpan.FromMinutes(10),
            AuthenticatorIssuer = "OET Learner"
        });
        // otpDelivery/runtimeSettings/firebaseSms deliberately omitted (null):
        // these tests exercise the Brevo-email path only, unrelated to the
        // Firebase/SMS orchestration already covered by FirebaseOtpTests.
        var service = new EmailOtpService(
            new LearnerDbContext(dbOptions),
            authOptions,
            sender,
            now);

        return new OtpCooldownHarness(dbOptions, sender, now, service);
    }

    private sealed record OtpCooldownHarness(
        DbContextOptions<LearnerDbContext> DbOptions,
        RecordingEmailSender Sender,
        MutableTimeProvider TimeProvider,
        EmailOtpService Service) : IAsyncDisposable
    {
        public async Task<ApplicationUserAccount> SeedLearnerAsync()
        {
            await using var db = new LearnerDbContext(DbOptions);
            var now = TimeProvider.GetUtcNow();
            var account = new ApplicationUserAccount
            {
                Id = "auth_learner_cooldown_001",
                Email = "learner@example.com",
                NormalizedEmail = "LEARNER@EXAMPLE.COM",
                PasswordHash = "hashed-password",
                Role = ApplicationUserRoles.Learner,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.ApplicationUserAccounts.Add(account);
            await db.SaveChangesAsync();
            return account;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _utcNow = start;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        private readonly List<EmailMessage> _sentMessages = [];

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
                _sentMessages.Add(message);
            }

            return Task.CompletedTask;
        }
    }
}
