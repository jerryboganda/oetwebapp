using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

[CollectionDefinition("AuthFlows", DisableParallelization = true)]
public sealed class AuthFlowsCollectionDefinition
{
}

[Collection("AuthFlows")]
public class AuthFlowsTests
{
    [Fact]
    public async Task AuthEndpoints_SendVerificationOtpAfterRegistration_ReturnsChallenge()
    {
        await using var harness = CreateAuthApiHarness();

        var registerResponse = await harness.Client.PostAsJsonAsync("/v1/auth/register",
            CreateLearnerRegisterRequest());
        registerResponse.EnsureSuccessStatusCode();

        var response = await harness.Client.PostAsJsonAsync("/v1/auth/email/send-verification-otp",
            new SendEmailOtpRequest("learner@example.com", "verify_email"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var challenge = await response.Content.ReadFromJsonAsync<OtpChallengeResponse>(JsonSupport.Options);
        Assert.NotNull(challenge);
        Assert.Equal("verify_email", challenge!.Purpose);
        Assert.Equal("email", challenge.DeliveryChannel);
        Assert.Equal("l*****@example.com", challenge.DestinationHint);
        Assert.Single(harness.Sender.SentMessages);
    }

    [Fact]
    public async Task AuthEndpoints_VerifyEmailOtp_RejectsInvalidCode()
    {
        await using var harness = CreateAuthApiHarness();
        await RegisterLearnerAsync(harness.Client);

        var sendResponse = await harness.Client.PostAsJsonAsync("/v1/auth/email/send-verification-otp",
            new SendEmailOtpRequest("learner@example.com", "verify_email"));
        sendResponse.EnsureSuccessStatusCode();

        var response = await harness.Client.PostAsJsonAsync("/v1/auth/email/verify-otp",
            new VerifyEmailOtpRequest("learner@example.com", "verify_email", "000000"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_otp_code", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task AuthEndpoints_VerifyEmailOtp_RejectsExpiredCode()
    {
        await using var harness = CreateAuthApiHarness();
        await RegisterLearnerAsync(harness.Client);

        var sendResponse = await harness.Client.PostAsJsonAsync("/v1/auth/email/send-verification-otp",
            new SendEmailOtpRequest("learner@example.com", "verify_email"));
        sendResponse.EnsureSuccessStatusCode();

        var otpCode = harness.ExtractLatestOtpCode();
        harness.Advance(TimeSpan.FromMinutes(11));

        var response = await harness.Client.PostAsJsonAsync("/v1/auth/email/verify-otp",
            new VerifyEmailOtpRequest("learner@example.com", "verify_email", otpCode));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("expired_otp_code", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task AuthEndpoints_VerifyEmailOtp_MarksEmailVerified()
    {
        await using var harness = CreateAuthApiHarness();
        await RegisterLearnerAsync(harness.Client);

        var sendResponse = await harness.Client.PostAsJsonAsync("/v1/auth/email/send-verification-otp",
            new SendEmailOtpRequest("learner@example.com", "verify_email"));
        sendResponse.EnsureSuccessStatusCode();

        var otpCode = harness.ExtractLatestOtpCode();
        var response = await harness.Client.PostAsJsonAsync("/v1/auth/email/verify-otp",
            new VerifyEmailOtpRequest("learner@example.com", "verify_email", otpCode));

        response.EnsureSuccessStatusCode();
        var currentUser = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(JsonSupport.Options);
        Assert.NotNull(currentUser);
        Assert.True(currentUser!.IsEmailVerified);
        Assert.False(currentUser.RequiresEmailVerification);

        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var account = await db.ApplicationUserAccounts.SingleAsync(x => x.Email == "learner@example.com");
        Assert.NotNull(account.EmailVerifiedAt);
    }

    [Fact]
    public async Task AuthEndpoints_SessionFlow_SignInRefreshMeAndSignOut()
    {
        await using var harness = CreateAuthApiHarness();
        await RegisterLearnerAsync(harness.Client);

        var signInResponse = await harness.Client.PostAsJsonAsync("/v1/auth/sign-in",
            new PasswordSignInRequest("learner@example.com", "Password123!", true));
        signInResponse.EnsureSuccessStatusCode();
        var signInSession = await signInResponse.Content.ReadFromJsonAsync<AuthSessionResponse>(JsonSupport.Options);
        Assert.NotNull(signInSession);
        Assert.True(signInSession!.CurrentUser.RequiresEmailVerification);

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/v1/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signInSession.AccessToken);
        var meResponse = await harness.Client.SendAsync(meRequest);
        var meResponseBody = await meResponse.Content.ReadAsStringAsync();
        var wwwAuthenticate = string.Join(" | ", meResponse.Headers.WwwAuthenticate.Select(value => value.ToString()));
        Assert.True(
            meResponse.IsSuccessStatusCode,
            $"Status: {(int)meResponse.StatusCode} {meResponse.StatusCode}; WWW-Authenticate: {wwwAuthenticate}; Body: {meResponseBody}");
        var currentUser = await meResponse.Content.ReadFromJsonAsync<CurrentUserResponse>(JsonSupport.Options);
        Assert.NotNull(currentUser);
        Assert.Equal(signInSession.CurrentUser.UserId, currentUser!.UserId);

        var refreshResponse = await harness.Client.PostAsJsonAsync("/v1/auth/refresh",
            new RefreshTokenRequest(signInSession.RefreshToken));
        refreshResponse.EnsureSuccessStatusCode();
        var refreshedSession = await refreshResponse.Content.ReadFromJsonAsync<AuthSessionResponse>(JsonSupport.Options);
        Assert.NotNull(refreshedSession);
        Assert.NotEqual(signInSession.AccessToken, refreshedSession!.AccessToken);
        Assert.NotEqual(signInSession.RefreshToken, refreshedSession.RefreshToken);

        var signOutResponse = await harness.Client.PostAsJsonAsync("/v1/auth/sign-out",
            new SignOutRequest(refreshedSession.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, signOutResponse.StatusCode);

        var revokedRefreshResponse = await harness.Client.PostAsJsonAsync("/v1/auth/refresh",
            new RefreshTokenRequest(refreshedSession.RefreshToken));
        Assert.Equal(HttpStatusCode.Forbidden, revokedRefreshResponse.StatusCode);
        Assert.Equal("invalid_refresh_token", await ReadErrorCodeAsync(revokedRefreshResponse));
    }

    [Fact]
    public async Task AuthEndpoints_ForgotPasswordAndResetPassword_RotatesCredentialsAndRevokesSessions()
    {
        await using var harness = CreateAuthApiHarness();
        await RegisterLearnerAsync(harness.Client);

        var initialSignInResponse = await harness.Client.PostAsJsonAsync("/v1/auth/sign-in",
            new PasswordSignInRequest("learner@example.com", "Password123!", true));
        initialSignInResponse.EnsureSuccessStatusCode();
        var initialSession = await initialSignInResponse.Content.ReadFromJsonAsync<AuthSessionResponse>(JsonSupport.Options);
        Assert.NotNull(initialSession);

        var forgotPasswordResponse = await harness.Client.PostAsJsonAsync("/v1/auth/forgot-password",
            new ForgotPasswordRequest("learner@example.com"));

        Assert.Equal(HttpStatusCode.OK, forgotPasswordResponse.StatusCode);
        var challenge = await forgotPasswordResponse.Content.ReadFromJsonAsync<OtpChallengeResponse>(JsonSupport.Options);
        Assert.NotNull(challenge);
        Assert.Equal("reset_password", challenge!.Purpose);
        Assert.Equal("email", challenge.DeliveryChannel);
        Assert.Equal("l*****@example.com", challenge.DestinationHint);
        Assert.Single(harness.Sender.SentMessages);

        var resetCode = harness.ExtractLatestOtpCode();
        var resetPasswordResponse = await harness.Client.PostAsJsonAsync("/v1/auth/reset-password",
            new ResetPasswordRequest("learner@example.com", resetCode, "BetterPassword123!"));

        Assert.Equal(HttpStatusCode.NoContent, resetPasswordResponse.StatusCode);

        var revokedRefreshResponse = await harness.Client.PostAsJsonAsync("/v1/auth/refresh",
            new RefreshTokenRequest(initialSession!.RefreshToken));
        Assert.Equal(HttpStatusCode.Forbidden, revokedRefreshResponse.StatusCode);
        Assert.Equal("invalid_refresh_token", await ReadErrorCodeAsync(revokedRefreshResponse));

        var oldPasswordSignInResponse = await harness.Client.PostAsJsonAsync("/v1/auth/sign-in",
            new PasswordSignInRequest("learner@example.com", "Password123!", true));
        Assert.Equal(HttpStatusCode.BadRequest, oldPasswordSignInResponse.StatusCode);
        Assert.Equal("invalid_credentials", await ReadErrorCodeAsync(oldPasswordSignInResponse));

        var newPasswordSignInResponse = await harness.Client.PostAsJsonAsync("/v1/auth/sign-in",
            new PasswordSignInRequest("learner@example.com", "BetterPassword123!", true));
        newPasswordSignInResponse.EnsureSuccessStatusCode();
        var newSession = await newPasswordSignInResponse.Content.ReadFromJsonAsync<AuthSessionResponse>(JsonSupport.Options);
        Assert.NotNull(newSession);
    }

    [Fact]
    public async Task AuthEndpoints_ResetPassword_RejectsInvalidResetToken()
    {
        await using var harness = CreateAuthApiHarness();
        await RegisterLearnerAsync(harness.Client);

        var forgotPasswordResponse = await harness.Client.PostAsJsonAsync("/v1/auth/forgot-password",
            new ForgotPasswordRequest("learner@example.com"));
        forgotPasswordResponse.EnsureSuccessStatusCode();

        var resetPasswordResponse = await harness.Client.PostAsJsonAsync("/v1/auth/reset-password",
            new ResetPasswordRequest("learner@example.com", "000000", "BetterPassword123!"));

        Assert.Equal(HttpStatusCode.BadRequest, resetPasswordResponse.StatusCode);
        Assert.Equal("invalid_reset_token", await ReadErrorCodeAsync(resetPasswordResponse));
    }

    [Theory]
    [InlineData(ApplicationUserRoles.Expert)]
    [InlineData(ApplicationUserRoles.Admin)]
    public async Task AuthEndpoints_SignIn_BlocksPrivilegedAccountsUntilEmailVerified(string role)
    {
        await using var harness = CreateAuthApiHarness();
        await harness.SeedPrivilegedAccountAsync(role);

        var response = await harness.Client.PostAsJsonAsync("/v1/auth/sign-in",
            new PasswordSignInRequest($"{role}@example.com", "Password123!", true));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("email_verification_required", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task AuthEndpoints_SignIn_RejectsSuspendedLearnerAccounts()
    {
        await using var harness = CreateAuthApiHarness();
        await RegisterLearnerAsync(harness.Client);
        await SuspendLearnerAsync(harness, "learner@example.com");

        var response = await harness.Client.PostAsJsonAsync(
            "/v1/auth/sign-in",
            new PasswordSignInRequest("learner@example.com", "Password123!", true));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("account_suspended", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task AuthEndpoints_SignIn_RejectsDeletedLearnerAccounts()
    {
        await using var harness = CreateAuthApiHarness();
        await RegisterLearnerAsync(harness.Client);
        await DeleteLearnerAsync(harness, "learner@example.com");

        var response = await harness.Client.PostAsJsonAsync(
            "/v1/auth/sign-in",
            new PasswordSignInRequest("learner@example.com", "Password123!", true));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("account_deleted", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task AuthEndpoints_SuspendedBearerTokens_AreRejectedAtJwtValidation()
    {
        await using var harness = CreateAuthApiHarness();
        await RegisterLearnerAsync(harness.Client);

        var signInResponse = await harness.Client.PostAsJsonAsync(
            "/v1/auth/sign-in",
            new PasswordSignInRequest("learner@example.com", "Password123!", true));
        signInResponse.EnsureSuccessStatusCode();

        var session = await signInResponse.Content.ReadFromJsonAsync<AuthSessionResponse>(JsonSupport.Options);
        Assert.NotNull(session);

        await SuspendLearnerAsync(harness, "learner@example.com");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        var response = await harness.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthEndpoints_DeletedBearerTokens_AreRejectedAtJwtValidation()
    {
        await using var harness = CreateAuthApiHarness();
        await RegisterLearnerAsync(harness.Client);

        var signInResponse = await harness.Client.PostAsJsonAsync(
            "/v1/auth/sign-in",
            new PasswordSignInRequest("learner@example.com", "Password123!", true));
        signInResponse.EnsureSuccessStatusCode();

        var session = await signInResponse.Content.ReadFromJsonAsync<AuthSessionResponse>(JsonSupport.Options);
        Assert.NotNull(session);

        await DeleteLearnerAsync(harness, "learner@example.com");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        var response = await harness.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthEndpoints_SuspendedRefreshTokens_AreRejected()
    {
        await using var harness = CreateAuthApiHarness();
        await RegisterLearnerAsync(harness.Client);

        var signInResponse = await harness.Client.PostAsJsonAsync(
            "/v1/auth/sign-in",
            new PasswordSignInRequest("learner@example.com", "Password123!", true));
        signInResponse.EnsureSuccessStatusCode();

        var session = await signInResponse.Content.ReadFromJsonAsync<AuthSessionResponse>(JsonSupport.Options);
        Assert.NotNull(session);

        await SuspendLearnerAsync(harness, "learner@example.com");

        var refreshResponse = await harness.Client.PostAsJsonAsync(
            "/v1/auth/refresh",
            new RefreshTokenRequest(session!.RefreshToken));

        Assert.Equal(HttpStatusCode.Forbidden, refreshResponse.StatusCode);
        Assert.Equal("account_suspended", await ReadErrorCodeAsync(refreshResponse));
    }

    [Theory]
    [InlineData(ApplicationUserRoles.Expert, "/v1/expert/me")]
    [InlineData(ApplicationUserRoles.Admin, "/v1/admin/content")]
    public async Task AuthEndpoints_PrivilegedRoutes_RejectUnverifiedPrivilegedTokens(string role, string route)
    {
        await using var harness = CreateAuthApiHarness();
        var account = await harness.SeedPrivilegedAccountAsync(role);
        var accessToken = await harness.IssueAccessTokenAsync(account, isEmailVerified: false);

        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await harness.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuthEndpoints_Register_RejectsMalformedEmail()
    {
        await using var harness = CreateAuthApiHarness();

        var response = await harness.Client.PostAsJsonAsync("/v1/auth/register",
            CreateLearnerRegisterRequest(email: "not-an-email"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_email", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task AuthEndpoints_BeginAndConfirmAuthenticatorSetup_EnableMfaForVerifiedExpert()
    {
        await using var harness = CreateAuthApiHarness();
        await harness.SeedPrivilegedAccountAsync(ApplicationUserRoles.Expert, isEmailVerified: true);

        var signInResponse = await harness.Client.PostAsJsonAsync("/v1/auth/sign-in",
            new PasswordSignInRequest("expert@example.com", "Password123!", true));
        signInResponse.EnsureSuccessStatusCode();
        var signInSession = await signInResponse.Content.ReadFromJsonAsync<AuthSessionResponse>(JsonSupport.Options);
        Assert.NotNull(signInSession);
        Assert.False(signInSession!.CurrentUser.RequiresMfa);

        using var beginRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/mfa/authenticator/begin")
        {
            Content = JsonContent.Create(new BeginAuthenticatorSetupRequest())
        };
        beginRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signInSession.AccessToken);

        var beginResponse = await harness.Client.SendAsync(beginRequest);
        beginResponse.EnsureSuccessStatusCode();
        var setup = await beginResponse.Content.ReadFromJsonAsync<AuthenticatorSetupResponse>(JsonSupport.Options);
        Assert.NotNull(setup);
        Assert.False(string.IsNullOrWhiteSpace(setup!.SecretKey));
        Assert.Contains("otpauth://totp/", setup.OtpAuthUri, StringComparison.Ordinal);
        Assert.NotEmpty(setup.RecoveryCodes);

        var confirmCode = GenerateTotpCode(setup.SecretKey, harness.Now);
        using var confirmRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/mfa/authenticator/confirm")
        {
            Content = JsonContent.Create(new ConfirmAuthenticatorSetupRequest(confirmCode))
        };
        confirmRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signInSession.AccessToken);

        var confirmResponse = await harness.Client.SendAsync(confirmRequest);
        confirmResponse.EnsureSuccessStatusCode();
        var currentUser = await confirmResponse.Content.ReadFromJsonAsync<CurrentUserResponse>(JsonSupport.Options);
        Assert.NotNull(currentUser);
        Assert.True(currentUser!.IsAuthenticatorEnabled);
        Assert.False(currentUser.RequiresMfa);

        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var account = await db.ApplicationUserAccounts.SingleAsync(x => x.Email == "expert@example.com");
        Assert.NotNull(account.AuthenticatorEnabledAt);
        Assert.Equal(setup.RecoveryCodes.Count, await db.MfaRecoveryCodes.CountAsync(x => x.ApplicationUserAccountId == account.Id));
    }

    [Fact]
    public async Task AuthEndpoints_MfaChallenge_CompletesPrivilegedSignInAfterAuthenticatorSetup()
    {
        await using var harness = CreateAuthApiHarness();
        var setup = await EnableExpertAuthenticatorAsync(harness);

        var signInResponse = await harness.Client.PostAsJsonAsync("/v1/auth/sign-in",
            new PasswordSignInRequest("expert@example.com", "Password123!", true));

        Assert.Equal(HttpStatusCode.Forbidden, signInResponse.StatusCode);
        Assert.Equal("mfa_challenge_required", await ReadErrorCodeAsync(signInResponse));
        var challengeToken = await ReadStringPropertyAsync(signInResponse, "challengeToken");
        Assert.False(string.IsNullOrWhiteSpace(challengeToken));

        var challengeCode = GenerateTotpCode(setup.SecretKey, harness.Now);
        var challengeResponse = await harness.Client.PostAsJsonAsync("/v1/auth/mfa/challenge",
            new MfaChallengeRequest("expert@example.com", challengeCode, challengeToken, null));

        challengeResponse.EnsureSuccessStatusCode();
        var session = await challengeResponse.Content.ReadFromJsonAsync<AuthSessionResponse>(JsonSupport.Options);
        Assert.NotNull(session);
        Assert.True(session!.CurrentUser.IsAuthenticatorEnabled);
        Assert.False(session.CurrentUser.RequiresMfa);
    }

    [Fact]
    public async Task AuthEndpoints_MfaRecoveryCode_AllowsOneTimeSignIn()
    {
        await using var harness = CreateAuthApiHarness();
        var setup = await EnableExpertAuthenticatorAsync(harness);

        var signInResponse = await harness.Client.PostAsJsonAsync("/v1/auth/sign-in",
            new PasswordSignInRequest("expert@example.com", "Password123!", true));
        Assert.Equal(HttpStatusCode.Forbidden, signInResponse.StatusCode);
        var challengeToken = await ReadStringPropertyAsync(signInResponse, "challengeToken");

        var recoveryCode = setup.RecoveryCodes[0];
        var recoveryResponse = await harness.Client.PostAsJsonAsync("/v1/auth/mfa/recovery",
            new MfaChallengeRequest("expert@example.com", string.Empty, challengeToken, recoveryCode));
        recoveryResponse.EnsureSuccessStatusCode();

        var secondSignInResponse = await harness.Client.PostAsJsonAsync("/v1/auth/sign-in",
            new PasswordSignInRequest("expert@example.com", "Password123!", true));
        Assert.Equal(HttpStatusCode.Forbidden, secondSignInResponse.StatusCode);
        var secondChallengeToken = await ReadStringPropertyAsync(secondSignInResponse, "challengeToken");

        var reusedRecoveryResponse = await harness.Client.PostAsJsonAsync("/v1/auth/mfa/recovery",
            new MfaChallengeRequest("expert@example.com", string.Empty, secondChallengeToken, recoveryCode));

        Assert.Equal(HttpStatusCode.BadRequest, reusedRecoveryResponse.StatusCode);
        Assert.Equal("invalid_mfa_recovery_code", await ReadErrorCodeAsync(reusedRecoveryResponse));
    }

    [Fact]
    public async Task AuthEndpoints_DeleteAccount_ReturnsNoContentOnSuccess()
    {
        await using var harness = CreateAuthApiHarness();
        await RegisterLearnerAsync(harness.Client);

        var signInResponse = await harness.Client.PostAsJsonAsync("/v1/auth/sign-in",
            new PasswordSignInRequest("learner@example.com", "Password123!", true));
        signInResponse.EnsureSuccessStatusCode();
        var session = await signInResponse.Content.ReadFromJsonAsync<AuthSessionResponse>(JsonSupport.Options);
        Assert.NotNull(session);

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/account/delete")
        {
            Content = JsonContent.Create(new DeleteAccountRequest("Password123!", "No longer needed"))
        };
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        var deleteResponse = await harness.Client.SendAsync(deleteRequest);

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task AuthEndpoints_DeleteAccount_RejectsWrongPassword()
    {
        await using var harness = CreateAuthApiHarness();
        await RegisterLearnerAsync(harness.Client);

        var signInResponse = await harness.Client.PostAsJsonAsync("/v1/auth/sign-in",
            new PasswordSignInRequest("learner@example.com", "Password123!", true));
        signInResponse.EnsureSuccessStatusCode();
        var session = await signInResponse.Content.ReadFromJsonAsync<AuthSessionResponse>(JsonSupport.Options);
        Assert.NotNull(session);

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/account/delete")
        {
            Content = JsonContent.Create(new DeleteAccountRequest("WrongPassword!", null))
        };
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        var deleteResponse = await harness.Client.SendAsync(deleteRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, deleteResponse.StatusCode);
        Assert.Equal("invalid_password", await ReadErrorCodeAsync(deleteResponse));
    }

    [Fact]
    public async Task AuthEndpoints_DeleteAccount_SoftDeletesAccountAndRevokesTokens()
    {
        await using var harness = CreateAuthApiHarness();
        await RegisterLearnerAsync(harness.Client);

        var signInResponse = await harness.Client.PostAsJsonAsync("/v1/auth/sign-in",
            new PasswordSignInRequest("learner@example.com", "Password123!", true));
        signInResponse.EnsureSuccessStatusCode();
        var session = await signInResponse.Content.ReadFromJsonAsync<AuthSessionResponse>(JsonSupport.Options);
        Assert.NotNull(session);

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/account/delete")
        {
            Content = JsonContent.Create(new DeleteAccountRequest("Password123!"))
        };
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        var deleteResponse = await harness.Client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Verify soft-delete fields
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var account = await db.ApplicationUserAccounts.SingleAsync(x => x.Email == "learner@example.com");
        var learner = await db.Users.SingleAsync(x => x.AuthAccountId == account.Id);

        Assert.NotNull(account.DeletedAt);
        Assert.Equal("deleted", learner.AccountStatus);

        // Verify all refresh tokens revoked
        var activeTokens = await db.RefreshTokenRecords
            .Where(t => t.ApplicationUserAccountId == account.Id && t.RevokedAt == null)
            .CountAsync();
        Assert.Equal(0, activeTokens);

        // Verify sign-in is rejected
        var signInAfterDelete = await harness.Client.PostAsJsonAsync("/v1/auth/sign-in",
            new PasswordSignInRequest("learner@example.com", "Password123!", true));
        Assert.Equal(HttpStatusCode.Forbidden, signInAfterDelete.StatusCode);
        Assert.Equal("account_deleted", await ReadErrorCodeAsync(signInAfterDelete));
    }

    [Fact]
    public async Task AuthEndpoints_DeleteAccount_RejectsUnauthenticatedRequest()
    {
        await using var harness = CreateAuthApiHarness();

        var deleteResponse = await harness.Client.PostAsJsonAsync("/v1/auth/account/delete",
            new DeleteAccountRequest("Password123!"));

        Assert.Equal(HttpStatusCode.Unauthorized, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task AuthEndpoints_DeleteAccount_RejectsNonLearnerRole()
    {
        await using var harness = CreateAuthApiHarness();
        var seed = await harness.SeedPrivilegedAccountAsync(ApplicationUserRoles.Expert, isEmailVerified: true);
        var accessToken = await harness.IssueAccessTokenAsync(seed, isEmailVerified: true);

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/account/delete")
        {
            Content = JsonContent.Create(new DeleteAccountRequest("Password123!"))
        };
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var deleteResponse = await harness.Client.SendAsync(deleteRequest);

        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task SeedData_EnsuresUnifiedAuthAccountsForLearnerExpertAndAdmin()
    {
        using var factory = new TestWebApplicationFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var learner = await db.Users.SingleAsync(x => x.Id == "mock-user-001");
        var expert = await db.ExpertUsers.SingleAsync(x => x.Id == "expert-001");
        var secondaryExpert = await db.ExpertUsers.SingleAsync(x => x.Id == "expert-unauthorised");

        Assert.Equal(SeedData.LearnerAuthAccountId, learner.AuthAccountId);
        Assert.Equal(SeedData.ExpertAuthAccountId, expert.AuthAccountId);
        Assert.Equal(SeedData.ExpertSecondaryAuthAccountId, secondaryExpert.AuthAccountId);

        var learnerAccount = await db.ApplicationUserAccounts.SingleAsync(x => x.Id == SeedData.LearnerAuthAccountId);
        var expertAccount = await db.ApplicationUserAccounts.SingleAsync(x => x.Id == SeedData.ExpertAuthAccountId);
        var adminAccount = await db.ApplicationUserAccounts.SingleAsync(x => x.Id == SeedData.AdminAuthAccountId);
        var auditEvent = await db.AuditEvents.SingleAsync(x => x.Id == "aud-001");

        Assert.Equal(ApplicationUserRoles.Learner, learnerAccount.Role);
        Assert.Equal(ApplicationUserRoles.Expert, expertAccount.Role);
        Assert.Equal(ApplicationUserRoles.Admin, adminAccount.Role);
        Assert.NotNull(learnerAccount.EmailVerifiedAt);
        Assert.NotNull(expertAccount.EmailVerifiedAt);
        Assert.NotNull(adminAccount.EmailVerifiedAt);
        Assert.Equal(SeedData.AdminAuthAccountId, auditEvent.ActorAuthAccountId);
    }

    [Fact]
    public async Task AuthService_RegisterLearner_CreatesLinkedAccountAndReturnsSession()
    {
        var harness = CreateAuthServiceHarness();

        var session = await harness.Service.RegisterLearnerAsync(
            CreateLearnerRegisterRequest());

        Assert.False(string.IsNullOrWhiteSpace(session.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(session.RefreshToken));
        Assert.Equal(ApplicationUserRoles.Learner, session.CurrentUser.Role);
        Assert.Equal("learner@example.com", session.CurrentUser.Email);
        Assert.False(session.CurrentUser.IsEmailVerified);
        Assert.False(session.CurrentUser.IsAuthenticatorEnabled);

        var principal = harness.ValidateAccessToken(session.AccessToken);
        Assert.Equal(ApplicationUserRoles.Learner, principal.FindFirstValue(ClaimTypes.Role));
        Assert.Equal(session.CurrentUser.UserId, principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.False(string.IsNullOrWhiteSpace(principal.FindFirstValue(AuthTokenService.AuthAccountIdClaimType)));

        await using var readDb = new LearnerDbContext(harness.DbOptions);
        var account = await readDb.ApplicationUserAccounts.SingleAsync(x => x.Email == "learner@example.com");
        var learner = await readDb.Users.SingleAsync(x => x.AuthAccountId == account.Id);
        var registrationProfile = await readDb.LearnerRegistrationProfiles.SingleAsync(x => x.ApplicationUserAccountId == account.Id);
        Assert.Equal(ApplicationUserRoles.Learner, account.Role);
        Assert.Equal(learner.Id, session.CurrentUser.UserId);
        Assert.Equal("Learner One", learner.DisplayName);
        Assert.Equal("nursing", learner.ActiveProfessionId);
        Assert.Equal("Learner", registrationProfile.FirstName);
        Assert.Equal("One", registrationProfile.LastName);
        Assert.Equal("oet", registrationProfile.ExamTypeId);
        Assert.Equal("nursing", registrationProfile.ProfessionId);
        Assert.Equal("session-oet-nursing-apr", registrationProfile.SessionId);
        Assert.Equal("Australia", registrationProfile.CountryTarget);
        Assert.Equal("+923001234567", registrationProfile.MobileNumber);
        Assert.True(registrationProfile.AgreeToTerms);
        Assert.True(registrationProfile.AgreeToPrivacy);
        Assert.True(registrationProfile.MarketingOptIn);

        var verificationResult = harness.PasswordHasher.VerifyHashedPassword(account, account.PasswordHash, "Password123!");
        Assert.Equal(PasswordVerificationResult.Success, verificationResult);
        Assert.Single(await readDb.RefreshTokenRecords.Where(x => x.ApplicationUserAccountId == account.Id).ToListAsync());
    }

    [Fact]
    public async Task AuthService_GetSignupCatalog_ReturnsImportedCatalog()
    {
        var harness = CreateAuthServiceHarness();

        var catalog = await harness.Service.GetSignupCatalogAsync();

        Assert.Contains(catalog.ExamTypes, item => item.Id == "oet");
        Assert.Contains(catalog.ExamTypes, item => item.Id == "ielts");
        Assert.Contains(catalog.Professions, item => item.Id == "nursing" && item.ExamTypeIds.Contains("oet"));
        Assert.Contains(catalog.Sessions, item => item.Id == "session-oet-nursing-apr" && item.ProfessionIds.Contains("nursing"));
    }

    [Fact]
    public async Task AuthService_RegisterLearner_WithExternalRegistrationToken_LinksExternalIdentityAndMarksEmailVerified()
    {
        var harness = CreateAuthServiceHarness();
        var externalRegistrationToken = harness.ExternalAuthTicketService.CreateRegistrationToken(
            ExternalAuthProviders.Google,
            "google-sub-001",
            "learner@example.com",
            "Learner",
            "One",
            "/");

        var session = await harness.Service.RegisterLearnerAsync(
            CreateLearnerRegisterRequest(externalRegistrationToken: externalRegistrationToken));

        Assert.True(session.CurrentUser.IsEmailVerified);

        await using var readDb = new LearnerDbContext(harness.DbOptions);
        var account = await readDb.ApplicationUserAccounts.SingleAsync(x => x.Email == "learner@example.com");
        var externalIdentity = await readDb.ExternalIdentityLinks.SingleAsync(x => x.ApplicationUserAccountId == account.Id);

        Assert.NotNull(account.EmailVerifiedAt);
        Assert.Equal(ExternalAuthProviders.Google, externalIdentity.Provider);
        Assert.Equal("google-sub-001", externalIdentity.ProviderSubject);
        Assert.Equal("learner@example.com", externalIdentity.Email);
        Assert.Equal("Learner", externalIdentity.FirstName);
        Assert.Equal("One", externalIdentity.LastName);
    }

    [Fact]
    public async Task ExternalAuthService_Exchange_ReturnsSessionForLinkedLearner()
    {
        var harness = CreateAuthServiceHarness();
        await harness.SeedLearnerAsync();
        var exchangeToken = harness.ExternalAuthTicketService.CreateAuthenticatedExchangeToken(
            ExternalAuthProviders.Google,
            "auth_learner_001",
            "/");
        var service = CreateExternalAuthServiceHarness(harness);

        var response = await service.ExchangeAsync(
            ExternalAuthProviders.Google,
            new ExternalAuthExchangeRequest(exchangeToken));

        Assert.Equal(ExternalAuthExchangeKinds.Authenticated, response.Status);
        Assert.NotNull(response.Session);
        Assert.Null(response.Registration);
        Assert.True(response.Session!.CurrentUser.IsEmailVerified);
    }

    [Fact]
    public async Task ExternalAuthService_Exchange_ReturnsRegistrationPromptForNewLearner()
    {
        var harness = CreateAuthServiceHarness();
        var exchangeToken = harness.ExternalAuthTicketService.CreateRegistrationExchangeToken(
            ExternalAuthProviders.LinkedIn,
            "linkedin-sub-001",
            "social@example.com",
            "Social",
            "Learner",
            "/dashboard");
        var service = CreateExternalAuthServiceHarness(harness);

        var response = await service.ExchangeAsync(
            ExternalAuthProviders.LinkedIn,
            new ExternalAuthExchangeRequest(exchangeToken));

        Assert.Equal(ExternalAuthExchangeKinds.RegistrationRequired, response.Status);
        Assert.Null(response.Session);
        Assert.NotNull(response.Registration);
        Assert.Equal(ExternalAuthProviders.LinkedIn, response.Registration!.Provider);
        Assert.Equal("social@example.com", response.Registration.Email);
        Assert.Equal("Social", response.Registration.FirstName);
        Assert.Equal("Learner", response.Registration.LastName);
        Assert.Equal("/dashboard", response.Registration.NextPath);

        var registrationTicket = harness.ExternalAuthTicketService.ReadRegistrationToken(response.Registration.RegistrationToken);
        Assert.Equal(ExternalAuthProviders.LinkedIn, registrationTicket.Provider);
        Assert.Equal("linkedin-sub-001", registrationTicket.ProviderSubject);
        Assert.Equal("social@example.com", registrationTicket.Email);
        Assert.Equal("/dashboard", registrationTicket.NextPath);
    }

    [Fact]
    public void ExternalAuthTicketService_NormalizesSchemeRelativeNextPaths()
    {
        var harness = CreateAuthServiceHarness();

        var stateToken = harness.ExternalAuthTicketService.CreateStateToken(
            ExternalAuthProviders.Google,
            "//evil.example.test",
            null);
        var stateTicket = harness.ExternalAuthTicketService.ReadStateToken(ExternalAuthProviders.Google, stateToken);

        var registrationToken = harness.ExternalAuthTicketService.CreateRegistrationToken(
            ExternalAuthProviders.Google,
            "google-sub-001",
            "learner@example.com",
            null,
            null,
            "//evil.example.test");
        var registrationTicket = harness.ExternalAuthTicketService.ReadRegistrationToken(registrationToken);

        Assert.Null(stateTicket.NextPath);
        Assert.Null(registrationTicket.NextPath);
    }

    [Fact]
    public async Task AuthService_SignIn_ReturnsSessionAndUpdatesLastLogin()
    {
        var harness = CreateAuthServiceHarness();
        await harness.SeedLearnerAsync();

        var session = await harness.Service.SignInAsync(
            new PasswordSignInRequest("learner@example.com", "Password123!", RememberMe: true));

        Assert.False(string.IsNullOrWhiteSpace(session.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(session.RefreshToken));
        Assert.Equal(ApplicationUserRoles.Learner, session.CurrentUser.Role);

        await using var readDb = new LearnerDbContext(harness.DbOptions);
        var account = await readDb.ApplicationUserAccounts.SingleAsync();
        Assert.Equal(harness.Now, account.LastLoginAt);
        Assert.Single(await readDb.RefreshTokenRecords.Where(x => x.ApplicationUserAccountId == account.Id).ToListAsync());
    }

    [Fact]
    public async Task AuthService_Refresh_RotatesRefreshTokenAndRevokesPreviousToken()
    {
        var harness = CreateAuthServiceHarness();
        await harness.SeedLearnerAsync();

        var firstSession = await harness.Service.SignInAsync(
            new PasswordSignInRequest("learner@example.com", "Password123!", RememberMe: true));

        harness.Advance(TimeSpan.FromMinutes(5));
        var refreshedSession = await harness.Service.RefreshAsync(new RefreshTokenRequest(firstSession.RefreshToken));

        Assert.NotEqual(firstSession.AccessToken, refreshedSession.AccessToken);
        Assert.NotEqual(firstSession.RefreshToken, refreshedSession.RefreshToken);

        await using var readDb = new LearnerDbContext(harness.DbOptions);
        var tokens = await readDb.RefreshTokenRecords.OrderBy(x => x.CreatedAt).ToListAsync();
        Assert.Equal(2, tokens.Count);
        Assert.NotNull(tokens[0].RevokedAt);
        Assert.NotNull(tokens[0].LastUsedAt);
        Assert.Null(tokens[1].RevokedAt);
    }

    [Fact]
    public async Task AuthService_SignOut_RevokesRefreshToken()
    {
        var harness = CreateAuthServiceHarness();
        await harness.SeedLearnerAsync();

        var session = await harness.Service.SignInAsync(
            new PasswordSignInRequest("learner@example.com", "Password123!", RememberMe: true));

        await harness.Service.SignOutAsync(new SignOutRequest(session.RefreshToken));

        await using var readDb = new LearnerDbContext(harness.DbOptions);
        var token = await readDb.RefreshTokenRecords.SingleAsync();
        Assert.NotNull(token.RevokedAt);
    }

    [Fact]
    public async Task AuthService_GetCurrentUser_ResolvesFromAccessTokenClaims()
    {
        var harness = CreateAuthServiceHarness();
        await harness.SeedLearnerAsync();

        var session = await harness.Service.SignInAsync(
            new PasswordSignInRequest("learner@example.com", "Password123!", RememberMe: true));
        var principal = harness.ValidateAccessToken(session.AccessToken);

        var currentUser = await harness.Service.GetCurrentUserAsync(principal);

        Assert.Equal(session.CurrentUser.UserId, currentUser.UserId);
        Assert.Equal("learner@example.com", currentUser.Email);
        Assert.Equal(ApplicationUserRoles.Learner, currentUser.Role);
        Assert.Equal("Learner One", currentUser.DisplayName);
    }

    [Fact]
    public async Task EmailOtpService_RequestEmailVerificationOtp_CreatesTimeLimitedChallengeAndSendsEmail()
    {
        var harness = CreateEmailOtpHarness();
        await harness.SeedAccountAsync();

        var response = await harness.Service.RequestEmailVerificationOtpAsync("learner@example.com");

        Assert.Equal("verify_email", response.Purpose);
        Assert.Equal("email", response.DeliveryChannel);
        Assert.Equal("l*****@example.com", response.DestinationHint);
        Assert.Equal(harness.Now.AddMinutes(10), response.ExpiresAt);
        Assert.Equal(60, response.RetryAfterSeconds);

        Assert.Single(harness.Sender.SentMessages);
        var sentMessage = harness.Sender.SentMessages[0];
        Assert.Equal("learner@example.com", sentMessage.To);
        Assert.Contains("verify your email", sentMessage.Subject, StringComparison.OrdinalIgnoreCase);

        var otpMatch = Regex.Match(sentMessage.TextBody, @"\b\d{6}\b");
        Assert.True(otpMatch.Success);

        await using var readDb = new LearnerDbContext(harness.DbOptions);
        var challenge = await readDb.EmailOtpChallenges.SingleAsync();
        Assert.Equal("verify_email", challenge.Purpose);
        Assert.Equal(harness.AccountId, challenge.ApplicationUserAccountId);
        Assert.Equal(harness.Now.AddMinutes(10), challenge.ExpiresAt);
        Assert.NotEqual(otpMatch.Value, challenge.CodeHash);
        Assert.Null(challenge.VerifiedAt);
    }

    [Fact]
    public async Task EmailOtpService_RequestEmailVerificationOtp_ReplacesPendingChallengeOnRepeatRequest()
    {
        var harness = CreateEmailOtpHarness();
        await harness.SeedAccountAsync();

        var firstResponse = await harness.Service.RequestEmailVerificationOtpAsync("learner@example.com");
        harness.Advance(TimeSpan.FromMinutes(1));
        var secondResponse = await harness.Service.RequestEmailVerificationOtpAsync("learner@example.com");

        Assert.NotEqual(firstResponse.ChallengeId, secondResponse.ChallengeId);
        Assert.Equal(harness.Now.AddMinutes(10), secondResponse.ExpiresAt);
        Assert.Equal(2, harness.Sender.SentMessages.Count);

        await using var readDb = new LearnerDbContext(harness.DbOptions);
        var challenges = await readDb.EmailOtpChallenges.ToListAsync();
        Assert.Single(challenges);
        Assert.Equal(secondResponse.ChallengeId, challenges[0].Id.ToString());
    }

    [Fact]
    public async Task EmailOtpService_RequestEmailVerificationOtp_KeepsExistingChallengeWhenDeliveryFails()
    {
        var harness = CreateEmailOtpHarness();
        await harness.SeedAccountAsync();

        var firstResponse = await harness.Service.RequestEmailVerificationOtpAsync("learner@example.com");
        var failingService = harness.CreateService(new ThrowingEmailSender());

        await Assert.ThrowsAsync<InvalidOperationException>(() => failingService.RequestEmailVerificationOtpAsync("learner@example.com"));

        await using var readDb = new LearnerDbContext(harness.DbOptions);
        var challenges = await readDb.EmailOtpChallenges.ToListAsync();
        var challenge = Assert.Single(challenges);
        Assert.Equal(firstResponse.ChallengeId, challenge.Id.ToString());
    }

    [Theory]
    [MemberData(nameof(AuthRequestContractSamples))]
    public void AuthRequestContracts_SerializeAndDeserializeWithExpectedShape(object sample, string[] expectedProperties)
    {
        var contractType = sample.GetType();
        var json = JsonSupport.Serialize(sample);

        AssertJsonProperties(json, expectedProperties);

        var roundTripped = JsonSerializer.Deserialize(json, contractType, JsonSupport.Options);
        Assert.NotNull(roundTripped);
        Assert.Equal(json, JsonSupport.Serialize(roundTripped));
    }

    [Theory]
    [MemberData(nameof(AuthResponseContractSamples))]
    public void AuthResponseContracts_SerializeAndDeserializeWithExpectedShape(object sample, string[] expectedProperties)
    {
        var contractType = sample.GetType();
        var json = JsonSupport.Serialize(sample);

        AssertJsonProperties(json, expectedProperties);

        var roundTripped = JsonSerializer.Deserialize(json, contractType, JsonSupport.Options);
        Assert.NotNull(roundTripped);
        Assert.Equal(json, JsonSupport.Serialize(roundTripped));
    }

    [Fact]
    public async Task SeedData_EnsureDemoData_CreatesUnifiedAuthAccountsForLearnerExpertAndAdmin()
    {
        var dbOptions = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new LearnerDbContext(dbOptions);
        await SeedData.EnsureReferenceDataAsync(db);
        await SeedData.EnsureDemoDataAsync(db);

        var learnerAccount = await db.ApplicationUserAccounts.SingleAsync(x => x.Id == SeedData.LearnerAuthAccountId);
        var expertAccount = await db.ApplicationUserAccounts.SingleAsync(x => x.Id == SeedData.ExpertAuthAccountId);
        var adminAccount = await db.ApplicationUserAccounts.SingleAsync(x => x.Id == SeedData.AdminAuthAccountId);
        var learner = await db.Users.SingleAsync(x => x.Id == "mock-user-001");
        var expert = await db.ExpertUsers.SingleAsync(x => x.Id == "expert-001");

        Assert.Equal(learnerAccount.Id, learner.AuthAccountId);
        Assert.Equal(expertAccount.Id, expert.AuthAccountId);
        Assert.Equal(SeedData.AdminEmail, adminAccount.Email);
        Assert.NotNull(learnerAccount.EmailVerifiedAt);
        Assert.NotNull(expertAccount.EmailVerifiedAt);
        Assert.NotNull(adminAccount.EmailVerifiedAt);
    }

    public static IEnumerable<object[]> AuthRequestContractSamples()
    {
        yield return new object[]
        {
            CreateLearnerRegisterRequest(),
            new[]
            {
                "email",
                "password",
                "role",
                "displayName",
                "firstName",
                "lastName",
                "mobileNumber",
                "examTypeId",
                "professionId",
                "sessionId",
                "countryTarget",
                "agreeToTerms",
                "agreeToPrivacy",
                "marketingOptIn",
                "externalRegistrationToken",
            }
        };

        yield return new object[]
        {
            new PasswordSignInRequest("learner@example.com", "Password123!", true),
            new[] { "email", "password", "rememberMe" }
        };

        yield return new object[]
        {
            new RefreshTokenRequest("refresh-token-value"),
            new[] { "refreshToken" }
        };

        yield return new object[]
        {
            new SignOutRequest("refresh-token-value"),
            new[] { "refreshToken" }
        };

        yield return new object[]
        {
            new SendEmailOtpRequest("learner@example.com", "verify_email"),
            new[] { "email", "purpose" }
        };

        yield return new object[]
        {
            new VerifyEmailOtpRequest("learner@example.com", "verify_email", "123456"),
            new[] { "email", "purpose", "code" }
        };

        yield return new object[]
        {
            new BeginAuthenticatorSetupRequest(),
            Array.Empty<string>()
        };

        yield return new object[]
        {
            new ConfirmAuthenticatorSetupRequest("123456"),
            new[] { "code" }
        };

        yield return new object[]
        {
            new MfaChallengeRequest("learner@example.com", "123456", "challenge-token", null),
            new[] { "email", "code", "challengeToken", "recoveryCode" }
        };

        yield return new object[]
        {
            new ForgotPasswordRequest("learner@example.com"),
            new[] { "email" }
        };

        yield return new object[]
        {
            new ResetPasswordRequest("learner@example.com", "reset-token", "NewPassword123!"),
            new[] { "email", "resetToken", "newPassword" }
        };
    }

    public static IEnumerable<object[]> AuthResponseContractSamples()
    {
        var currentUser = new CurrentUserResponse(
            "auth_learner_001",
            "learner@example.com",
            "learner",
            "Learner One",
            true,
            true,
            false,
            false,
            new DateTimeOffset(2026, 03, 27, 12, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 03, 27, 13, 0, 0, TimeSpan.Zero));

        yield return new object[]
        {
            currentUser,
            new[] { "userId", "email", "role", "displayName", "isEmailVerified", "isAuthenticatorEnabled", "requiresEmailVerification", "requiresMfa", "emailVerifiedAt", "authenticatorEnabledAt", "adminPermissions" }
        };

        yield return new object[]
        {
            new AuthSessionResponse(
                "access-token",
                "refresh-token",
                new DateTimeOffset(2026, 03, 27, 12, 45, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 04, 26, 12, 45, 0, TimeSpan.Zero),
                currentUser),
            new[] { "accessToken", "refreshToken", "accessTokenExpiresAt", "refreshTokenExpiresAt", "currentUser" }
        };

        yield return new object[]
        {
            new OtpChallengeResponse(
                "otp-challenge-001",
                "verify_email",
                "email",
                "l*****@example.com",
                new DateTimeOffset(2026, 03, 27, 12, 55, 0, TimeSpan.Zero),
                60),
            new[] { "challengeId", "purpose", "deliveryChannel", "destinationHint", "expiresAt", "retryAfterSeconds" }
        };

        yield return new object[]
        {
            new AuthenticatorSetupResponse(
                "JBSWY3DPEHPK3PXP",
                "otpauth://totp/OET%20Learner:learner%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=OET%20Learner",
                "data:image/png;base64,AA==",
                new[] { "12345678", "23456789", "34567890" }),
            new[] { "secretKey", "otpAuthUri", "qrCodeDataUrl", "recoveryCodes" }
        };
    }

    [Fact]
    public async Task LearnerDbContext_PersistsAuthAccountAndRefreshTokenRoundTrip()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        var createdAt = new DateTimeOffset(2026, 03, 27, 12, 0, 0, TimeSpan.Zero);
        var account = new ApplicationUserAccount
        {
            Id = "auth_learner_001",
            Email = "learner@example.com",
            NormalizedEmail = "LEARNER@EXAMPLE.COM",
            PasswordHash = "hashed-password",
            Role = ApplicationUserRoles.Learner,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };

        var refreshToken = new RefreshTokenRecord
        {
            Id = Guid.NewGuid(),
            ApplicationUserAccountId = account.Id,
            TokenHash = "refresh-token-hash",
            ExpiresAt = createdAt.AddDays(30),
            CreatedAt = createdAt
        };

        account.RefreshTokens.Add(refreshToken);

        await using (var writeDb = new LearnerDbContext(options))
        {
            writeDb.ApplicationUserAccounts.Add(account);
            await writeDb.SaveChangesAsync();
        }

        await using var readDb = new LearnerDbContext(options);
        var persistedAccount = await readDb.ApplicationUserAccounts
            .Include(x => x.RefreshTokens)
            .SingleAsync(x => x.Id == account.Id);

        Assert.Equal(account.Email, persistedAccount.Email);
        Assert.Equal(account.NormalizedEmail, persistedAccount.NormalizedEmail);
        Assert.Equal(account.Role, persistedAccount.Role);

        var persistedToken = Assert.Single(persistedAccount.RefreshTokens);
        Assert.Equal(refreshToken.Id, persistedToken.Id);
        Assert.Equal(refreshToken.ApplicationUserAccountId, persistedToken.ApplicationUserAccountId);
        Assert.Equal(refreshToken.TokenHash, persistedToken.TokenHash);
        Assert.Equal(refreshToken.ExpiresAt, persistedToken.ExpiresAt);

        var persistedTokenRecord = await readDb.RefreshTokenRecords
            .SingleAsync(x => x.Id == refreshToken.Id);
        Assert.Equal(account.Id, persistedTokenRecord.ApplicationUserAccountId);
    }

    [Fact]
    public void LearnerDbContext_ModelContainsUnifiedAuthDomain()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        using var db = new LearnerDbContext(options);

        var account = AssertEntity<ApplicationUserAccount>(db);
        AssertProperty(account, nameof(ApplicationUserAccount.Id));
        AssertProperty(account, nameof(ApplicationUserAccount.Email));
        AssertProperty(account, nameof(ApplicationUserAccount.NormalizedEmail));
        AssertProperty(account, nameof(ApplicationUserAccount.PasswordHash));
        AssertProperty(account, nameof(ApplicationUserAccount.EmailVerifiedAt));
        AssertProperty(account, nameof(ApplicationUserAccount.AuthenticatorEnabledAt));
        AssertProperty(account, nameof(ApplicationUserAccount.LastLoginAt));
        AssertProperty(account, nameof(ApplicationUserAccount.Role));
        AssertUniqueIndex(account, nameof(ApplicationUserAccount.NormalizedEmail));
        AssertIndex(account, nameof(ApplicationUserAccount.NormalizedEmail), nameof(ApplicationUserAccount.Role));

        var refreshToken = AssertEntity<RefreshTokenRecord>(db);
        AssertProperty(refreshToken, nameof(RefreshTokenRecord.Id));
        AssertProperty(refreshToken, nameof(RefreshTokenRecord.ApplicationUserAccountId));
        AssertProperty(refreshToken, nameof(RefreshTokenRecord.TokenHash));
        Assert.Null(refreshToken.FindProperty("Token"));
        AssertIndex(refreshToken, nameof(RefreshTokenRecord.ApplicationUserAccountId), nameof(RefreshTokenRecord.ExpiresAt));

        var emailOtp = AssertEntity<EmailOtpChallenge>(db);
        AssertProperty(emailOtp, nameof(EmailOtpChallenge.Id));
        AssertProperty(emailOtp, nameof(EmailOtpChallenge.ApplicationUserAccountId));
        AssertProperty(emailOtp, nameof(EmailOtpChallenge.CodeHash));
        AssertProperty(emailOtp, nameof(EmailOtpChallenge.ExpiresAt));
        AssertIndex(emailOtp, nameof(EmailOtpChallenge.ApplicationUserAccountId), nameof(EmailOtpChallenge.Purpose), nameof(EmailOtpChallenge.ExpiresAt));

        var recoveryCode = AssertEntity<MfaRecoveryCode>(db);
        AssertProperty(recoveryCode, nameof(MfaRecoveryCode.Id));
        AssertProperty(recoveryCode, nameof(MfaRecoveryCode.ApplicationUserAccountId));
        AssertProperty(recoveryCode, nameof(MfaRecoveryCode.CodeHash));
        AssertProperty(recoveryCode, nameof(MfaRecoveryCode.RedeemedAt));
        AssertIndex(recoveryCode, nameof(MfaRecoveryCode.ApplicationUserAccountId));

        var learnerUser = AssertEntity<LearnerUser>(db);
        AssertProperty(learnerUser, nameof(LearnerUser.AuthAccountId));
        AssertUniqueIndex(learnerUser, nameof(LearnerUser.AuthAccountId));

        var expertUser = AssertEntity<ExpertUser>(db);
        AssertProperty(expertUser, nameof(ExpertUser.AuthAccountId));
        AssertUniqueIndex(expertUser, nameof(ExpertUser.AuthAccountId));

        var auditEvent = AssertEntity<AuditEvent>(db);
        AssertProperty(auditEvent, nameof(AuditEvent.ActorAuthAccountId));
    }

    private static IEntityType AssertEntity<TEntity>(LearnerDbContext db)
    {
        var entityType = db.Model.FindEntityType(typeof(TEntity));
        Assert.NotNull(entityType);
        return entityType!;
    }

    private static IProperty AssertProperty(IEntityType entityType, string propertyName)
    {
        var property = entityType.FindProperty(propertyName);
        Assert.NotNull(property);
        return property!;
    }

    private static void AssertUniqueIndex(IEntityType entityType, string propertyName)
    {
        var property = AssertProperty(entityType, propertyName);
        var uniqueIndex = entityType.GetIndexes().SingleOrDefault(index =>
            index.IsUnique &&
            index.Properties.Count == 1 &&
            index.Properties[0] == property);

        Assert.NotNull(uniqueIndex);
    }

    private static void AssertIndex(IEntityType entityType, params string[] propertyNames)
    {
        var properties = propertyNames.Select(propertyName => AssertProperty(entityType, propertyName)).ToArray();
        var index = entityType.GetIndexes().SingleOrDefault(candidate =>
            candidate.Properties.Count == properties.Length &&
            candidate.Properties.SequenceEqual(properties));

        Assert.NotNull(index);
    }

    private static void AssertJsonProperties(string json, IReadOnlyCollection<string> expectedProperties)
    {
        using var document = JsonDocument.Parse(json);
        var element = document.RootElement;

        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        var actualProperties = element.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(expectedProperties.Count, actualProperties.Length);
        Assert.Equal(expectedProperties.OrderBy(name => name), actualProperties.OrderBy(name => name));
    }

    private static EmailOtpHarness CreateEmailOtpHarness()
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

        return new EmailOtpHarness(dbOptions, sender, now, new EmailOtpService(
            new LearnerDbContext(dbOptions),
            authOptions,
            sender,
            now));
    }

    private static AuthApiHarness CreateAuthApiHarness()
    {
        var sender = new RecordingEmailSender();
        var timeProvider = new MutableTimeProvider(TimeProvider.System.GetUtcNow());
        var factory = new JwtAuthApiWebApplicationFactory(sender, timeProvider);
        var client = factory.CreateClient();
        return new AuthApiHarness(factory, client, sender, timeProvider);
    }

    private static AuthServiceHarness CreateAuthServiceHarness()
    {
        var dbOptions = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var now = new MutableTimeProvider(new DateTimeOffset(2026, 03, 27, 14, 0, 0, TimeSpan.Zero));
        var authOptions = Options.Create(new AuthTokenOptions
        {
            Issuer = "https://api.example.test",
            Audience = "oet-learner-web",
            AccessTokenSigningKey = "access-token-signing-key-12345678901234567890",
            RefreshTokenSigningKey = "refresh-token-signing-key-1234567890123456789",
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
            RefreshTokenLifetime = TimeSpan.FromDays(30),
            OtpLifetime = TimeSpan.FromMinutes(10),
            AuthenticatorIssuer = "OET Learner"
        });
        var passwordHasher = new PasswordHasher<ApplicationUserAccount>();
        var tokenService = new AuthTokenService(authOptions, now);
        var emailSender = new RecordingEmailSender();
        var emailOtpService = new EmailOtpService(new LearnerDbContext(dbOptions), authOptions, emailSender, now);
        var authBehaviorOptions = Options.Create(new AuthOptions { UseDevelopmentAuth = true });
        var externalAuthOptions = Options.Create(new ExternalAuthOptions());
        var environment = new TestWebHostEnvironment { EnvironmentName = "Development" };
        var dataProtectionProvider = DataProtectionProvider.Create("OetLearner.Api.AuthServiceHarness");
        var externalAuthTicketService = new ExternalAuthTicketService(dataProtectionProvider);
        using (var setupDb = new LearnerDbContext(dbOptions))
        {
            SeedData.EnsureReferenceDataAsync(setupDb).GetAwaiter().GetResult();
        }

        var service = new AuthService(
            new LearnerDbContext(dbOptions),
            passwordHasher,
            new PasswordPolicyService(
                new StubHttpClientFactory(),
                Options.Create(new PasswordPolicyOptions { BreachCheckEnabled = false }),
                NullLogger<PasswordPolicyService>.Instance),
            tokenService,
            emailOtpService,
            externalAuthTicketService,
            externalAuthOptions,
            authOptions,
            authBehaviorOptions,
            environment,
            dataProtectionProvider,
            new HttpContextAccessor(),
            now);

        return new AuthServiceHarness(
            dbOptions,
            now,
            authOptions,
            passwordHasher,
            tokenService,
            dataProtectionProvider,
            externalAuthTicketService,
            service);
    }

    private sealed record EmailOtpHarness(
        DbContextOptions<LearnerDbContext> DbOptions,
        RecordingEmailSender Sender,
        MutableTimeProvider TimeProvider,
        EmailOtpService Service)
    {
        public DateTimeOffset Now => TimeProvider.UtcNow;
        public string AccountId => "auth_learner_001";

        public async Task SeedAccountAsync()
        {
            await using var db = new LearnerDbContext(DbOptions);
            db.ApplicationUserAccounts.Add(new ApplicationUserAccount
            {
                Id = AccountId,
                Email = "learner@example.com",
                NormalizedEmail = "LEARNER@EXAMPLE.COM",
                PasswordHash = "hashed-password",
                Role = ApplicationUserRoles.Learner,
                CreatedAt = Now,
                UpdatedAt = Now
            });
            await db.SaveChangesAsync();
        }

        public void Advance(TimeSpan amount) => TimeProvider.Advance(amount);

        public EmailOtpService CreateService(IEmailSender sender)
            => new(new LearnerDbContext(DbOptions), Options.Create(new AuthTokenOptions
            {
                OtpLifetime = TimeSpan.FromMinutes(10),
                AuthenticatorIssuer = "OET Learner"
            }), sender, TimeProvider);
    }

    private sealed record AuthServiceHarness(
        DbContextOptions<LearnerDbContext> DbOptions,
        MutableTimeProvider TimeProvider,
        IOptions<AuthTokenOptions> AuthTokenOptions,
        PasswordHasher<ApplicationUserAccount> PasswordHasher,
        AuthTokenService TokenService,
        IDataProtectionProvider DataProtectionProvider,
        ExternalAuthTicketService ExternalAuthTicketService,
        AuthService Service)
    {
        public DateTimeOffset Now => TimeProvider.UtcNow;

        public void Advance(TimeSpan amount) => TimeProvider.Advance(amount);

        public async Task SeedLearnerAsync(
            string email = "learner@example.com",
            string password = "Password123!",
            string displayName = "Learner One")
        {
            var account = new ApplicationUserAccount
            {
                Id = "auth_learner_001",
                Email = email,
                NormalizedEmail = email.Trim().ToUpperInvariant(),
                Role = ApplicationUserRoles.Learner,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            account.PasswordHash = PasswordHasher.HashPassword(account, password);

            await using var db = new LearnerDbContext(DbOptions);
            db.ApplicationUserAccounts.Add(account);
            db.Users.Add(new LearnerUser
            {
                Id = "learner_001",
                AuthAccountId = account.Id,
                Role = ApplicationUserRoles.Learner,
                DisplayName = displayName,
                Email = email,
                CreatedAt = Now,
                LastActiveAt = Now
            });
            await db.SaveChangesAsync();
        }

        public ClaimsPrincipal ValidateAccessToken(string accessToken)
        {
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = AuthTokenOptions.Value.Issuer,
                ValidAudience = AuthTokenOptions.Value.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthTokenOptions.Value.AccessTokenSigningKey!)),
                ClockSkew = TimeSpan.Zero,
                NameClaimType = ClaimTypes.NameIdentifier,
                RoleClaimType = ClaimTypes.Role,
                LifetimeValidator = (notBefore, expires, _, _) =>
                    (!notBefore.HasValue || notBefore.Value <= Now.UtcDateTime)
                    && (!expires.HasValue || expires.Value > Now.UtcDateTime)
            };

            var handler = new JwtSecurityTokenHandler();
            return handler.ValidateToken(accessToken, validationParameters, out _);
        }
    }

    private static ExternalAuthService CreateExternalAuthServiceHarness(AuthServiceHarness harness)
        => new(
            new LearnerDbContext(harness.DbOptions),
            new StubExternalIdentityProviderClient(),
            harness.ExternalAuthTicketService,
            harness.Service,
            Options.Create(new PlatformOptions
            {
                PublicWebBaseUrl = "http://localhost:3000",
            }),
            harness.TimeProvider);

    private sealed record AuthApiHarness(
        JwtAuthApiWebApplicationFactory Factory,
        HttpClient Client,
        RecordingEmailSender Sender,
        MutableTimeProvider TimeProvider) : IAsyncDisposable
    {
        public DateTimeOffset Now => TimeProvider.UtcNow;

        public void Advance(TimeSpan amount) => TimeProvider.Advance(amount);

        public string ExtractLatestOtpCode()
        {
            var message = Sender.SentMessages.Last();
            var match = Regex.Match(message.TextBody, @"\b\d{6}\b");
            Assert.True(match.Success);
            return match.Value;
        }

        public async Task<PrivilegedAccountSeed> SeedPrivilegedAccountAsync(string role, bool isEmailVerified = false)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var passwordHasher = new PasswordHasher<ApplicationUserAccount>();
            var now = TimeProvider.UtcNow;
            var displayName = string.Equals(role, ApplicationUserRoles.Admin, StringComparison.Ordinal) ? "Admin One" : "Expert One";
            var userId = string.Empty;
            var account = new ApplicationUserAccount
            {
                Id = $"auth_{role}_{Guid.NewGuid():N}",
                Email = $"{role}@example.com",
                NormalizedEmail = $"{role}@example.com".ToUpperInvariant(),
                Role = role,
                EmailVerifiedAt = isEmailVerified ? now : null,
                CreatedAt = now,
                UpdatedAt = now
            };
            account.PasswordHash = passwordHasher.HashPassword(account, "Password123!");
            db.ApplicationUserAccounts.Add(account);

            if (string.Equals(role, ApplicationUserRoles.Expert, StringComparison.Ordinal))
            {
                userId = $"expert_{Guid.NewGuid():N}";
                db.ExpertUsers.Add(new ExpertUser
                {
                    Id = userId,
                    AuthAccountId = account.Id,
                    Role = ApplicationUserRoles.Expert,
                    DisplayName = displayName,
                    Email = account.Email,
                    CreatedAt = now
                });
            }
            else if (string.Equals(role, ApplicationUserRoles.Admin, StringComparison.Ordinal))
            {
                userId = account.Id;
            }
            else
            {
                throw new InvalidOperationException($"Unsupported privileged role '{role}'.");
            }

            await db.SaveChangesAsync();
            return new PrivilegedAccountSeed(role, account.Id, userId, account.Email, displayName);
        }

        public async Task<string> IssueAccessTokenAsync(PrivilegedAccountSeed account, bool isEmailVerified)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var tokenService = scope.ServiceProvider.GetRequiredService<AuthTokenService>();
            var subject = new AuthenticatedSessionSubject(
                account.UserId,
                account.AuthAccountId,
                account.Email,
                account.Role,
                account.DisplayName,
                isEmailVerified,
                IsAuthenticatorEnabled: false,
                RequiresEmailVerification: !isEmailVerified,
                RequiresMfa: true,
                EmailVerifiedAt: isEmailVerified ? TimeProvider.UtcNow : null,
                AuthenticatorEnabledAt: null);

            return tokenService.IssueSession(subject).AccessToken;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            Factory.Dispose();
            await Task.CompletedTask;
        }
    }

    private sealed class JwtAuthApiWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly Dictionary<string, string?> _previousValues;

        public JwtAuthApiWebApplicationFactory(RecordingEmailSender sender, MutableTimeProvider timeProvider)
        {
            Sender = sender;
            TimeProvider = timeProvider;

            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"InMemory:oet-learner-auth-api-tests-{Guid.NewGuid():N}",
                ["Auth:UseDevelopmentAuth"] = "false",
                ["Bootstrap:AutoMigrate"] = "false",
                ["Bootstrap:SeedDemoData"] = "false",
                ["Platform:PublicApiBaseUrl"] = "https://api.example.test",
                ["Platform:FallbackEmailDomain"] = "example.test",
                ["Billing:CheckoutBaseUrl"] = "https://app.example.test/billing/checkout",
                ["Storage:LocalRootPath"] = Path.Combine(Path.GetTempPath(), $"oet-learner-auth-api-storage-{Guid.NewGuid():N}"),
                ["Proxy:TrustForwardHeaders"] = "false",
                ["Proxy:EnforceHttps"] = "false",
                ["AuthTokens:Issuer"] = "https://api.example.test",
                ["AuthTokens:Audience"] = "oet-learner-web",
                ["AuthTokens:AccessTokenSigningKey"] = "access-token-signing-key-12345678901234567890",
                ["AuthTokens:RefreshTokenSigningKey"] = "refresh-token-signing-key-1234567890123456789",
                ["AuthTokens:AccessTokenLifetime"] = "00:15:00",
                ["AuthTokens:RefreshTokenLifetime"] = "30.00:00:00",
                ["AuthTokens:OtpLifetime"] = "00:10:00",
                ["AuthTokens:AuthenticatorIssuer"] = "OET Learner",
                ["Smtp:Enabled"] = "true",
                ["Smtp:Host"] = "smtp-relay.brevo.com",
                ["Smtp:Port"] = "587",
                ["Smtp:EnableSsl"] = "true",
                ["Smtp:Username"] = "brevo-login@example.test",
                ["Smtp:Password"] = "brevo-smtp-key-placeholder",
                ["Smtp:FromEmail"] = "no-reply@example.test",
                ["Smtp:FromName"] = "OET Learner"
            };

            _previousValues = settings.ToDictionary(
                entry => ToEnvironmentVariableName(entry.Key),
                entry => Environment.GetEnvironmentVariable(ToEnvironmentVariableName(entry.Key)));

            foreach (var (key, value) in settings)
            {
                Environment.SetEnvironmentVariable(ToEnvironmentVariableName(key), value);
            }
        }

        private RecordingEmailSender Sender { get; }
        private MutableTimeProvider TimeProvider { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(Sender);
                services.AddSingleton<IEmailSender>(Sender);
                services.AddSingleton<TimeProvider>(TimeProvider);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing)
            {
                return;
            }

            foreach (var (key, value) in _previousValues)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        private static string ToEnvironmentVariableName(string configurationKey)
            => configurationKey.Replace(":", "__", StringComparison.Ordinal);
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<EmailMessage> SentMessages { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            SentMessages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingEmailSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated delivery failure.");
    }

    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _utcNow = start;

        public DateTimeOffset UtcNow => _utcNow;

        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long GetTimestamp() => (long)(_utcNow - DateTimeOffset.UnixEpoch).TotalMilliseconds;
    }

    private sealed class StubExternalIdentityProviderClient : IExternalIdentityProviderClient
    {
        public Uri BuildAuthorizationUri(string provider, string state, string redirectUri)
            => new($"https://example.test/oauth/{provider}?state={Uri.EscapeDataString(state)}");

        public Task<ExternalIdentityProfile> ExchangeCodeAsync(
            string provider,
            string code,
            string redirectUri,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test stub does not perform provider code exchanges.");
    }

    private sealed record PrivilegedAccountSeed(
        string Role,
        string AuthAccountId,
        string UserId,
        string Email,
        string DisplayName);

    private static async Task RegisterLearnerAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/v1/auth/register",
            CreateLearnerRegisterRequest());
        response.EnsureSuccessStatusCode();
    }

    private static RegisterRequest CreateLearnerRegisterRequest(
        string email = "learner@example.com",
        string password = "Password123!",
        string displayName = "Learner One",
        string firstName = "Learner",
        string lastName = "One",
        string mobileNumber = "+923001234567",
        string examTypeId = "oet",
        string professionId = "nursing",
        string sessionId = "session-oet-nursing-apr",
        string countryTarget = "Australia",
        bool agreeToTerms = true,
        bool agreeToPrivacy = true,
        bool marketingOptIn = true,
        string? externalRegistrationToken = null)
        => new(
            email,
            password,
            ApplicationUserRoles.Learner,
            displayName,
            firstName,
            lastName,
            mobileNumber,
            examTypeId,
            professionId,
            sessionId,
            countryTarget,
            agreeToTerms,
            agreeToPrivacy,
            marketingOptIn,
            externalRegistrationToken);

    private static async Task SuspendLearnerAsync(AuthApiHarness harness, string email)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var account = await db.ApplicationUserAccounts.SingleAsync(x => x.Email == email);
        var learner = await db.Users.SingleAsync(x => x.AuthAccountId == account.Id);
        learner.AccountStatus = "suspended";
        await db.SaveChangesAsync();
    }

    private static async Task DeleteLearnerAsync(AuthApiHarness harness, string email)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var account = await db.ApplicationUserAccounts.SingleAsync(x => x.Email == email);
        var learner = await db.Users.SingleAsync(x => x.AuthAccountId == account.Id);
        account.DeletedAt = harness.Now;
        account.UpdatedAt = harness.Now;
        learner.AccountStatus = "deleted";
        await db.SaveChangesAsync();
    }

    private static async Task<AuthenticatorSetupResponse> EnableExpertAuthenticatorAsync(AuthApiHarness harness)
    {
        await harness.SeedPrivilegedAccountAsync(ApplicationUserRoles.Expert, isEmailVerified: true);

        var signInResponse = await harness.Client.PostAsJsonAsync("/v1/auth/sign-in",
            new PasswordSignInRequest("expert@example.com", "Password123!", true));
        signInResponse.EnsureSuccessStatusCode();
        var signInSession = await signInResponse.Content.ReadFromJsonAsync<AuthSessionResponse>(JsonSupport.Options);
        Assert.NotNull(signInSession);

        using var beginRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/mfa/authenticator/begin")
        {
            Content = JsonContent.Create(new BeginAuthenticatorSetupRequest())
        };
        beginRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signInSession!.AccessToken);
        var beginResponse = await harness.Client.SendAsync(beginRequest);
        beginResponse.EnsureSuccessStatusCode();
        var setup = await beginResponse.Content.ReadFromJsonAsync<AuthenticatorSetupResponse>(JsonSupport.Options);
        Assert.NotNull(setup);

        using var confirmRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/mfa/authenticator/confirm")
        {
            Content = JsonContent.Create(new ConfirmAuthenticatorSetupRequest(GenerateTotpCode(setup!.SecretKey, harness.Now)))
        };
        confirmRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signInSession.AccessToken);
        var confirmResponse = await harness.Client.SendAsync(confirmRequest);
        confirmResponse.EnsureSuccessStatusCode();

        return setup;
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var codeElement)
            ? codeElement.GetString()
            : null;
    }

    private static async Task<string?> ReadStringPropertyAsync(HttpResponseMessage response, string propertyName)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty(propertyName, out var element)
            ? element.GetString()
            : null;
    }

    private static string GenerateTotpCode(string secretKey, DateTimeOffset timestamp)
    {
        var key = DecodeBase32(secretKey);
        var timestep = timestamp.ToUnixTimeSeconds() / 30;
        Span<byte> counter = stackalloc byte[8];
        for (var index = 7; index >= 0; index--)
        {
            counter[index] = (byte)(timestep & 0xFF);
            timestep >>= 8;
        }

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counter.ToArray());
        var offset = hash[^1] & 0x0F;
        var binaryCode = ((hash[offset] & 0x7F) << 24)
                         | (hash[offset + 1] << 16)
                         | (hash[offset + 2] << 8)
                         | hash[offset + 3];

        return (binaryCode % 1_000_000).ToString("D6");
    }

    private static byte[] DecodeBase32(string value)
    {
        var cleaned = value.Trim().TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>();
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var character in cleaned)
        {
            var digit = character switch
            {
                >= 'A' and <= 'Z' => character - 'A',
                >= '2' and <= '7' => character - '2' + 26,
                _ => throw new FormatException("Invalid base32 character.")
            };

            buffer = (buffer << 5) | digit;
            bitsLeft += 5;

            if (bitsLeft < 8)
            {
                continue;
            }

            output.Add((byte)(buffer >> (bitsLeft - 8)));
            bitsLeft -= 8;
            buffer &= (1 << bitsLeft) - 1;
        }

        return output.ToArray();
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }
}
