using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Settings;

namespace OetWithDrHesham.Api.Services;

public sealed class PrivateSpeakingCalendarService(
    LearnerDbContext db,
    IHttpClientFactory httpClientFactory,
    IRuntimeSettingsProvider runtimeSettings,
    IDataProtectionProvider dataProtectionProvider,
    PlatformLinkService platformLinks,
    TimeProvider timeProvider,
    ILogger<PrivateSpeakingCalendarService> logger)
{
    private static readonly string[] GoogleCalendarScopes =
    [
        "openid",
        "email",
        "https://www.googleapis.com/auth/calendar.freebusy",
        "https://www.googleapis.com/auth/calendar.events"
    ];

    private readonly IDataProtector _stateProtector = dataProtectionProvider.CreateProtector("PrivateSpeaking.CalendarOAuthState.v1");

    public async Task<PrivateSpeakingCalendarStatusDto> GetStatusAsync(string expertUserId, CancellationToken ct)
    {
        var connection = await db.PrivateSpeakingTutorCalendarConnections
            .AsNoTracking()
            .Where(item => item.ExpertUserId == expertUserId && item.DisconnectedAt == null)
            .OrderByDescending(item => item.ConnectedAt)
            .FirstOrDefaultAsync(ct);

        return connection is null
            ? new PrivateSpeakingCalendarStatusDto(false, null, null, null, null, null, null, null)
            : new PrivateSpeakingCalendarStatusDto(
                true,
                connection.Provider,
                connection.CalendarId,
                connection.ConnectedEmail,
                connection.ConnectedAt,
                connection.LastCheckedAt,
                connection.LastSyncedAt,
                connection.LastError);
    }

    public async Task<PrivateSpeakingCalendarConnectResult> BuildGoogleConnectUrlAsync(string expertUserId, CancellationToken ct)
    {
        _ = await ResolveTutorProfileForExpertAsync(expertUserId, ct);
        var settings = await runtimeSettings.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.OAuth.GoogleClientId) || string.IsNullOrWhiteSpace(settings.OAuth.GoogleClientSecret))
        {
            throw ApiException.ServiceUnavailable("google_calendar_not_configured", "Google Calendar is not configured for this environment.", retryable: false);
        }

        var expiresAt = timeProvider.GetUtcNow().AddMinutes(10);
        var state = _stateProtector.Protect(JsonSerializer.Serialize(new GoogleCalendarOAuthState(expertUserId, expiresAt)));
        var redirectUri = BuildCallbackUrl();
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = settings.OAuth.GoogleClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', GoogleCalendarScopes),
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["include_granted_scopes"] = "true",
            ["state"] = state
        };

        var authorizationUrl = QueryHelpers.AddQueryString("https://accounts.google.com/o/oauth2/v2/auth", query);
        return new PrivateSpeakingCalendarConnectResult(authorizationUrl, expiresAt);
    }

    public async Task<PrivateSpeakingCalendarStatusDto> CompleteGoogleConnectAsync(
        string expertUserId,
        string code,
        string state,
        CancellationToken ct)
    {
        var statePayload = UnprotectState(state);
        if (statePayload.ExpertUserId != expertUserId || statePayload.ExpiresAt <= timeProvider.GetUtcNow())
        {
            throw ApiException.Forbidden("calendar_oauth_state_invalid", "The Google Calendar connection request expired. Please try again.");
        }

        var profile = await ResolveTutorProfileForExpertAsync(expertUserId, ct);
        var settings = await runtimeSettings.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.OAuth.GoogleClientId) || string.IsNullOrWhiteSpace(settings.OAuth.GoogleClientSecret))
        {
            throw ApiException.ServiceUnavailable("google_calendar_not_configured", "Google Calendar is not configured for this environment.", retryable: false);
        }

        var token = await ExchangeAuthorizationCodeAsync(
            code,
            settings.OAuth.GoogleClientId!,
            settings.OAuth.GoogleClientSecret!,
            BuildCallbackUrl(),
            ct);

        var existing = await db.PrivateSpeakingTutorCalendarConnections
            .FirstOrDefaultAsync(item => item.TutorProfileId == profile.Id, ct);

        if (string.IsNullOrWhiteSpace(token.RefreshToken) && string.IsNullOrWhiteSpace(existing?.RefreshTokenEncrypted))
        {
            throw ApiException.Conflict("calendar_refresh_token_missing", "Google did not return offline calendar access. Please reconnect and approve offline access.");
        }

        var now = timeProvider.GetUtcNow();
        var connectedEmail = await TryGetGoogleEmailAsync(token.AccessToken, ct);
        existing ??= new PrivateSpeakingTutorCalendarConnection
        {
            Id = $"pscal-{Guid.NewGuid():N}",
            TutorProfileId = profile.Id,
            ExpertUserId = expertUserId,
            ConnectedAt = now
        };

        existing.Provider = "google";
        existing.CalendarId = "primary";
        existing.ConnectedEmail = connectedEmail ?? existing.ConnectedEmail;
        existing.Scopes = token.Scope ?? string.Join(' ', GoogleCalendarScopes);
        existing.DisconnectedAt = null;
        existing.LastError = null;
        existing.UpdatedAt = now;
        if (!string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            existing.RefreshTokenEncrypted = runtimeSettings.Protect(token.RefreshToken!);
        }

        if (db.Entry(existing).State == EntityState.Detached)
        {
            db.PrivateSpeakingTutorCalendarConnections.Add(existing);
        }

        await db.SaveChangesAsync(ct);
        return await GetStatusAsync(expertUserId, ct);
    }

    public async Task<PrivateSpeakingCalendarStatusDto> CompleteGoogleConnectCallbackAsync(
        string code,
        string state,
        CancellationToken ct)
    {
        var statePayload = UnprotectState(state);
        if (statePayload.ExpiresAt <= timeProvider.GetUtcNow())
        {
            throw ApiException.Forbidden("calendar_oauth_state_invalid", "The Google Calendar connection request expired. Please try again.");
        }

        return await CompleteGoogleConnectAsync(statePayload.ExpertUserId, code, state, ct);
    }

    public async Task DisconnectAsync(string expertUserId, CancellationToken ct)
    {
        var profile = await ResolveTutorProfileForExpertAsync(expertUserId, ct);
        var connection = await db.PrivateSpeakingTutorCalendarConnections
            .FirstOrDefaultAsync(item => item.TutorProfileId == profile.Id && item.DisconnectedAt == null, ct);
        if (connection is null) return;

        connection.RefreshTokenEncrypted = null;
        connection.DisconnectedAt = timeProvider.GetUtcNow();
        connection.UpdatedAt = connection.DisconnectedAt.Value;
        await db.SaveChangesAsync(ct);
    }

    public async Task<PrivateSpeakingCalendarBusyResult> CheckBusyAsync(
        string tutorProfileId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken ct)
    {
        var connection = await GetActiveConnectionForTutorAsync(tutorProfileId, track: true, ct);
        if (connection is null)
        {
            return new PrivateSpeakingCalendarBusyResult(false, false, null);
        }

        try
        {
            var accessToken = await RefreshAccessTokenAsync(connection, ct);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/calendar/v3/freeBusy");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = JsonContent(new
            {
                timeMin = startUtc.UtcDateTime.ToString("O"),
                timeMax = endUtc.UtcDateTime.ToString("O"),
                timeZone = "UTC",
                items = new[] { new { id = connection.CalendarId } }
            });

            using var response = await GoogleClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Google free-busy check failed: {(int)response.StatusCode} {Truncate(body, 220)}");
            }

            using var doc = JsonDocument.Parse(body);
            var busy = false;
            if (doc.RootElement.TryGetProperty("calendars", out var calendars)
                && calendars.TryGetProperty(connection.CalendarId, out var calendar)
                && calendar.TryGetProperty("busy", out var busyItems)
                && busyItems.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in busyItems.EnumerateArray())
                {
                    if (!TryReadDateTimeOffset(item, "start", out var busyStart)
                        || !TryReadDateTimeOffset(item, "end", out var busyEnd))
                    {
                        continue;
                    }

                    if (busyStart < endUtc && busyEnd > startUtc)
                    {
                        busy = true;
                        break;
                    }
                }
            }

            connection.LastCheckedAt = timeProvider.GetUtcNow();
            connection.LastError = null;
            connection.UpdatedAt = connection.LastCheckedAt.Value;
            await db.SaveChangesAsync(ct);
            return new PrivateSpeakingCalendarBusyResult(true, busy, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            connection.LastCheckedAt = timeProvider.GetUtcNow();
            connection.LastError = Truncate(ex.Message, 500);
            connection.UpdatedAt = connection.LastCheckedAt.Value;
            await db.SaveChangesAsync(ct);
            logger.LogWarning(ex, "Google Calendar busy check failed for tutor profile {TutorProfileId}", tutorProfileId);
            return new PrivateSpeakingCalendarBusyResult(true, false, connection.LastError);
        }
    }

    public async Task SyncBookingAsync(string bookingId, CancellationToken ct)
    {
        var booking = await db.PrivateSpeakingBookings
            .Include(item => item.TutorProfile)
            .FirstOrDefaultAsync(item => item.Id == bookingId, ct);
        if (booking?.TutorProfile is null) return;

        var connection = await GetActiveConnectionForTutorAsync(booking.TutorProfileId, track: true, ct);
        if (connection is null) return;

        try
        {
            var accessToken = await RefreshAccessTokenAsync(connection, ct);
            if (IsCalendarDeleteStatus(booking.Status))
            {
                if (!string.IsNullOrWhiteSpace(booking.GoogleCalendarEventId))
                {
                    await DeleteCalendarEventAsync(connection, booking.GoogleCalendarEventId!, accessToken, ct);
                }

                booking.GoogleCalendarSyncStatus = "deleted";
                booking.GoogleCalendarSyncError = null;
                booking.GoogleCalendarSyncedAt = timeProvider.GetUtcNow();
                connection.LastSyncedAt = booking.GoogleCalendarSyncedAt;
                connection.LastError = null;
                connection.UpdatedAt = booking.GoogleCalendarSyncedAt.Value;
                await db.SaveChangesAsync(ct);
                return;
            }

            var eventBody = BuildEventBody(booking);
            var eventId = string.IsNullOrWhiteSpace(booking.GoogleCalendarEventId)
                ? await CreateCalendarEventAsync(connection, eventBody, accessToken, ct)
                : await PatchCalendarEventAsync(connection, booking.GoogleCalendarEventId!, eventBody, accessToken, ct);

            booking.GoogleCalendarEventId = eventId;
            booking.GoogleCalendarSyncStatus = "synced";
            booking.GoogleCalendarSyncError = null;
            booking.GoogleCalendarSyncedAt = timeProvider.GetUtcNow();
            connection.LastSyncedAt = booking.GoogleCalendarSyncedAt;
            connection.LastError = null;
            connection.UpdatedAt = booking.GoogleCalendarSyncedAt.Value;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            booking.GoogleCalendarSyncStatus = "failed";
            booking.GoogleCalendarSyncError = Truncate(ex.Message, 500);
            booking.GoogleCalendarSyncedAt = timeProvider.GetUtcNow();
            connection.LastError = booking.GoogleCalendarSyncError;
            connection.UpdatedAt = booking.GoogleCalendarSyncedAt.Value;
            await db.SaveChangesAsync(ct);
            logger.LogWarning(ex, "Google Calendar sync failed for private speaking booking {BookingId}", booking.Id);
            throw;
        }
    }

    private HttpClient GoogleClient => httpClientFactory.CreateClient("GoogleCalendar");

    private async Task<PrivateSpeakingTutorProfile> ResolveTutorProfileForExpertAsync(string expertUserId, CancellationToken ct)
        => await db.PrivateSpeakingTutorProfiles.FirstOrDefaultAsync(item => item.ExpertUserId == expertUserId, ct)
           ?? throw ApiException.NotFound("private_speaking_tutor_profile_missing", "Create a private speaking tutor profile before connecting a calendar.");

    private async Task<PrivateSpeakingTutorCalendarConnection?> GetActiveConnectionForTutorAsync(
        string tutorProfileId,
        bool track,
        CancellationToken ct)
    {
        var query = db.PrivateSpeakingTutorCalendarConnections
            .Where(item => item.TutorProfileId == tutorProfileId && item.DisconnectedAt == null && item.RefreshTokenEncrypted != null);
        if (!track) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(ct);
    }

    private GoogleCalendarOAuthState UnprotectState(string state)
    {
        try
        {
            return JsonSerializer.Deserialize<GoogleCalendarOAuthState>(_stateProtector.Unprotect(state))
                   ?? throw ApiException.Forbidden("calendar_oauth_state_invalid", "Invalid Google Calendar connection state.");
        }
        catch (Exception ex) when (ex is not ApiException)
        {
            throw ApiException.Forbidden("calendar_oauth_state_invalid", "Invalid Google Calendar connection state.");
        }
    }

    private string BuildCallbackUrl()
        => platformLinks.BuildApiUrl("/v1/expert/private-speaking/calendar/google/callback");

    private async Task<GoogleTokenResponse> ExchangeAuthorizationCodeAsync(
        string code,
        string clientId,
        string clientSecret,
        string redirectUri,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code"
            })
        };
        using var response = await GoogleClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw ApiException.ServiceUnavailable("google_calendar_token_exchange_failed", "Google Calendar authorization could not be completed.");
        }

        using var doc = JsonDocument.Parse(body);
        return new GoogleTokenResponse(
            RequireString(doc.RootElement, "access_token"),
            ReadOptionalString(doc.RootElement, "refresh_token"),
            ReadOptionalString(doc.RootElement, "scope"));
    }

    private async Task<string> RefreshAccessTokenAsync(PrivateSpeakingTutorCalendarConnection connection, CancellationToken ct)
    {
        var refreshToken = runtimeSettings.Unprotect(connection.RefreshTokenEncrypted)
            ?? throw new InvalidOperationException("Google Calendar refresh token is unavailable.");
        var settings = await runtimeSettings.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(settings.OAuth.GoogleClientId) || string.IsNullOrWhiteSpace(settings.OAuth.GoogleClientSecret))
        {
            throw new InvalidOperationException("Google Calendar is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = settings.OAuth.GoogleClientId!,
                ["client_secret"] = settings.OAuth.GoogleClientSecret!,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token"
            })
        };
        using var response = await GoogleClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google Calendar token refresh failed: {(int)response.StatusCode} {Truncate(body, 220)}");
        }

        using var doc = JsonDocument.Parse(body);
        return RequireString(doc.RootElement, "access_token");
    }

    private async Task<string?> TryGetGoogleEmailAsync(string accessToken, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await GoogleClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            return ReadOptionalString(doc.RootElement, "email");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Google userinfo lookup failed during calendar connect.");
            return null;
        }
    }

    private static HttpContent JsonContent(object value)
        => new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private async Task<string> CreateCalendarEventAsync(
        PrivateSpeakingTutorCalendarConnection connection,
        object eventBody,
        string accessToken,
        CancellationToken ct)
    {
        var url = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(connection.CalendarId)}/events?sendUpdates=none";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent(eventBody);
        using var response = await GoogleClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google Calendar event create failed: {(int)response.StatusCode} {Truncate(body, 220)}");
        }

        using var doc = JsonDocument.Parse(body);
        return RequireString(doc.RootElement, "id");
    }

    private async Task<string> PatchCalendarEventAsync(
        PrivateSpeakingTutorCalendarConnection connection,
        string eventId,
        object eventBody,
        string accessToken,
        CancellationToken ct)
    {
        var url = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(connection.CalendarId)}/events/{Uri.EscapeDataString(eventId)}?sendUpdates=none";
        using var request = new HttpRequestMessage(HttpMethod.Patch, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent(eventBody);
        using var response = await GoogleClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google Calendar event update failed: {(int)response.StatusCode} {Truncate(body, 220)}");
        }

        using var doc = JsonDocument.Parse(body);
        return RequireString(doc.RootElement, "id");
    }

    private async Task DeleteCalendarEventAsync(
        PrivateSpeakingTutorCalendarConnection connection,
        string eventId,
        string accessToken,
        CancellationToken ct)
    {
        var url = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(connection.CalendarId)}/events/{Uri.EscapeDataString(eventId)}?sendUpdates=none";
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await GoogleClient.SendAsync(request, ct);
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException($"Google Calendar event delete failed: {(int)response.StatusCode} {Truncate(body, 220)}");
    }

    private object BuildEventBody(PrivateSpeakingBooking booking)
    {
        var start = booking.SessionStartUtc.UtcDateTime.ToString("O");
        var end = booking.SessionStartUtc.AddMinutes(booking.DurationMinutes).UtcDateTime.ToString("O");
        var learnerUrl = platformLinks.BuildWebUrl($"/private-speaking?bookingId={Uri.EscapeDataString(booking.Id)}");
        var expertUrl = platformLinks.BuildWebUrl($"/expert/private-speaking?bookingId={Uri.EscapeDataString(booking.Id)}");
        var description = $"OET private speaking session. Tutor console: {expertUrl}\nLearner booking: {learnerUrl}";

        return new
        {
            summary = "OET Private Speaking Session",
            description,
            location = "Zoom",
            start = new { dateTime = start, timeZone = "UTC" },
            end = new { dateTime = end, timeZone = "UTC" },
            transparency = "opaque",
            visibility = "private",
            reminders = new
            {
                useDefault = false,
                overrides = new[]
                {
                    new { method = "popup", minutes = 60 },
                    new { method = "email", minutes = 24 * 60 }
                }
            },
            extendedProperties = new
            {
                @private = new Dictionary<string, string>
                {
                    ["oetPrivateSpeakingBookingId"] = booking.Id
                }
            }
        };
    }

    private static bool IsCalendarDeleteStatus(PrivateSpeakingBookingStatus status)
        => status is PrivateSpeakingBookingStatus.Cancelled
            or PrivateSpeakingBookingStatus.Refunded
            or PrivateSpeakingBookingStatus.Expired
            or PrivateSpeakingBookingStatus.Failed;

    private static bool TryReadDateTimeOffset(JsonElement element, string propertyName, out DateTimeOffset value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(property.GetString(), out value);
    }

    private static string RequireString(JsonElement element, string propertyName)
    {
        var value = ReadOptionalString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Google response did not include {propertyName}.");
        }

        return value!;
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private sealed record GoogleCalendarOAuthState(string ExpertUserId, DateTimeOffset ExpiresAt);
    private sealed record GoogleTokenResponse(string AccessToken, string? RefreshToken, string? Scope);
}

public sealed record PrivateSpeakingCalendarStatusDto(
    bool Connected,
    string? Provider,
    string? CalendarId,
    string? ConnectedEmail,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset? LastSyncedAt,
    string? LastError);

public sealed record PrivateSpeakingCalendarConnectResult(string AuthorizationUrl, DateTimeOffset ExpiresAt);

public sealed record PrivateSpeakingCalendarBusyResult(bool Connected, bool IsBusy, string? Error);