using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Twilio SMS channel for billing notifications. Looks up the learner's phone
/// number from the user account (LearnerUser.Profile JSON when present);
/// silently skips when Twilio is unconfigured or the user has no phone.
/// </summary>
public sealed class TwilioSmsChannel : IBillingNotificationChannel
{
    public string Channel => "sms";

    private readonly HttpClient _http;
    private readonly LearnerDbContext _db;
    private readonly IOptions<TwilioOptions> _options;
    private readonly ILogger<TwilioSmsChannel> _logger;

    public TwilioSmsChannel(HttpClient http, LearnerDbContext db, IOptions<TwilioOptions> options, ILogger<TwilioSmsChannel> logger)
    {
        _http = http;
        _db = db;
        _options = options;
        _logger = logger;
    }

    public async Task SendAsync(string userId, string subject, string body, CancellationToken ct)
    {
        var opts = _options.Value;
        if (!opts.Enabled || string.IsNullOrWhiteSpace(opts.AccountSid) || string.IsNullOrWhiteSpace(opts.AuthToken))
        {
            _logger.LogDebug("Twilio disabled — skipping SMS to {UserId}", userId);
            return;
        }

        var phone = await ResolvePhoneAsync(userId, ct);
        if (string.IsNullOrWhiteSpace(phone))
        {
            _logger.LogDebug("No phone on file for {UserId} — skipping SMS", userId);
            return;
        }

        var url = $"{opts.ApiBaseUrl.TrimEnd('/')}/2010-04-01/Accounts/{opts.AccountSid}/Messages.json";
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", phone),
            new("Body", TruncateForSms(body)),
        };
        if (!string.IsNullOrWhiteSpace(opts.MessagingServiceSid))
        {
            form.Add(new("MessagingServiceSid", opts.MessagingServiceSid));
        }
        else if (!string.IsNullOrWhiteSpace(opts.FromNumber))
        {
            form.Add(new("From", opts.FromNumber));
        }
        else
        {
            _logger.LogWarning("Twilio configured but no FromNumber/MessagingServiceSid; aborting SMS to {UserId}", userId);
            return;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{opts.AccountSid}:{opts.AuthToken}")));
        req.Content = new FormUrlEncodedContent(form);

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("Twilio send failed ({Status}): {Body}", (int)resp.StatusCode, err);
            throw new InvalidOperationException($"Twilio SMS failed: {(int)resp.StatusCode}");
        }
    }

    private async Task<string?> ResolvePhoneAsync(string userId, CancellationToken ct)
    {
        var phone = await _db.LearnerRegistrationProfiles
            .Where(p => p.LearnerUserId == userId || p.ApplicationUserAccountId == userId)
            .Select(p => p.MobileNumber)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(phone) ? null : phone;
    }

    /// <summary>Twilio caps SMS bodies at 1600 chars. We keep it well below to avoid concatenation surprises.</summary>
    private static string TruncateForSms(string body)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;
        return body.Length <= 480 ? body : body.Substring(0, 477) + "...";
    }
}
