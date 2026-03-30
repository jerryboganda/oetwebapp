using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public sealed class ExternalAuthService(
    LearnerDbContext db,
    IExternalIdentityProviderClient providerClient,
    ExternalAuthTicketService ticketService,
    AuthService authService,
    IOptions<PlatformOptions> platformOptions,
    TimeProvider timeProvider)
{
    private readonly PlatformOptions _platformOptions = platformOptions.Value;

    public Uri BuildAuthorizationRedirect(string provider, string? nextPath)
    {
        var normalizedProvider = NormalizeProvider(provider);
        var state = ticketService.CreateStateToken(normalizedProvider, nextPath);
        return providerClient.BuildAuthorizationUri(
            normalizedProvider,
            state,
            BuildBackendCallbackUrl(normalizedProvider));
    }

    public async Task<Uri> CompleteCallbackAsync(
        string provider,
        string? code,
        string? state,
        string? providerError,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = NormalizeProvider(provider);

        try
        {
            if (!string.IsNullOrWhiteSpace(providerError))
            {
                return BuildFrontendErrorRedirect("external_auth_cancelled");
            }

            var stateTicket = ticketService.ReadStateToken(normalizedProvider, state ?? string.Empty);
            var profile = await providerClient.ExchangeCodeAsync(
                normalizedProvider,
                code ?? string.Empty,
                BuildBackendCallbackUrl(normalizedProvider),
                cancellationToken);

            var linkedAccount = await ResolveLinkedLearnerAccountAsync(profile, cancellationToken);
            if (linkedAccount is not null)
            {
                var exchangeToken = ticketService.CreateAuthenticatedExchangeToken(
                    normalizedProvider,
                    linkedAccount.Id,
                    stateTicket.NextPath);

                return BuildFrontendCallbackRedirect(normalizedProvider, exchangeToken, stateTicket.NextPath);
            }

            var registrationToken = ticketService.CreateRegistrationExchangeToken(
                normalizedProvider,
                profile.Subject,
                profile.Email,
                profile.FirstName,
                profile.LastName,
                stateTicket.NextPath);

            return BuildFrontendCallbackRedirect(normalizedProvider, registrationToken, stateTicket.NextPath);
        }
        catch (ApiException apiException)
        {
            return BuildFrontendErrorRedirect(apiException.ErrorCode);
        }
        catch
        {
            return BuildFrontendErrorRedirect("external_auth_failed");
        }
    }

    public async Task<ExternalAuthExchangeResponse> ExchangeAsync(
        string provider,
        ExternalAuthExchangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = NormalizeProvider(provider);
        var exchangeTicket = ticketService.ReadExchangeToken(normalizedProvider, request.ExchangeToken);

        if (string.Equals(exchangeTicket.Kind, ExternalAuthExchangeKinds.Authenticated, StringComparison.Ordinal))
        {
            var session = await authService.CompleteDirectSignInAsync(
                exchangeTicket.AccountId ?? throw new InvalidOperationException("Exchange ticket did not include an account id."),
                markEmailVerified: true,
                cancellationToken);

            return new ExternalAuthExchangeResponse(
                Status: ExternalAuthExchangeKinds.Authenticated,
                Session: session,
                Registration: null);
        }

        var registrationToken = ticketService.CreateRegistrationToken(
            normalizedProvider,
            exchangeTicket.ProviderSubject ?? throw new InvalidOperationException("Registration ticket did not include a provider subject."),
            exchangeTicket.Email ?? throw new InvalidOperationException("Registration ticket did not include an email."),
            exchangeTicket.FirstName,
            exchangeTicket.LastName,
            exchangeTicket.NextPath);

        return new ExternalAuthExchangeResponse(
            Status: ExternalAuthExchangeKinds.RegistrationRequired,
            Session: null,
            Registration: new ExternalRegistrationPromptResponse(
                registrationToken,
                normalizedProvider,
                exchangeTicket.Email!,
                exchangeTicket.FirstName,
                exchangeTicket.LastName,
                exchangeTicket.NextPath));
    }

    private async Task<ApplicationUserAccount?> ResolveLinkedLearnerAccountAsync(
        ExternalIdentityProfile profile,
        CancellationToken cancellationToken)
    {
        var existingLink = await db.ExternalIdentityLinks
            .Include(link => link.ApplicationUserAccount)
            .SingleOrDefaultAsync(
                link => link.Provider == profile.Provider && link.ProviderSubject == profile.Subject,
                cancellationToken);

        if (existingLink is not null)
        {
            existingLink.LastSignedInAt = timeProvider.GetUtcNow();
            existingLink.UpdatedAt = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            return existingLink.ApplicationUserAccount;
        }

        var normalizedEmail = profile.Email.Trim().ToUpperInvariant();
        var existingAccount = await db.ApplicationUserAccounts
            .SingleOrDefaultAsync(account => account.NormalizedEmail == normalizedEmail, cancellationToken);
        if (existingAccount is null)
        {
            return null;
        }

        if (!string.Equals(existingAccount.Role, ApplicationUserRoles.Learner, StringComparison.Ordinal))
        {
            throw ApiException.Forbidden(
                "external_auth_role_not_supported",
                "External sign-in is available for learner self-service accounts only.");
        }

        var learner = await db.Users
            .SingleOrDefaultAsync(user => user.AuthAccountId == existingAccount.Id, cancellationToken);
        if (existingAccount.DeletedAt is not null || learner is null || !string.Equals(learner.AccountStatus, "active", StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Forbidden("account_suspended", "This account is not available for external sign-in.");
        }

        db.ExternalIdentityLinks.Add(new ExternalIdentityLink
        {
            Id = Guid.NewGuid(),
            ApplicationUserAccountId = existingAccount.Id,
            Provider = profile.Provider,
            ProviderSubject = profile.Subject,
            Email = profile.Email,
            FirstName = profile.FirstName,
            LastName = profile.LastName,
            CreatedAt = timeProvider.GetUtcNow(),
            UpdatedAt = timeProvider.GetUtcNow(),
            LastSignedInAt = timeProvider.GetUtcNow()
        });

        existingAccount.EmailVerifiedAt ??= timeProvider.GetUtcNow();
        existingAccount.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        return existingAccount;
    }

    private string BuildBackendCallbackUrl(string provider)
        => $"{ResolvePublicWebBaseUrl()}/api/backend/v1/auth/external/{provider}/callback";

    private Uri BuildFrontendCallbackRedirect(string provider, string exchangeToken, string? nextPath)
    {
        var callbackUrl = $"{ResolvePublicWebBaseUrl()}/auth/callback/{provider}?token={Uri.EscapeDataString(exchangeToken)}";
        if (!string.IsNullOrWhiteSpace(nextPath))
        {
            callbackUrl += $"&next={Uri.EscapeDataString(nextPath)}";
        }

        return new Uri(callbackUrl);
    }

    private Uri BuildFrontendErrorRedirect(string errorCode)
        => new($"{ResolvePublicWebBaseUrl()}/sign-in?externalError={Uri.EscapeDataString(errorCode)}");

    private string ResolvePublicWebBaseUrl()
    {
        var configured = _platformOptions.PublicWebBaseUrl;
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw ApiException.Validation(
                "external_auth_web_url_missing",
                "Platform:PublicWebBaseUrl must be configured for external authentication.");
        }

        return configured.TrimEnd('/');
    }

    private static string NormalizeProvider(string provider)
    {
        var normalized = provider.Trim().ToLowerInvariant();
        if (!ExternalAuthProviders.All.Contains(normalized, StringComparer.Ordinal))
        {
            throw ApiException.Validation("unsupported_external_provider", $"Unsupported external auth provider '{provider}'.");
        }

        return normalized;
    }
}
