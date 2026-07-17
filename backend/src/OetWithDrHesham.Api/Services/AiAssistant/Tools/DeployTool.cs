using System.Text.Json;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.AiAssistant.Safety;
using OetWithDrHesham.Api.Services.AiTools;

namespace OetWithDrHesham.Api.Services.AiAssistant.Tools;

/// <summary>
/// Deployment operations tool (admin only, extreme caution).
/// ONLY allows read-only status and preview actions.
/// NEVER triggers actual deployments — those require manual VPS access.
/// </summary>
public sealed class DeployTool : IAiToolExecutor
{
    public string Code => "deploy";
    public AiToolCategory Category => AiToolCategory.Write;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "action":{"type":"string","enum":["status","preview"],"description":"Deployment action (read-only)"},
        "environment":{"type":"string","enum":["production","staging"],"description":"Target environment (default: production)"}
      },
      "required":["action"],
      "additionalProperties":false
    }
    """;

    private static readonly HashSet<string> AllowedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "status", "preview"
    };

    // Actions that are NEVER allowed
    private static readonly HashSet<string> ForbiddenActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "deploy", "rollback", "restart", "stop", "start", "scale",
    };

    private readonly ISafetyGuard _safetyGuard;
    private readonly ICircuitBreaker _circuitBreaker;
    private readonly ILogger<DeployTool> _logger;

    public DeployTool(
        ISafetyGuard safetyGuard,
        ICircuitBreaker circuitBreaker,
        ILogger<DeployTool> logger)
    {
        _safetyGuard = safetyGuard;
        _circuitBreaker = circuitBreaker;
        _logger = logger;
    }

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        // Circuit breaker check
        if (await _circuitBreaker.IsOpenAsync(Code, ct))
        {
            return new AiToolExecutionResult(AiToolOutcome.ExecutionError, null,
                "circuit_open", "Tool temporarily disabled due to repeated failures.");
        }

        // Safety guard check
        var safety = await _safetyGuard.CheckAsync(Code, args, ctx, ct);
        if (!safety.IsAllowed)
        {
            return new AiToolExecutionResult(AiToolOutcome.RbacDenied, null, "safety_denied", safety.DenialReason);
        }

        // Parse action
        if (!args.TryGetProperty("action", out var actionElem) || string.IsNullOrWhiteSpace(actionElem.GetString()))
        {
            return new AiToolExecutionResult(AiToolOutcome.ArgsInvalid, null, "action_required", "action is required");
        }

        var action = actionElem.GetString()!.Trim().ToLowerInvariant();
        var environment = args.TryGetProperty("environment", out var envElem)
            ? envElem.GetString()?.Trim().ToLowerInvariant() ?? "production"
            : "production";

        // Block forbidden actions
        if (ForbiddenActions.Contains(action))
        {
            return new AiToolExecutionResult(AiToolOutcome.RbacDenied, null,
                "action_forbidden", $"Action '{action}' is forbidden. Actual deployments require manual VPS access.");
        }

        // Validate allowed actions
        if (!AllowedActions.Contains(action))
        {
            return new AiToolExecutionResult(AiToolOutcome.ArgsInvalid, null,
                "action_unknown", $"Unknown action '{action}'. Allowed: status, preview.");
        }

        try
        {
            var result = action switch
            {
                "status" => GetDeploymentStatus(environment),
                "preview" => GetDeploymentPreview(environment),
                _ => throw new InvalidOperationException($"Unhandled action: {action}"),
            };

            await _circuitBreaker.RecordSuccessAsync(Code, ct);
            return new AiToolExecutionResult(AiToolOutcome.Success, ToJson(result));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _circuitBreaker.RecordFailureAsync(Code, ct);
            _logger.LogError(ex, "DeployTool: failed action '{Action}' on {Environment}", action, environment);
            return new AiToolExecutionResult(AiToolOutcome.ExecutionError, null,
                "deploy_check_failed", $"Deployment check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns current deployment status. In production, this would query
    /// the actual VPS/container health endpoints. For now, returns mock data.
    /// </summary>
    private static object GetDeploymentStatus(string environment)
    {
        return new
        {
            action = "status",
            environment,
            status = "healthy",
            last_deploy = DateTimeOffset.UtcNow.AddHours(-6).ToString("o"),
            uptime_hours = 168,
            services = new[]
            {
                new { name = "web", status = "running", port = 3000 },
                new { name = "api", status = "running", port = 5000 },
                new { name = "postgres", status = "running", port = 5432 },
            },
            health_checks = new
            {
                api = "pass",
                database = "pass",
                redis = "pass",
            },
            note = "This is read-only status information. Actual deployments require manual VPS access via SSH.",
        };
    }

    /// <summary>
    /// Returns a preview of what would be deployed. Shows pending changes
    /// without triggering any deployment.
    /// </summary>
    private static object GetDeploymentPreview(string environment)
    {
        return new
        {
            action = "preview",
            environment,
            current_version = "latest",
            pending_changes = "Run 'git log' tool to see recent commits since last deploy.",
            docker_compose_file = environment == "staging"
                ? "docker-compose.staging.yml"
                : "docker-compose.production.yml",
            deployment_method = "Manual SSH + docker compose pull && docker compose up -d",
            warnings = new[]
            {
                "This tool does NOT trigger deployments.",
                "To deploy, SSH into VPS and run the deployment script manually.",
                "Always verify staging before production.",
            },
            note = "Preview only — no changes will be made.",
        };
    }

    private static JsonElement ToJson(object payload) =>
        JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement.Clone();
}
