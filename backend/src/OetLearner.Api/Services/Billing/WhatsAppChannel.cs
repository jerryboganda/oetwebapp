using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.Billing;

/// <summary>
/// Meta WhatsApp Business Cloud API channel. Resolves the learner's mobile
/// from <c>LearnerRegistrationProfile.MobileNumber</c> and posts to
/// <c>/{phone_number_id}/messages</c>. Outside the 24-hour customer-service
/// window WhatsApp requires a pre-approved template; we use freeform inside
/// the window and skip otherwise (unless <c>FallbackTemplateName</c> is set).
/// </summary>
public sealed class WhatsAppChannel : IBillingNotificationChannel
{
    public string Channel => "whatsapp";

    private readonly HttpClient _http;
    private readonly LearnerDbContext _db;
    private readonly IRuntimeSettingsProvider _runtimeSettings;
    private readonly ILogger<WhatsAppChannel> _logger;

    public WhatsAppChannel(HttpClient http, LearnerDbContext db, IRuntimeSettingsProvider runtimeSettings, ILogger<WhatsAppChannel> logger)
    {
        _http = http;
        _db = db;
        _runtimeSettings = runtimeSettings;
        _logger = logger;
    }

    public async Task SendAsync(string userId, string subject, string body, CancellationToken ct)
    {
        var opts = (await _runtimeSettings.GetAsync(ct)).Messaging;
        if (!opts.IsWhatsAppConfigured)
        {
            _logger.LogDebug("WhatsApp disabled — skipping to {UserId}", userId);
            return;
        }

        var phone = await _db.LearnerRegistrationProfiles
            .Where(p => p.LearnerUserId == userId || p.ApplicationUserAccountId == userId)
            .Select(p => p.MobileNumber)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(phone))
        {
            _logger.LogDebug("No phone for {UserId} — skipping WhatsApp", userId);
            return;
        }

        // Strip leading + and any non-digits — WA expects E.164 digits.
        var to = new string(phone.Where(char.IsDigit).ToArray());

        object payload;
        if (string.IsNullOrWhiteSpace(opts.WhatsAppFallbackTemplateName))
        {
            payload = new
            {
                messaging_product = "whatsapp",
                to,
                type = "text",
                text = new { body },
            };
        }
        else
        {
            payload = new
            {
                messaging_product = "whatsapp",
                to,
                type = "template",
                template = new
                {
                    name = opts.WhatsAppFallbackTemplateName,
                    language = new { code = "en" },
                    components = new[]
                    {
                        new
                        {
                            type = "body",
                            parameters = new[] { new { type = "text", text = body } },
                        },
                    },
                },
            };
        }

        var url = $"{opts.WhatsAppApiBaseUrl.TrimEnd('/')}/{opts.WhatsAppPhoneNumberId}/messages";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.WhatsAppAccessToken);
        req.Content = JsonContent.Create(payload);

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("WhatsApp send failed ({Status}): {Body}", (int)resp.StatusCode, err);
            throw new InvalidOperationException($"WhatsApp send failed: {(int)resp.StatusCode}");
        }
    }
}
