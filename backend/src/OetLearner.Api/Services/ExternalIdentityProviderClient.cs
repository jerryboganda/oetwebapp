using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services;

public interface IExternalIdentityProviderClient
{
    Uri BuildAuthorizationUri(string provider, string state, string redirectUri);
    Task<ExternalIdentityProfile> ExchangeCodeAsync(string provider, string code, string redirectUri, CancellationToken cancellationToken = default);
}

public sealed class ExternalIdentityProviderClient(
    HttpClient httpClient,
    IOptions<ExternalAuthOptions> optionsAccessor) : IExternalIdentityProviderClient
{
    private readonly ExternalAuthOptions _options = optionsAccessor.Value;

    public Uri BuildAuthorizationUri(string provider, string state, string redirectUri)
    {
        var configuration = GetProviderConfiguration(provider);
        var parameters = provider switch
        {
            ExternalAuthProviders.Google => new Dictionary<string, string?>
            {
                ["client_id"] = configuration.ClientId,
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["scope"] = "openid email profile",
                ["state"] = state,
                ["prompt"] = "select_account"
            },
            ExternalAuthProviders.Facebook => new Dictionary<string, string?>
            {
                ["client_id"] = configuration.ClientId,
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["scope"] = "email,public_profile",
                ["state"] = state
            },
            ExternalAuthProviders.LinkedIn => new Dictionary<string, string?>
            {
                ["client_id"] = configuration.ClientId,
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["scope"] = "openid profile email",
                ["state"] = state
            },
            _ => throw ApiException.Validation("unsupported_external_provider", $"Unsupported external auth provider '{provider}'.")
        };

        return new Uri(QueryHelpers.AddQueryString(GetAuthorizationEndpoint(provider), parameters!));
    }

    public async Task<ExternalIdentityProfile> ExchangeCodeAsync(string provider, string code, string redirectUri, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw ApiException.Validation("external_auth_code_required", "The external sign-in code is missing.");
        }

        var configuration = GetProviderConfiguration(provider);
        var tokenPayload = await ExchangeAuthorizationCodeAsync(provider, configuration, code, redirectUri, cancellationToken);
        var accessToken = tokenPayload.GetProperty("access_token").GetString();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw ApiException.Validation("external_auth_access_token_missing", "The external provider did not return an access token.");
        }

        return provider switch
        {
            ExternalAuthProviders.Google => await LoadGoogleProfileAsync(accessToken, cancellationToken),
            ExternalAuthProviders.Facebook => await LoadFacebookProfileAsync(accessToken, cancellationToken),
            ExternalAuthProviders.LinkedIn => await LoadLinkedInProfileAsync(accessToken, cancellationToken),
            _ => throw ApiException.Validation("unsupported_external_provider", $"Unsupported external auth provider '{provider}'.")
        };
    }

    private async Task<JsonElement> ExchangeAuthorizationCodeAsync(
        string provider,
        ExternalAuthProviderOptions configuration,
        string code,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, GetTokenEndpoint(provider))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["client_id"] = configuration.ClientId,
                ["client_secret"] = configuration.ClientSecret,
                ["code"] = code,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri
            }!)
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ApiException.Validation(
                "external_auth_token_exchange_failed",
                $"The {provider} sign-in token exchange failed.");
        }

        using var document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }

    private async Task<ExternalIdentityProfile> LoadGoogleProfileAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var profile = await response.Content.ReadFromJsonAsync<GoogleUserInfo>(cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode || profile is null || string.IsNullOrWhiteSpace(profile.Sub) || string.IsNullOrWhiteSpace(profile.Email))
        {
            throw ApiException.Validation("external_auth_profile_failed", "Google did not return a valid email profile.");
        }

        return new ExternalIdentityProfile(
            ExternalAuthProviders.Google,
            profile.Sub,
            profile.Email,
            profile.GivenName,
            profile.FamilyName,
            profile.Name);
    }

    private async Task<ExternalIdentityProfile> LoadFacebookProfileAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://graph.facebook.com/me?fields=id,email,first_name,last_name,name");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var profile = await response.Content.ReadFromJsonAsync<FacebookUserInfo>(cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode || profile is null || string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.Email))
        {
            throw ApiException.Validation("external_auth_profile_failed", "Facebook did not return a valid email profile.");
        }

        return new ExternalIdentityProfile(
            ExternalAuthProviders.Facebook,
            profile.Id,
            profile.Email,
            profile.FirstName,
            profile.LastName,
            profile.Name);
    }

    private async Task<ExternalIdentityProfile> LoadLinkedInProfileAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.linkedin.com/v2/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var profile = await response.Content.ReadFromJsonAsync<LinkedInUserInfo>(cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode || profile is null || string.IsNullOrWhiteSpace(profile.Sub) || string.IsNullOrWhiteSpace(profile.Email))
        {
            throw ApiException.Validation("external_auth_profile_failed", "LinkedIn did not return a valid email profile.");
        }

        return new ExternalIdentityProfile(
            ExternalAuthProviders.LinkedIn,
            profile.Sub,
            profile.Email,
            profile.GivenName,
            profile.FamilyName,
            profile.Name);
    }

    private ExternalAuthProviderOptions GetProviderConfiguration(string provider)
    {
        if (!ExternalAuthProviders.All.Contains(provider, StringComparer.OrdinalIgnoreCase))
        {
            throw ApiException.Validation("unsupported_external_provider", $"Unsupported external auth provider '{provider}'.");
        }

        var configuration = _options.GetProvider(provider);
        if (!configuration.Enabled
            || string.IsNullOrWhiteSpace(configuration.ClientId)
            || string.IsNullOrWhiteSpace(configuration.ClientSecret))
        {
            throw ApiException.Validation(
                "external_auth_not_configured",
                $"The {provider} sign-in provider is not configured.");
        }

        return configuration;
    }

    private static string GetAuthorizationEndpoint(string provider)
        => provider switch
        {
            ExternalAuthProviders.Google => "https://accounts.google.com/o/oauth2/v2/auth",
            ExternalAuthProviders.Facebook => "https://www.facebook.com/v23.0/dialog/oauth",
            ExternalAuthProviders.LinkedIn => "https://www.linkedin.com/oauth/v2/authorization",
            _ => throw ApiException.Validation("unsupported_external_provider", $"Unsupported external auth provider '{provider}'.")
        };

    private static string GetTokenEndpoint(string provider)
        => provider switch
        {
            ExternalAuthProviders.Google => "https://oauth2.googleapis.com/token",
            ExternalAuthProviders.Facebook => "https://graph.facebook.com/v23.0/oauth/access_token",
            ExternalAuthProviders.LinkedIn => "https://www.linkedin.com/oauth/v2/accessToken",
            _ => throw ApiException.Validation("unsupported_external_provider", $"Unsupported external auth provider '{provider}'.")
        };

    private sealed record GoogleUserInfo(
        string Sub,
        string Email,
        string? GivenName,
        string? FamilyName,
        string? Name);

    private sealed record FacebookUserInfo(
        string Id,
        string Email,
        string? FirstName,
        string? LastName,
        string? Name);

    private sealed record LinkedInUserInfo(
        string Sub,
        string Email,
        string? GivenName,
        string? FamilyName,
        string? Name);
}

public sealed record ExternalIdentityProfile(
    string Provider,
    string Subject,
    string Email,
    string? FirstName,
    string? LastName,
    string? DisplayName);
