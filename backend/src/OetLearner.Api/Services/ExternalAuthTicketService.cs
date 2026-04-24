using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace OetLearner.Api.Services;

public sealed class ExternalAuthTicketService(IDataProtectionProvider dataProtectionProvider)
{
    private readonly ITimeLimitedDataProtector _stateProtector = dataProtectionProvider
        .CreateProtector("ExternalAuth.State")
        .ToTimeLimitedDataProtector();

    private readonly ITimeLimitedDataProtector _exchangeProtector = dataProtectionProvider
        .CreateProtector("ExternalAuth.Exchange")
        .ToTimeLimitedDataProtector();

    private readonly ITimeLimitedDataProtector _registrationProtector = dataProtectionProvider
        .CreateProtector("ExternalAuth.Registration")
        .ToTimeLimitedDataProtector();

    public string CreateStateToken(string provider, string? nextPath, string? platform)
        => Protect(
            _stateProtector,
            new ExternalAuthStateTicket(provider, NormalizeNextPath(nextPath), NormalizePlatform(platform)),
            lifetime: TimeSpan.FromMinutes(10));

    public ExternalAuthStateTicket ReadStateToken(string provider, string token)
    {
        var ticket = Unprotect<ExternalAuthStateTicket>(_stateProtector, token, "invalid_external_auth_state");
        if (!string.Equals(ticket.Provider, provider, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("invalid_external_auth_state", "The external sign-in state is invalid.");
        }

        return ticket with { NextPath = NormalizeNextPath(ticket.NextPath) };
    }

    public string CreateAuthenticatedExchangeToken(string provider, string accountId, string? nextPath, bool emailVerified)
        => Protect(
            _exchangeProtector,
            new ExternalAuthExchangeTicket(
                provider,
                Kind: ExternalAuthExchangeKinds.Authenticated,
                AccountId: accountId,
                ProviderSubject: null,
                Email: null,
                FirstName: null,
                LastName: null,
                NextPath: NormalizeNextPath(nextPath),
                EmailVerified: emailVerified),
            lifetime: TimeSpan.FromMinutes(10));

    public string CreateRegistrationExchangeToken(
        string provider,
        string providerSubject,
        string email,
        string? firstName,
        string? lastName,
        string? nextPath,
        bool emailVerified)
        => Protect(
            _exchangeProtector,
            new ExternalAuthExchangeTicket(
                provider,
                Kind: ExternalAuthExchangeKinds.RegistrationRequired,
                AccountId: null,
                ProviderSubject: providerSubject,
                Email: email,
                FirstName: firstName,
                LastName: lastName,
                NextPath: NormalizeNextPath(nextPath),
                EmailVerified: emailVerified),
            lifetime: TimeSpan.FromMinutes(10));

    public ExternalAuthExchangeTicket ReadExchangeToken(string provider, string token)
    {
        var ticket = Unprotect<ExternalAuthExchangeTicket>(_exchangeProtector, token, "invalid_external_exchange_token");
        if (!string.Equals(ticket.Provider, provider, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("invalid_external_exchange_token", "The external exchange token is invalid.");
        }

        return ticket with { NextPath = NormalizeNextPath(ticket.NextPath) };
    }

    public string CreateRegistrationToken(
        string provider,
        string providerSubject,
        string email,
        string? firstName,
        string? lastName,
        string? nextPath)
        => Protect(
            _registrationProtector,
            new ExternalRegistrationTicket(
                provider,
                providerSubject,
                email,
                firstName,
                lastName,
                NormalizeNextPath(nextPath)),
            lifetime: TimeSpan.FromMinutes(30));

    public ExternalRegistrationTicket ReadRegistrationToken(string token)
    {
        var ticket = Unprotect<ExternalRegistrationTicket>(
            _registrationProtector,
            token,
            "invalid_external_registration_token");

        return ticket with { NextPath = NormalizeNextPath(ticket.NextPath) };
    }

    private static string? NormalizeNextPath(string? nextPath)
        => !string.IsNullOrWhiteSpace(nextPath)
           && nextPath.StartsWith("/", StringComparison.Ordinal)
           && !nextPath.StartsWith("//", StringComparison.Ordinal)
            ? nextPath
            : null;

    private static string? NormalizePlatform(string? platform)
        => string.Equals(platform?.Trim(), "desktop", StringComparison.OrdinalIgnoreCase) ? "desktop" : null;

    private static string Protect<T>(ITimeLimitedDataProtector protector, T ticket, TimeSpan lifetime)
    {
        var json = JsonSupport.Serialize(ticket);
        var protectedPayload = protector.Protect(json, lifetime);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(protectedPayload));
    }

    private static T Unprotect<T>(ITimeLimitedDataProtector protector, string token, string errorCode)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw ApiException.Validation(errorCode, "The external authentication token is missing.");
        }

        try
        {
            var protectedPayload = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
            var json = protector.Unprotect(protectedPayload);
            return JsonSupport.Deserialize<T>(json, fallback: default!)
                   ?? throw new InvalidOperationException("Protected ticket payload was empty.");
        }
        catch (ApiException)
        {
            throw;
        }
        catch
        {
            throw ApiException.Validation(errorCode, "The external authentication token is invalid or expired.");
        }
    }
}

public static class ExternalAuthExchangeKinds
{
    public const string Authenticated = "authenticated";
    public const string RegistrationRequired = "registration_required";
}

public sealed record ExternalAuthStateTicket(string Provider, string? NextPath, string? Platform);

public sealed record ExternalAuthExchangeTicket(
    string Provider,
    string Kind,
    string? AccountId,
    string? ProviderSubject,
    string? Email,
    string? FirstName,
    string? LastName,
    string? NextPath,
    // H4 (security): whether the upstream provider asserted email verification.
    // Defaults to false on deserialisation of older tokens.
    bool EmailVerified = false);

public sealed record ExternalRegistrationTicket(
    string Provider,
    string ProviderSubject,
    string Email,
    string? FirstName,
    string? LastName,
    string? NextPath);
