using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.StudyPlanner;

/// <summary>
/// Google Calendar OAuth + event push integration for the Study Planner.
///
/// <para>
/// Config (via <c>appsettings.json</c> <c>GoogleCalendar</c> section):
/// <code>
/// "GoogleCalendar": {
///   "ClientId":     "...",
///   "ClientSecret": "...",
///   "RedirectUri":  "https://api.oetwithdrhesham.co.uk/v1/study-plan/google-calendar/oauth/callback"
/// }
/// </code>
/// </para>
///
/// <para>
/// Sync model: on plan regeneration, delete + recreate events tagged with
/// <c>extendedProperties.private.source = "oet-study-plan"</c>. Keeps sync
/// state-machine trivial vs tracking per-event IDs, at the cost of an extra
/// list call per regen (acceptable for &lt; 50 items/learner).
/// </para>
/// </summary>
public interface IGoogleCalendarService
{
    string BuildAuthorizationUrl(string state);
    Task<LearnerCalendarLink> ExchangeCodeAsync(string userId, string code, CancellationToken ct);
    Task<LearnerCalendarLink?> GetLinkAsync(string userId, CancellationToken ct);
    Task DisconnectAsync(string userId, CancellationToken ct);
    Task<int> PushPlanAsync(string userId, CancellationToken ct);
}

public sealed class GoogleCalendarOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string Scope { get; set; } = "https://www.googleapis.com/auth/calendar";
}

public sealed class GoogleCalendarService(
    LearnerDbContext db,
    IHttpClientFactory httpFactory,
    IDataProtectionProvider dataProtection,
    Microsoft.Extensions.Options.IOptions<GoogleCalendarOptions> options,
    ILogger<GoogleCalendarService>? logger = null) : IGoogleCalendarService
{
    private const string ProtectorPurpose = "study-planner.google-calendar.refresh-token";
    private const string EventSourceMarker = "oet-study-plan";
    private readonly GoogleCalendarOptions _opts = options.Value;

    public string BuildAuthorizationUrl(string state)
    {
        if (string.IsNullOrWhiteSpace(_opts.ClientId))
            throw new InvalidOperationException("Google Calendar client id not configured");
        var q = new Dictionary<string, string>
        {
            ["client_id"] = _opts.ClientId,
            ["redirect_uri"] = _opts.RedirectUri,
            ["scope"] = _opts.Scope,
            ["response_type"] = "code",
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state,
        };
        var qs = string.Join('&', q.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"https://accounts.google.com/o/oauth2/v2/auth?{qs}";
    }

    public async Task<LearnerCalendarLink> ExchangeCodeAsync(string userId, string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.ClientId) || string.IsNullOrWhiteSpace(_opts.ClientSecret))
            throw new InvalidOperationException("Google Calendar not configured");
        var http = httpFactory.CreateClient();
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _opts.ClientId,
            ["client_secret"] = _opts.ClientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = _opts.RedirectUri,
        });
        using var resp = await http.PostAsync("https://oauth2.googleapis.com/token", body, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Google token exchange failed: {json}");
        using var doc = JsonDocument.Parse(json);
        var refreshToken = doc.RootElement.GetProperty("refresh_token").GetString()
            ?? throw new InvalidOperationException("No refresh_token returned (user may have previously authorised without offline access).");
        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;

        var protector = dataProtection.CreateProtector(ProtectorPurpose);
        var encrypted = protector.Protect(refreshToken);
        var hint = refreshToken.Length >= 6 ? refreshToken[^6..] : "****";
        var now = DateTimeOffset.UtcNow;

        var existing = await db.LearnerCalendarLinks
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Provider == "google", ct);
        if (existing is null)
        {
            existing = new LearnerCalendarLink
            {
                Id = $"lcl-{Guid.NewGuid():N}",
                UserId = userId,
                Provider = "google",
                CalendarId = "primary",
                RefreshTokenEncrypted = encrypted,
                TokenHint = hint,
                AccessTokenExpiresAt = now.AddSeconds(expiresIn),
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.LearnerCalendarLinks.Add(existing);
        }
        else
        {
            existing.RefreshTokenEncrypted = encrypted;
            existing.TokenHint = hint;
            existing.AccessTokenExpiresAt = now.AddSeconds(expiresIn);
            existing.IsActive = true;
            existing.UpdatedAt = now;
            existing.LastError = null;
        }
        await db.SaveChangesAsync(ct);
        return existing;
    }

    public Task<LearnerCalendarLink?> GetLinkAsync(string userId, CancellationToken ct)
        => db.LearnerCalendarLinks.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId && x.Provider == "google", ct);

    public async Task DisconnectAsync(string userId, CancellationToken ct)
    {
        var link = await db.LearnerCalendarLinks.FirstOrDefaultAsync(x => x.UserId == userId && x.Provider == "google", ct);
        if (link is null) return;
        db.LearnerCalendarLinks.Remove(link);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> PushPlanAsync(string userId, CancellationToken ct)
    {
        var link = await db.LearnerCalendarLinks
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Provider == "google" && x.IsActive, ct);
        if (link is null) return 0;
        var accessToken = await ExchangeRefreshAsync(link, ct);
        if (accessToken is null) return 0;

        var plan = await db.StudyPlans
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.GeneratedAt).FirstOrDefaultAsync(ct);
        if (plan is null) return 0;
        var items = await db.StudyPlanItems.AsNoTracking()
            .Where(i => i.StudyPlanId == plan.Id && i.Status == StudyPlanItemStatus.NotStarted)
            .ToListAsync(ct);

        var http = httpFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        // 1) Clear existing events tagged with source=oet-study-plan
        try
        {
            var listUrl = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(link.CalendarId)}/events?privateExtendedProperty=source%3D{EventSourceMarker}&singleEvents=true&maxResults=250";
            using var listResp = await http.GetAsync(listUrl, ct);
            if (listResp.IsSuccessStatusCode)
            {
                var json = await listResp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("items", out var evts))
                {
                    foreach (var e in evts.EnumerateArray())
                    {
                        if (!e.TryGetProperty("id", out var eid)) continue;
                        var id = eid.GetString();
                        if (string.IsNullOrEmpty(id)) continue;
                        await http.DeleteAsync($"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(link.CalendarId)}/events/{id}", ct);
                    }
                }
            }
        }
        catch (Exception ex) { logger?.LogWarning(ex, "Failed to clean stale events"); }

        // 2) Push new events
        var pushed = 0;
        foreach (var it in items)
        {
            var startDt = it.DueDate.ToDateTime(new TimeOnly(9, 0));
            var endDt = startDt.AddMinutes(Math.Max(5, it.DurationMinutes));
            var payload = new Dictionary<string, object?>
            {
                ["summary"] = it.Title,
                ["description"] = it.Rationale,
                ["start"] = new { dateTime = startDt.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = "UTC" },
                ["end"] = new { dateTime = endDt.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = "UTC" },
                ["extendedProperties"] = new { @private = new Dictionary<string, string> {
                    ["source"] = EventSourceMarker,
                    ["itemId"] = it.Id,
                }},
                ["reminders"] = new { useDefault = false, overrides = new[] { new { method = "popup", minutes = 15 } } },
            };
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(link.CalendarId)}/events")
            { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
            using var resp = await http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode) pushed++;
        }
        var tracked = await db.LearnerCalendarLinks.FirstAsync(x => x.Id == link.Id, ct);
        tracked.LastSyncedAt = DateTimeOffset.UtcNow;
        tracked.LastError = null;
        await db.SaveChangesAsync(ct);
        return pushed;
    }

    private async Task<string?> ExchangeRefreshAsync(LearnerCalendarLink link, CancellationToken ct)
    {
        try
        {
            var protector = dataProtection.CreateProtector(ProtectorPurpose);
            var refresh = protector.Unprotect(link.RefreshTokenEncrypted);
            var http = httpFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _opts.ClientId,
                    ["client_secret"] = _opts.ClientSecret,
                    ["refresh_token"] = refresh,
                    ["grant_type"] = "refresh_token",
                })
            };
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                var tracked = await db.LearnerCalendarLinks.FirstOrDefaultAsync(x => x.Id == link.Id, ct);
                if (tracked is not null) { tracked.LastError = err; await db.SaveChangesAsync(ct); }
                return null;
            }
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("access_token").GetString();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to refresh Google access token");
            return null;
        }
    }
}
