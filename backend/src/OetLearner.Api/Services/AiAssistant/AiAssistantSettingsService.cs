using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace OetLearner.Api.Services.AiAssistant;

/// <summary>
/// V1 settings for the AI Assistant. Backed by IConfiguration (env vars).
/// Phase 1.5 will migrate to IRuntimeSettingsProvider so the kill-switch can
/// be flipped live from /admin/settings without a restart. Until then, the
/// kill-switch endpoint writes to an in-memory override that wins over
/// configuration.
/// </summary>
public sealed class AiAssistantSettings
{
    public bool GlobalEnabled { get; set; }
    public bool RequireApprovalAlways { get; set; } = true;
    public string DefaultProvider { get; set; } = "OpenAi";
    public string DefaultModel { get; set; } = "gpt-4o-mini";
}

public interface IAiAssistantSettingsService
{
    AiAssistantSettings Current { get; }
    void SetEnabled(bool enabled, string actorUserId);
    DateTimeOffset? LastKillSwitchAt { get; }
    string? LastKillSwitchActor { get; }
}

public sealed class AiAssistantSettingsService : IAiAssistantSettingsService
{
    private readonly object _lock = new();
    private AiAssistantSettings _current;
    private DateTimeOffset? _lastKillSwitchAt;
    private string? _lastKillSwitchActor;

    public AiAssistantSettingsService(IConfiguration config)
    {
        var section = config.GetSection("AiAssistant");
        _current = new AiAssistantSettings
        {
            GlobalEnabled = section.GetValue<bool?>("GlobalEnabled") ?? false,
            RequireApprovalAlways = section.GetValue<bool?>("RequireApprovalAlways") ?? true,
            DefaultProvider = section["DefaultProvider"] ?? "OpenAi",
            DefaultModel = section["DefaultModel"] ?? "gpt-4o-mini",
        };
    }

    public AiAssistantSettings Current
    {
        get { lock (_lock) { return _current; } }
    }

    public void SetEnabled(bool enabled, string actorUserId)
    {
        lock (_lock)
        {
            _current = new AiAssistantSettings
            {
                GlobalEnabled = enabled,
                RequireApprovalAlways = _current.RequireApprovalAlways,
                DefaultProvider = _current.DefaultProvider,
                DefaultModel = _current.DefaultModel,
            };
            _lastKillSwitchAt = DateTimeOffset.UtcNow;
            _lastKillSwitchActor = actorUserId;
        }
    }

    public DateTimeOffset? LastKillSwitchAt { get { lock (_lock) { return _lastKillSwitchAt; } } }
    public string? LastKillSwitchActor { get { lock (_lock) { return _lastKillSwitchActor; } } }
}
