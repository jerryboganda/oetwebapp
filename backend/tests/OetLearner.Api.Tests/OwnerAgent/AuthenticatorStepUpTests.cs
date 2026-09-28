using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Endpoints;
using OetLearner.Api.Security;
using OetLearner.Api.Services;
using OetLearner.Api.Services.OwnerAgent;

namespace OetLearner.Api.Tests.OwnerAgent;

/// <summary>
/// <see cref="AuthService.VerifyAuthenticatorStepUpAsync"/> (password + TOTP step-up) and the
/// hardened authenticator re-enrolment, exercised against the real DI graph.
/// </summary>
public sealed class AuthenticatorStepUpTests
{
    private static async Task<ApiException> StepUpFailsAsync(OwnerAgentWebApplicationFactory factory, OwnerSeed owner, string? password, string code)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        return await Assert.ThrowsAsync<ApiException>(() =>
            auth.VerifyAuthenticatorStepUpAsync(OwnerAgentWebApplicationFactory.Principal(owner.AccountId, owner.FamilyId), password, code));
    }

    private static async Task<AuthenticatorStepUpResult> StepUpAsync(OwnerAgentWebApplicationFactory factory, OwnerSeed owner, string code)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
        return await auth.VerifyAuthenticatorStepUpAsync(
            OwnerAgentWebApplicationFactory.Principal(owner.AccountId, owner.FamilyId), owner.Password, code);
    }

    [Fact]
    public async Task StepUp_WithPasswordAndCurrentCode_Succeeds_AndRecordsTimeStep()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        var owner = await factory.SeedOwnerAsync();

        var result = await StepUpAsync(factory, owner, TestWebApplicationFactoryCodes.Now(owner));

        Assert.Equal(owner.AccountId, result.AccountId);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var recorded = await db.SecurityEvents.SingleAsync(e =>
            e.AuthAccountId == owner.AccountId && e.Kind == OwnerAgentSecurityEventKinds.StepUpSucceeded);
        Assert.Contains($"\"timeStep\":{result.TimeStep}", recorded.DetailsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StepUp_ReplayOfTheSameCode_IsRejected_ButTheNextCodeWorks()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        var owner = await factory.SeedOwnerAsync();
        var code = TestWebApplicationFactoryCodes.Now(owner);

        await StepUpAsync(factory, owner, code);
        var replay = await StepUpFailsAsync(factory, owner, owner.Password, code);
        Assert.Equal("authenticator_code_replayed", replay.ErrorCode);

        var next = await StepUpAsync(factory, owner, TestWebApplicationFactoryCodes.Next(owner));
        Assert.True(next.TimeStep > 0);
    }

    [Fact]
    public async Task StepUp_WrongPassword_IsRejected_AndCountedAsMfaFailure()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        var owner = await factory.SeedOwnerAsync();

        var failure = await StepUpFailsAsync(factory, owner, "definitely-not-the-password", TestWebApplicationFactoryCodes.Now(owner));

        Assert.Equal("invalid_step_up_credentials", failure.ErrorCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Equal(1, await db.SecurityEvents.CountAsync(e => e.AuthAccountId == owner.AccountId && e.Kind == SecurityEventKinds.AuthMfaFailed));
        Assert.False(await db.SecurityEvents.AnyAsync(e => e.Kind == OwnerAgentSecurityEventKinds.StepUpSucceeded));
    }

    [Fact]
    public async Task StepUp_MissingPassword_IsRejected()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        var owner = await factory.SeedOwnerAsync();

        var failure = await StepUpFailsAsync(factory, owner, password: null, TestWebApplicationFactoryCodes.Now(owner));
        Assert.Equal("password_required", failure.ErrorCode);
    }

    [Fact]
    public async Task StepUp_RecoveryCode_IsNotAccepted_AndIsNotConsumed()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        var owner = await factory.SeedOwnerAsync();
        var recoveryCode = owner.RecoveryCodes[0];

        var failure = await StepUpFailsAsync(factory, owner, owner.Password, recoveryCode);

        Assert.Equal("recovery_code_not_accepted", failure.ErrorCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var hash = AuthenticatorTotp.HashRecoveryCode(recoveryCode);
        var stored = await db.MfaRecoveryCodes.SingleAsync(c => c.ApplicationUserAccountId == owner.AccountId && c.CodeHash == hash);
        Assert.Null(stored.RedeemedAt);
    }

    [Fact]
    public async Task StepUp_RecoveryCodeShape_IsRejectedBeforeThePasswordCheck_SoItIsNoPasswordOracle()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        var owner = await factory.SeedOwnerAsync();

        // Same answer whether the password is right or wrong: a recovery-code-shaped "code"
        // can never be used to confirm a guessed password without the authenticator.
        var wrongPassword = await StepUpFailsAsync(factory, owner, "definitely-not-the-password", owner.RecoveryCodes[0]);
        var rightPassword = await StepUpFailsAsync(factory, owner, owner.Password, owner.RecoveryCodes[0]);

        Assert.Equal("recovery_code_not_accepted", wrongPassword.ErrorCode);
        Assert.Equal(wrongPassword.ErrorCode, rightPassword.ErrorCode);
        Assert.Equal(wrongPassword.StatusCode, rightPassword.StatusCode);
    }

    [Fact]
    public async Task StepUp_WithoutEnabledAuthenticator_IsRefused()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        var owner = await factory.SeedOwnerAsync(authenticatorEnabled: false);

        var failure = await StepUpFailsAsync(factory, owner, owner.Password, TestWebApplicationFactoryCodes.Now(owner));
        Assert.Equal("mfa_not_configured", failure.ErrorCode);
    }

    [Fact]
    public async Task StepUp_AfterFiveFailures_IsLockedOut_EvenWithCorrectFactors_AndEmailsOnce()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        var owner = await factory.SeedOwnerAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failure = await StepUpFailsAsync(factory, owner, "wrong-password-" + attempt, TestWebApplicationFactoryCodes.Now(owner));
            Assert.Equal("invalid_step_up_credentials", failure.ErrorCode);
        }

        var locked = await StepUpFailsAsync(factory, owner, owner.Password, TestWebApplicationFactoryCodes.Now(owner));
        Assert.Equal("mfa_attempts_exceeded", locked.ErrorCode);

        var lockoutEmails = factory.Emails.Messages
            .Where(m => m.To == owner.Email && m.TemplateKey == "security_alert")
            .ToList();
        Assert.Single(lockoutEmails);
    }

    [Fact]
    public async Task StepUp_DbBackedFailureCount_SurvivesAnInProcessCounterReset()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        var owner = await factory.SeedOwnerAsync();

        // Five historic failures persisted as SecurityEvents (e.g. recorded by the other
        // blue/green slot) — nothing in this process's memory cache.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            for (var i = 0; i < 5; i++)
            {
                db.SecurityEvents.Add(OwnerAgentSecurityEvents.Create(
                    owner.AccountId, SecurityEventKinds.AuthMfaFailed, DateTimeOffset.UtcNow.AddMinutes(-1), httpContext: null));
            }

            await db.SaveChangesAsync();
        }

        var locked = await StepUpFailsAsync(factory, owner, owner.Password, TestWebApplicationFactoryCodes.Now(owner));
        Assert.Equal("mfa_attempts_exceeded", locked.ErrorCode);
    }

    // ── Hardened re-enrolment ───────────────────────────────────────────────

    [Fact]
    public async Task Reenrolment_WithoutCurrentFactors_IsRefused_AndKeepsTheOldAuthenticator()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        var owner = await factory.SeedOwnerAsync();
        using var client = factory.CreateBearerClient(owner.AccessToken);

        var noBody = await client.PostAsync("/v1/auth/mfa/authenticator/begin", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, noBody.StatusCode);
        Assert.Equal("authenticator_reauthentication_required", await OwnerAgentWebApplicationFactory.ReadCodeAsync(noBody));

        var passwordOnly = await client.PostAsJsonAsync("/v1/auth/mfa/authenticator/begin",
            new AuthenticatorReenrolmentRequest(owner.Password, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, passwordOnly.StatusCode);

        var wrongPassword = await client.PostAsJsonAsync("/v1/auth/mfa/authenticator/begin",
            new AuthenticatorReenrolmentRequest("not-the-password", TestWebApplicationFactoryCodes.Now(owner), null));
        Assert.Equal(HttpStatusCode.BadRequest, wrongPassword.StatusCode);
        Assert.Equal("invalid_reauthentication", await OwnerAgentWebApplicationFactory.ReadCodeAsync(wrongPassword));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var account = await db.ApplicationUserAccounts.AsNoTracking().SingleAsync(a => a.Id == owner.AccountId);
        Assert.NotNull(account.AuthenticatorEnabledAt);
        Assert.False(await db.SecurityEvents.AnyAsync(e => e.Kind == OwnerAgentSecurityEventKinds.AuthenticatorReenrolled));
    }

    [Fact]
    public async Task Reenrolment_WithPasswordAndCurrentCode_Rotates_RecordsEvent_AndEmails()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        var owner = await factory.SeedOwnerAsync();
        using var client = factory.CreateBearerClient(owner.AccessToken);

        var response = await client.PostAsJsonAsync("/v1/auth/mfa/authenticator/begin",
            new AuthenticatorReenrolmentRequest(owner.Password, TestWebApplicationFactoryCodes.Now(owner), null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(owner.Password, body, StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.True(await db.SecurityEvents.AnyAsync(e =>
            e.AuthAccountId == owner.AccountId && e.Kind == OwnerAgentSecurityEventKinds.AuthenticatorReenrolled));
        Assert.Contains(factory.Emails.Messages, m => m.To == owner.Email && m.Subject.Contains("authenticator", StringComparison.OrdinalIgnoreCase));

        var unlock = scope.ServiceProvider.GetRequiredService<IOwnerAgentUnlockService>();
        Assert.NotNull(await unlock.GetUnlockBlockedUntilAsync(owner.AccountId, CancellationToken.None));
    }

    [Fact]
    public async Task Reenrolment_WithPasswordAndUnusedRecoveryCode_IsAllowed()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        var owner = await factory.SeedOwnerAsync();
        using var client = factory.CreateBearerClient(owner.AccessToken);

        var response = await client.PostAsJsonAsync("/v1/auth/mfa/authenticator/begin",
            new AuthenticatorReenrolmentRequest(owner.Password, null, owner.RecoveryCodes[1]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var reenrolled = await db.SecurityEvents.SingleAsync(e =>
            e.AuthAccountId == owner.AccountId && e.Kind == OwnerAgentSecurityEventKinds.AuthenticatorReenrolled);
        Assert.Contains("recovery_code", reenrolled.DetailsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reenrolment_RevokesExistingConsoleUnlock_AndBlocksANewOne()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        await factory.SetFeatureFlagAsync(true);
        var owner = await factory.SeedOwnerAsync();
        using var client = factory.CreateBearerClient(owner.AccessToken);
        var ticket = await OwnerAgentWebApplicationFactory.UnlockAsync(client, owner);

        var reenrol = await client.PostAsJsonAsync("/v1/auth/mfa/authenticator/begin",
            new AuthenticatorReenrolmentRequest(owner.Password, TestWebApplicationFactoryCodes.Next(owner), null));
        Assert.Equal(HttpStatusCode.OK, reenrol.StatusCode);

        using var status = OwnerAgentWebApplicationFactory.Unlocked(HttpMethod.Get, "/v1/owner-agent/status", ticket);
        var revoked = await client.SendAsync(status);
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        Assert.Equal(OwnerAgentFailureCodes.UnlockRevoked, await OwnerAgentWebApplicationFactory.ReadCodeAsync(revoked));

        var unlockAgain = await client.PostAsJsonAsync("/v1/owner-agent/unlock", new { password = owner.Password, code = "123456" });
        Assert.Equal(HttpStatusCode.Forbidden, unlockAgain.StatusCode);
        Assert.Equal("owner_agent_unlock_cooldown", await OwnerAgentWebApplicationFactory.ReadCodeAsync(unlockAgain));
    }

    [Fact]
    public async Task ConfirmSetup_WhenAlreadyEnabled_DoesNotRearmOrMoveTheEnabledTimestamp()
    {
        await using var factory = new OwnerAgentWebApplicationFactory();
        var owner = await factory.SeedOwnerAsync();
        DateTimeOffset? enabledBefore;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            enabledBefore = (await scope.ServiceProvider.GetRequiredService<LearnerDbContext>()
                .ApplicationUserAccounts.AsNoTracking().SingleAsync(a => a.Id == owner.AccountId)).AuthenticatorEnabledAt;
        }

        using var client = factory.CreateBearerClient(owner.AccessToken);
        var confirm = await client.PostAsJsonAsync("/v1/auth/mfa/authenticator/confirm",
            new OetLearner.Api.Contracts.ConfirmAuthenticatorSetupRequest(TestWebApplicationFactoryCodes.Now(owner)));
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        await using var verify = factory.Services.CreateAsyncScope();
        var after = await verify.ServiceProvider.GetRequiredService<LearnerDbContext>()
            .ApplicationUserAccounts.AsNoTracking().SingleAsync(a => a.Id == owner.AccountId);
        Assert.Equal(enabledBefore, after.AuthenticatorEnabledAt);
    }
}
