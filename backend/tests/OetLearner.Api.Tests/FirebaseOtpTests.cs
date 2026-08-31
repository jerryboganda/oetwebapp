using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Otp;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Tests;

public class FirebaseOtpTests
{
    [Fact]
    public async Task PasswordReset_UsesFirebaseSms_WhenOrchestratorDelivers()
    {
        await using var harness = CreateHarness();
        await harness.SeedLearnerWithPhoneAsync();
        harness.Orchestrator.Next = new OtpSmsDeliveryResult(true, true, "+923001234567", "session-info-1");
        harness.Firebase.VerifyResult = new FirebaseSmsVerifyResult(true, "+923001234567", null);

        var challenge = await harness.Service.RequestPasswordResetOtpAsync(
            "learner@example.com", recaptchaToken: "recaptcha-token");

        Assert.Equal("sms", challenge.DeliveryChannel);
        Assert.Contains("567", challenge.DestinationHint);
        Assert.Empty(harness.Sender.SentMessages);
        Assert.Equal(1, harness.Orchestrator.CallCount);
        Assert.Equal("reset_password", harness.Orchestrator.LastPurpose);

        await using var db = new LearnerDbContext(harness.DbOptions);
        var stored = await db.EmailOtpChallenges.SingleAsync();
        Assert.Equal(EmailOtpProviders.FirebaseSms, stored.Provider);
        Assert.Equal("sms", stored.DeliveryChannel);
        Assert.Equal("session-info-1", stored.ExternalSessionInfoEncrypted);

        var account = await harness.Service.VerifyPasswordResetOtpAsync("learner@example.com", "654321");
        Assert.Equal("learner@example.com", account.Email);
        Assert.Null(account.EmailVerifiedAt);
        Assert.Equal(1, harness.Firebase.VerifyCallCount);
        Assert.Empty(harness.Sender.SentMessages);
    }

    [Fact]
    public async Task PasswordReset_FallsBackToBrevo_WhenFirebaseDoesNotDeliver()
    {
        await using var harness = CreateHarness();
        await harness.SeedLearnerWithPhoneAsync();
        harness.Orchestrator.Next = new OtpSmsDeliveryResult(true, false, "+923001234567", null);

        var challenge = await harness.Service.RequestPasswordResetOtpAsync(
            "learner@example.com", recaptchaToken: "recaptcha-token");

        Assert.Equal("email", challenge.DeliveryChannel);
        Assert.Equal("l*****@example.com", challenge.DestinationHint);
        Assert.Single(harness.Sender.SentMessages);
        Assert.Equal(1, harness.Orchestrator.CallCount);

        var code = harness.ExtractLatestOtpCode();
        var account = await harness.Service.VerifyPasswordResetOtpAsync("learner@example.com", code);
        Assert.Null(account.EmailVerifiedAt);
        Assert.Equal(0, harness.Firebase.VerifyCallCount);
    }

    [Fact]
    public async Task EmailVerification_NeverCallsFirebase()
    {
        await using var harness = CreateHarness();
        await harness.SeedLearnerWithPhoneAsync();
        harness.Orchestrator.ThrowIfCalled = true;
        harness.Firebase.ThrowIfCalled = true;

        var challenge = await harness.Service.RequestEmailVerificationOtpAsync("learner@example.com");

        Assert.Equal("email", challenge.DeliveryChannel);
        Assert.Single(harness.Sender.SentMessages);
        Assert.Equal(0, harness.Orchestrator.CallCount);
        Assert.Equal(0, harness.Firebase.SendCallCount);

        var code = harness.ExtractLatestOtpCode();
        var account = await harness.Service.VerifyEmailVerificationOtpAsync("learner@example.com", code);
        Assert.NotNull(account.EmailVerifiedAt);
    }

    [Fact]
    public async Task DeviceTrust_DoesNotSendTwoNumericDeliveries()
    {
        await using var harness = CreateHarness();
        var account = await harness.SeedLearnerWithPhoneAsync();
        harness.Orchestrator.Next = new OtpSmsDeliveryResult(true, true, "+923001234567", "session-info-2");

        var challenge = await harness.Service.RequestDeviceTrustOtpAsync(account, recaptchaToken: "recaptcha-token");

        Assert.Equal("sms", challenge.DeliveryChannel);
        Assert.Empty(harness.Sender.SentMessages);
        Assert.Equal(1, harness.Orchestrator.CallCount);
    }

    [Fact]
    public async Task PasswordReset_RapidRetry_ReusesChallengeWithoutAnotherDelivery()
    {
        await using var harness = CreateHarness();
        await harness.SeedLearnerWithPhoneAsync();

        var first = await harness.Service.RequestPasswordResetOtpAsync("learner@example.com");
        var retry = await harness.Service.RequestPasswordResetOtpAsync("learner@example.com");

        Assert.Equal(first.ChallengeId, retry.ChallengeId);
        Assert.Single(harness.Sender.SentMessages);
        Assert.Equal(1, harness.Orchestrator.CallCount);

        await using var db = new LearnerDbContext(harness.DbOptions);
        Assert.Single(await db.EmailOtpChallenges
            .Where(x => x.Purpose == EmailOtpService.PasswordResetPurpose)
            .ToListAsync());
    }

    [Fact]
    public async Task DeviceTrust_RapidRetry_ReusesChallengeWithoutAnotherDelivery()
    {
        await using var harness = CreateHarness();
        var account = await harness.SeedLearnerWithPhoneAsync();

        var first = await harness.Service.RequestDeviceTrustOtpAsync(account);
        var retry = await harness.Service.RequestDeviceTrustOtpAsync(account);

        Assert.Equal(first.ChallengeId, retry.ChallengeId);
        Assert.Single(harness.Sender.SentMessages);
        Assert.Equal(1, harness.Orchestrator.CallCount);

        await using var db = new LearnerDbContext(harness.DbOptions);
        Assert.Single(await db.EmailOtpChallenges
            .Where(x => x.Purpose == EmailOtpService.DeviceTrustPurpose)
            .ToListAsync());
    }

    [Fact]
    public async Task DeviceTrust_ConcurrentRequests_SendOnlyOneOtp()
    {
        await using var harness = CreateHarness();
        var account = await harness.SeedLearnerWithPhoneAsync();
        harness.Orchestrator.PauseDeliveries = true;
        var firstService = harness.CreateService();
        var secondService = harness.CreateService();

        var firstRequest = firstService.RequestDeviceTrustOtpAsync(account);
        await harness.Orchestrator.DeliveryStarted;
        var secondRequest = secondService.RequestDeviceTrustOtpAsync(account);
        harness.Orchestrator.ReleaseDeliveries();
        var responses = await Task.WhenAll(firstRequest, secondRequest);

        Assert.Equal(responses[0].ChallengeId, responses[1].ChallengeId);
        Assert.Single(harness.Sender.SentMessages);
        Assert.Equal(1, harness.Orchestrator.CallCount);

        await using var db = new LearnerDbContext(harness.DbOptions);
        Assert.Single(await db.EmailOtpChallenges
            .Where(x => x.Purpose == EmailOtpService.DeviceTrustPurpose)
            .ToListAsync());
    }

    [Fact]
    public async Task EmailVerification_ConcurrentRequests_SendOnlyOneOtp()
    {
        await using var harness = CreateHarness();
        await harness.SeedLearnerWithPhoneAsync();
        harness.Sender.PauseDeliveries = true;
        var firstService = harness.CreateService();
        var secondService = harness.CreateService();

        var firstRequest = firstService.RequestEmailVerificationOtpAsync("learner@example.com");
        await harness.Sender.DeliveryStarted;
        var secondRequest = secondService.RequestEmailVerificationOtpAsync("learner@example.com");
        harness.Sender.ReleaseDeliveries();
        var responses = await Task.WhenAll(firstRequest, secondRequest);

        Assert.Equal(responses[0].ChallengeId, responses[1].ChallengeId);
        Assert.Single(harness.Sender.SentMessages);
    }

    [Fact]
    public async Task PasswordReset_AfterCooldown_IssuesOneReplacementOtp()
    {
        await using var harness = CreateHarness();
        await harness.SeedLearnerWithPhoneAsync();

        var first = await harness.Service.RequestPasswordResetOtpAsync("learner@example.com");
        harness.TimeProvider.Advance(TimeSpan.FromSeconds(61));
        var replacement = await harness.Service.RequestPasswordResetOtpAsync("learner@example.com");

        Assert.NotEqual(first.ChallengeId, replacement.ChallengeId);
        Assert.Equal(2, harness.Sender.SentMessages.Count);
        Assert.Equal(2, harness.Orchestrator.CallCount);
    }

    [Theory]
    [InlineData("+923001234567", "+923001234567")]
    [InlineData("+92 300 1234567", "+923001234567")]
    [InlineData("00923001234567", "+923001234567")]
    [InlineData("03001234567", null)]
    [InlineData("", null)]
    public void PhoneNumberNormalizer_AcceptsOnlyInternationalNumbers(string raw, string? expected)
    {
        Assert.Equal(expected, PhoneNumberNormalizer.TryNormalize(raw));
    }

    [Fact]
    public async Task Orchestrator_SkipsSms_ForEmailVerificationPurpose()
    {
        await using var harness = CreateHarness();
        var account = await harness.SeedLearnerWithPhoneAsync();
        var orchestrator = new OtpDeliveryOrchestrator(
            new LearnerDbContext(harness.DbOptions),
            harness.Settings,
            harness.Firebase);

        var result = await orchestrator.TrySendFirebaseSmsAsync(
            account, EmailOtpService.EmailVerificationPurpose, "recaptcha-token");

        Assert.False(result.Attempted);
        Assert.False(result.Delivered);
        Assert.Equal(0, harness.Firebase.SendCallCount);
    }

    [Fact]
    public async Task Orchestrator_FallsBack_WhenFirebaseSendFails()
    {
        await using var harness = CreateHarness(enabled: true);
        var account = await harness.SeedLearnerWithPhoneAsync();
        harness.Firebase.SendResult = new FirebaseSmsSendResult(false, null, "quota");
        var orchestrator = new OtpDeliveryOrchestrator(
            new LearnerDbContext(harness.DbOptions),
            harness.Settings,
            harness.Firebase);

        var result = await orchestrator.TrySendFirebaseSmsAsync(
            account, EmailOtpService.PasswordResetPurpose, "recaptcha-token");

        Assert.True(result.Attempted);
        Assert.False(result.Delivered);
        Assert.Equal(1, harness.Firebase.SendCallCount);
    }

    private static FirebaseOtpHarness CreateHarness(bool enabled = false)
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
        var firebase = new FakeFirebaseSmsOtpClient();
        var orchestrator = new FakeOtpDeliveryOrchestrator();
        var settings = new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base() with
        {
            FirebaseOtp = new FirebaseOtpSettings(
                Enabled: enabled,
                SmsEnabled: true,
                EmailLinksEnabled: false,
                FallbackToBrevo: true,
                ProjectId: "oet-prep-learner",
                AuthDomain: "oet-prep-learner.firebaseapp.com",
                WebApiKey: "web-key")
        });
        EmailOtpService CreateService() => new(
            new LearnerDbContext(dbOptions),
            authOptions,
            sender,
            now,
            orchestrator,
            settings,
            firebase);

        return new FirebaseOtpHarness(dbOptions, sender, now, CreateService(), CreateService, orchestrator, firebase, settings);
    }

    private sealed record FirebaseOtpHarness(
        DbContextOptions<LearnerDbContext> DbOptions,
        RecordingEmailSender Sender,
        MutableTimeProvider TimeProvider,
        EmailOtpService Service,
        Func<EmailOtpService> CreateService,
        FakeOtpDeliveryOrchestrator Orchestrator,
        FakeFirebaseSmsOtpClient Firebase,
        TestRuntimeSettingsProvider Settings) : IAsyncDisposable
    {
        public async Task<ApplicationUserAccount> SeedLearnerWithPhoneAsync()
        {
            await using var db = new LearnerDbContext(DbOptions);
            var now = TimeProvider.GetUtcNow();
            var account = new ApplicationUserAccount
            {
                Id = "auth_learner_001",
                Email = "learner@example.com",
                NormalizedEmail = "LEARNER@EXAMPLE.COM",
                PasswordHash = "hashed-password",
                Role = ApplicationUserRoles.Learner,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.ApplicationUserAccounts.Add(account);
            db.Users.Add(new LearnerUser
            {
                Id = "learner_001",
                AuthAccountId = account.Id,
                Role = ApplicationUserRoles.Learner,
                DisplayName = "Learner One",
                Email = account.Email,
                CreatedAt = now,
                LastActiveAt = now
            });
            db.LearnerRegistrationProfiles.Add(new LearnerRegistrationProfile
            {
                Id = "signup_001",
                ApplicationUserAccountId = account.Id,
                LearnerUserId = "learner_001",
                FirstName = "Learner",
                LastName = "One",
                ExamTypeId = "oet",
                ProfessionId = "nursing",
                SessionId = string.Empty,
                CountryTarget = "Australia",
                MobileNumber = "+923001234567",
                AgreeToTerms = true,
                AgreeToPrivacy = true,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
            return account;
        }

        public string ExtractLatestOtpCode()
        {
            var match = System.Text.RegularExpressions.Regex.Match(Sender.SentMessages.Last().TextBody, @"\b\d{6}\b");
            Assert.True(match.Success);
            return match.Value;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _utcNow = start;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
    }

    private sealed class FakeOtpDeliveryOrchestrator : IOtpDeliveryOrchestrator
    {
        private int _callCount;
        private readonly TaskCompletionSource _deliveryStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseDeliveries = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public OtpSmsDeliveryResult Next { get; set; } = new(false, false, null, null);
        public bool ThrowIfCalled { get; set; }
        public bool PauseDeliveries { get; set; }
        public int CallCount => Volatile.Read(ref _callCount);
        public Task DeliveryStarted => _deliveryStarted.Task;
        public string? LastPurpose { get; private set; }

        public void ReleaseDeliveries() => _releaseDeliveries.TrySetResult();

        public async Task<OtpSmsDeliveryResult> TrySendFirebaseSmsAsync(
            ApplicationUserAccount account,
            string purpose,
            string? recaptchaToken,
            CancellationToken cancellationToken = default)
        {
            if (ThrowIfCalled)
            {
                throw new InvalidOperationException("Firebase SMS must not be attempted.");
            }

            Interlocked.Increment(ref _callCount);
            LastPurpose = purpose;
            _deliveryStarted.TrySetResult();
            if (PauseDeliveries)
            {
                await _releaseDeliveries.Task.WaitAsync(cancellationToken);
            }

            return Next;
        }
    }

    private sealed class FakeFirebaseSmsOtpClient : IFirebaseSmsOtpClient
    {
        public FirebaseSmsSendResult SendResult { get; set; } = new(false, null, "unused");
        public FirebaseSmsVerifyResult VerifyResult { get; set; } = new(false, null, "unused");
        public bool ThrowIfCalled { get; set; }
        public int SendCallCount { get; private set; }
        public int VerifyCallCount { get; private set; }

        public Task<FirebaseSmsSendResult> SendVerificationCodeAsync(
            string phoneNumber, string recaptchaToken, string webApiKey, CancellationToken cancellationToken = default)
        {
            if (ThrowIfCalled)
            {
                throw new InvalidOperationException("Firebase SMS must not be called.");
            }

            SendCallCount++;
            return Task.FromResult(SendResult);
        }

        public Task<FirebaseSmsVerifyResult> VerifyCodeAsync(
            string sessionInfo, string code, string webApiKey, CancellationToken cancellationToken = default)
        {
            if (ThrowIfCalled)
            {
                throw new InvalidOperationException("Firebase SMS must not be called.");
            }

            VerifyCallCount++;
            return Task.FromResult(VerifyResult);
        }
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        private readonly List<EmailMessage> _sentMessages = [];
        private readonly TaskCompletionSource _deliveryStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseDeliveries = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool PauseDeliveries { get; set; }
        public Task DeliveryStarted => _deliveryStarted.Task;

        public void ReleaseDeliveries() => _releaseDeliveries.TrySetResult();

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

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            lock (_sentMessages)
            {
                _sentMessages.Add(message);
            }

            _deliveryStarted.TrySetResult();
            if (PauseDeliveries)
            {
                await _releaseDeliveries.Task.WaitAsync(cancellationToken);
            }
        }
    }
}
