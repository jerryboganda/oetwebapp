using System.Collections.Concurrent;
using System.Text.Json;
using OetWithDrHesham.Api.Services.AiTools;

namespace OetWithDrHesham.Api.Services.AiAssistant.Safety;

/// <summary>
/// Production safety guard implementation. Enforces:
/// - Admin-only access for mutation tools
/// - Rate limiting (10 mutations/minute/user)
/// - Path validation within allowed directories
/// - Content scanning for secrets before writes
/// - Deployment lock check
/// </summary>
public sealed class SafetyGuard : ISafetyGuard
{
    private static readonly HashSet<string> MutationToolCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "write_file", "run_command", "git_operations", "deploy"
    };

    private static readonly string[] AllowedDirectoryPrefixes =
    [
        "app/", "app\\",
        "components/", "components\\",
        "lib/", "lib\\",
        "backend/src/", "backend\\src\\",
    ];

    private static readonly HashSet<string> BlockedFilePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        ".env", ".env.local", ".env.production", ".env.development",
        "secrets.json", "appsettings.secrets.json",
        "id_rsa", "id_ed25519", "id_ecdsa",
    };

    private static readonly HashSet<string> BlockedPathSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "bin", "obj",
    };

    // Rate limiting: per-user mutation timestamps
    private static readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> RateLimitState = new();
    private const int MaxMutationsPerMinute = 10;

    // Deployment lock flag (can be set externally)
    private static volatile bool _isDeploymentActive;
    public static bool IsDeploymentActive { get => _isDeploymentActive; set => _isDeploymentActive = value; }

    // Admin user IDs (in production, this would come from claims/roles)
    private static readonly HashSet<string> AdminUserIds = new(StringComparer.OrdinalIgnoreCase);
    public static void RegisterAdmin(string userId) => AdminUserIds.Add(userId);
    public static void UnregisterAdmin(string userId) => AdminUserIds.Remove(userId);

    private readonly ISecretScanner _secretScanner;
    private readonly ILogger<SafetyGuard> _logger;

    public SafetyGuard(ISecretScanner secretScanner, ILogger<SafetyGuard> logger)
    {
        _secretScanner = secretScanner;
        _logger = logger;
    }

    public Task<SafetyCheckResult> CheckAsync(string toolCode, JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        // Non-mutation tools pass through
        if (!MutationToolCodes.Contains(toolCode))
            return Task.FromResult(new SafetyCheckResult(true, null, SafetyRiskLevel.None));

        // 1. Admin-only check
        if (string.IsNullOrEmpty(ctx.UserId) || !AdminUserIds.Contains(ctx.UserId))
        {
            _logger.LogWarning("SafetyGuard: Non-admin user {UserId} attempted mutation tool {Tool}", ctx.UserId, toolCode);
            return Task.FromResult(new SafetyCheckResult(false, "Mutation tools require admin role.", SafetyRiskLevel.Critical));
        }

        // 2. Deployment lock check
        if (_isDeploymentActive)
        {
            return Task.FromResult(new SafetyCheckResult(false, "Mutations blocked during active deployment.", SafetyRiskLevel.High));
        }

        // 3. Rate limiting
        var rateLimitKey = $"{ctx.UserId}:mutations";
        var now = DateTimeOffset.UtcNow;
        var window = now.AddMinutes(-1);

        var queue = RateLimitState.GetOrAdd(rateLimitKey, _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            // Evict expired entries
            while (queue.Count > 0 && queue.Peek() < window)
                queue.Dequeue();

            if (queue.Count >= MaxMutationsPerMinute)
            {
                _logger.LogWarning("SafetyGuard: Rate limit exceeded for user {UserId}", ctx.UserId);
                return Task.FromResult(new SafetyCheckResult(false,
                    $"Rate limit exceeded: max {MaxMutationsPerMinute} mutations per minute.", SafetyRiskLevel.Medium));
            }

            queue.Enqueue(now);
        }

        // 4. Path validation for write_file
        if (toolCode == "write_file" && args.TryGetProperty("path", out var pathElem))
        {
            var path = pathElem.GetString();
            if (!string.IsNullOrEmpty(path))
            {
                var pathCheck = ValidatePath(path);
                if (!pathCheck.IsAllowed)
                    return Task.FromResult(pathCheck);
            }
        }

        // 5. Content scanning for write_file
        if (toolCode == "write_file" && args.TryGetProperty("content", out var contentElem))
        {
            var content = contentElem.GetString();
            if (!string.IsNullOrEmpty(content))
            {
                var scanResult = _secretScanner.Scan(content);
                if (scanResult.HasSecrets)
                {
                    var types = string.Join(", ", scanResult.Findings.Select(f => f.Type).Distinct());
                    _logger.LogWarning("SafetyGuard: Secret detected in write_file content: {Types}", types);
                    return Task.FromResult(new SafetyCheckResult(false,
                        $"Content contains potential secrets ({types}). Remove secrets before writing.", SafetyRiskLevel.Critical));
                }
            }
        }

        // Determine risk level
        var riskLevel = toolCode switch
        {
            "deploy" => SafetyRiskLevel.High,
            "run_command" => SafetyRiskLevel.High,
            "git_operations" => args.TryGetProperty("operation", out var op) && op.GetString() == "commit"
                ? SafetyRiskLevel.Medium
                : SafetyRiskLevel.Low,
            "write_file" => SafetyRiskLevel.Medium,
            _ => SafetyRiskLevel.Low,
        };

        return Task.FromResult(new SafetyCheckResult(true, null, riskLevel));
    }

    private static SafetyCheckResult ValidatePath(string path)
    {
        // Normalize separators
        var normalized = path.Replace('\\', '/').TrimStart('/');

        // Block path traversal
        if (normalized.Contains(".."))
            return new SafetyCheckResult(false, "Path traversal (..) is not allowed.", SafetyRiskLevel.Critical);

        // Check filename against blocked patterns
        var fileName = Path.GetFileName(normalized);
        if (BlockedFilePatterns.Contains(fileName))
            return new SafetyCheckResult(false, $"Writing to '{fileName}' is blocked for security.", SafetyRiskLevel.Critical);

        // Check path segments
        var segments = normalized.Split('/');
        foreach (var segment in segments)
        {
            if (BlockedPathSegments.Contains(segment))
                return new SafetyCheckResult(false, $"Writing to paths containing '{segment}' is blocked.", SafetyRiskLevel.High);
        }

        // Check allowed directory prefixes
        var isAllowed = AllowedDirectoryPrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (!isAllowed)
            return new SafetyCheckResult(false,
                $"Path '{normalized}' is outside allowed directories (app/, components/, lib/, backend/src/).", SafetyRiskLevel.High);

        return new SafetyCheckResult(true, null, SafetyRiskLevel.None);
    }
}
