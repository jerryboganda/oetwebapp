using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services;

public sealed record BrevoBlockedContact(
    string Email,
    string? ReasonCode,
    string? ReasonMessage,
    string? SenderEmail,
    DateTimeOffset? BlockedAt);

public interface IBrevoTransactionalMailbox
{
    Task<BrevoBlockedContact?> FindBlockedContactAsync(string email, CancellationToken cancellationToken = default);
    Task<bool> UnblockContactAsync(string email, CancellationToken cancellationToken = default);
}

public sealed class DisabledBrevoTransactionalMailbox : IBrevoTransactionalMailbox
{
    public Task<BrevoBlockedContact?> FindBlockedContactAsync(string email, CancellationToken cancellationToken = default)
        => Task.FromResult<BrevoBlockedContact?>(null);

    public Task<bool> UnblockContactAsync(string email, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}

/// <summary>
/// Best-effort Brevo transactional blocklist lookup/unblock. Admin-only.
/// Never auto-unblocks an entire list.
/// </summary>
public sealed class BrevoTransactionalMailbox(
    HttpClient httpClient,
    IOptions<BrevoOptions> options,
    IRuntimeSettingsProvider runtimeSettings,
    ILogger<BrevoTransactionalMailbox> logger) : IBrevoTransactionalMailbox
{
    public async Task<BrevoBlockedContact?> FindBlockedContactAsync(string email, CancellationToken cancellationToken = default)
    {
        var apiKey = await ResolveApiKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var target = email.Trim();
        for (var offset = 0; offset < 500; offset += 100)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/smtp/blockedContacts?limit=100&offset={offset}");
            ApplyApiKey(request, apiKey);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Brevo blocked-contact lookup failed. Status={StatusCode}", (int)response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<BrevoBlockedContactsResponse>(cancellationToken);
            var contacts = payload?.Contacts ?? [];
            var match = contacts.FirstOrDefault(contact =>
                string.Equals(contact.Email, target, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return new BrevoBlockedContact(
                    match.Email,
                    match.Reason?.Code,
                    match.Reason?.Message,
                    match.SenderEmail,
                    match.BlockedAt);
            }

            if (contacts.Count < 100)
            {
                return null;
            }
        }

        return null;
    }

    public async Task<bool> UnblockContactAsync(string email, CancellationToken cancellationToken = default)
    {
        var apiKey = await ResolveApiKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var encoded = Uri.EscapeDataString(email.Trim());
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/smtp/blockedContacts/{encoded}");
        ApplyApiKey(request, apiKey);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("Brevo unblock failed. Status={StatusCode} Response={Response}", (int)response.StatusCode, payload);
            throw new InvalidOperationException($"Brevo unblock failed with status {(int)response.StatusCode}.");
        }

        return true;
    }

    private async Task<string?> ResolveApiKeyAsync(CancellationToken cancellationToken)
    {
        var settings = await runtimeSettings.GetAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(settings.Email.BrevoApiKey)
            ? options.Value.ApiKey
            : settings.Email.BrevoApiKey;
    }

    private static void ApplyApiKey(HttpRequestMessage request, string apiKey)
    {
        request.Headers.TryAddWithoutValidation("api-key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private sealed record BrevoBlockedContactsResponse(
        [property: JsonPropertyName("contacts")] IReadOnlyList<BrevoBlockedContactDto>? Contacts,
        [property: JsonPropertyName("count")] long? Count);

    private sealed record BrevoBlockedContactDto(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("senderEmail")] string? SenderEmail,
        [property: JsonPropertyName("blockedAt")] DateTimeOffset? BlockedAt,
        [property: JsonPropertyName("reason")] BrevoBlockedReasonDto? Reason);

    private sealed record BrevoBlockedReasonDto(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("message")] string? Message);
}
