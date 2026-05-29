using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public sealed class NotificationRule
{
    public Guid Id { get; set; }
    public string EventKey { get; set; } = default!;
    public string? AudienceRole { get; set; }
    public string Channels { get; set; } = "InApp,Email,Push";
    public int Priority { get; set; } = 5;
    public int? DelaySeconds { get; set; }
    public int? ExpiryMinutes { get; set; }
    public string? FallbackChannels { get; set; }
    public string? RequiredConsentCategory { get; set; }
    public bool BypassQuietHours { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class NotificationRuleEngine(LearnerDbContext db, IMemoryCache cache, ILogger<NotificationRuleEngine> logger)
{
    private const string CacheKey = "NotificationRules_All";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<IReadOnlyList<NotificationRule>> ResolveRulesAsync(
        string eventKey, string? recipientRole, CancellationToken cancellationToken = default)
    {
        var allRules = await GetActiveRulesAsync(cancellationToken);

        var matched = allRules
            .Where(r => r.EventKey == eventKey || r.EventKey == "*")
            .Where(r => r.AudienceRole is null || string.Equals(r.AudienceRole, recipientRole, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Priority)
            .ThenByDescending(r => r.EventKey == eventKey ? 1 : 0) // prefer specific over wildcard
            .ToList();

        if (matched.Count == 0)
        {
            logger.LogDebug("No notification rule matched event={EventKey} role={Role}; using defaults", eventKey, recipientRole);
        }

        return matched;
    }

    public async Task<IReadOnlyList<NotificationChannel>> GetChannelsForEventAsync(
        string eventKey, string? recipientRole, CancellationToken cancellationToken = default)
    {
        var rules = await ResolveRulesAsync(eventKey, recipientRole, cancellationToken);

        if (rules.Count == 0)
        {
            // Default: InApp always; Email+Push for priority <= 5
            return [NotificationChannel.InApp, NotificationChannel.Email, NotificationChannel.Push];
        }

        var topRule = rules[0];
        return ParseChannels(topRule.Channels);
    }

    public async Task<bool> ShouldBypassQuietHoursAsync(
        string eventKey, string? recipientRole, CancellationToken cancellationToken = default)
    {
        var rules = await ResolveRulesAsync(eventKey, recipientRole, cancellationToken);
        return rules.Count > 0 && rules[0].BypassQuietHours;
    }

    public async Task<TimeSpan?> GetDelayAsync(
        string eventKey, string? recipientRole, CancellationToken cancellationToken = default)
    {
        var rules = await ResolveRulesAsync(eventKey, recipientRole, cancellationToken);
        if (rules.Count == 0) return null;

        var delay = rules[0].DelaySeconds;
        return delay.HasValue ? TimeSpan.FromSeconds(delay.Value) : null;
    }

    public async Task<TimeSpan?> GetExpiryAsync(
        string eventKey, string? recipientRole, CancellationToken cancellationToken = default)
    {
        var rules = await ResolveRulesAsync(eventKey, recipientRole, cancellationToken);
        if (rules.Count == 0) return null;

        var expiry = rules[0].ExpiryMinutes;
        return expiry.HasValue ? TimeSpan.FromMinutes(expiry.Value) : null;
    }

    public async Task<IReadOnlyList<NotificationChannel>> GetFallbackChannelsAsync(
        string eventKey, string? recipientRole, CancellationToken cancellationToken = default)
    {
        var rules = await ResolveRulesAsync(eventKey, recipientRole, cancellationToken);
        if (rules.Count == 0) return [];

        var fallback = rules[0].FallbackChannels;
        return string.IsNullOrWhiteSpace(fallback) ? [] : ParseChannels(fallback);
    }

    private async Task<IReadOnlyList<NotificationRule>> GetActiveRulesAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<NotificationRule>? cached) && cached is not null)
            return cached;

        var rules = await db.Set<NotificationRule>()
            .AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.Priority)
            .ToListAsync(cancellationToken);

        cache.Set(CacheKey, (IReadOnlyList<NotificationRule>)rules, CacheDuration);
        return rules;
    }

    private static IReadOnlyList<NotificationChannel> ParseChannels(string channelsString)
    {
        var channels = new List<NotificationChannel>();
        foreach (var part in channelsString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<NotificationChannel>(part, ignoreCase: true, out var channel))
                channels.Add(channel);
        }
        return channels;
    }
}
