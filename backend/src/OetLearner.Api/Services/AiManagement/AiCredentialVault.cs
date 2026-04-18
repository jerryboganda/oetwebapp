using System.Net.Http.Headers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.AiManagement;

// ═════════════════════════════════════════════════════════════════════════════
// BYOK credential vault — Slice 3
//
// Encrypts keys at rest with purpose-scoped ASP.NET Data Protection.
// Keys are NEVER returned to the client after save (only a KeyHint).
// Validate-on-save pings the provider once before storing.
// ═════════════════════════════════════════════════════════════════════════════

public interface IAiCredentialVault
{
    /// <summary>Add or replace the caller's credential for a provider. Runs
    /// a live validation against the provider unless
    /// <paramref name="skipValidation"/> is true (tests only).</summary>
    Task<AiCredentialSaveResult> UpsertAsync(
        string userId,
        string? authAccountId,
        string providerCode,
        string plaintextKey,
        string? modelAllowlistCsv,
        bool skipValidation,
        CancellationToken ct);

    Task<IReadOnlyList<AiCredentialListItem>> ListAsync(string userId, CancellationToken ct);

    Task<bool> RevokeAsync(string userId, string credentialId, CancellationToken ct);

    /// <summary>Used by the credential resolver (Slice 4). Returns the
    /// decrypted plaintext if a usable credential exists, otherwise null.</summary>
    Task<string?> ResolvePlaintextAsync(string userId, string providerCode, CancellationToken ct);

    /// <summary>Mark a credential invalid after an upstream 401/403 so the
    /// resolver will skip it until cooldown expires.</summary>
    Task MarkInvalidAsync(string credentialId, string errorCode, TimeSpan cooldown, CancellationToken ct);
}

public sealed record AiCredentialSaveResult(
    bool Success,
    string? CredentialId,
    string? KeyHint,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record AiCredentialListItem(
    string Id,
    string ProviderCode,
    string KeyHint,
    string Status,
    string? ModelAllowlistCsv,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? CooldownUntil);

public sealed class AiCredentialVault(
    LearnerDbContext db,
    IDataProtectionProvider dpProvider,
    IHttpClientFactory httpClientFactory,
    ILogger<AiCredentialVault> logger) : IAiCredentialVault
{
    private const string ProtectorPurpose = "UserAiCredential.v1";
    private readonly IDataProtector _protector = dpProvider.CreateProtector(ProtectorPurpose);

    public async Task<AiCredentialSaveResult> UpsertAsync(
        string userId, string? authAccountId, string providerCode,
        string plaintextKey, string? modelAllowlistCsv,
        bool skipValidation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(plaintextKey) || plaintextKey.Length < 16)
        {
            return new AiCredentialSaveResult(false, null, null, "key_too_short",
                "API key looks too short to be valid.");
        }

        providerCode = providerCode.Trim().ToLowerInvariant();

        // ── Validate-on-save ────────────────────────────────────────────────
        if (!skipValidation)
        {
            var (ok, validationError) = await ValidateKeyAsync(providerCode, plaintextKey, ct);
            if (!ok)
            {
                return new AiCredentialSaveResult(false, null, null, "validation_failed",
                    validationError ?? "Provider rejected this API key.");
            }
        }

        // ── Upsert ──────────────────────────────────────────────────────────
        var row = await db.UserAiCredentials
            .FirstOrDefaultAsync(x => x.UserId == userId && x.ProviderCode == providerCode, ct);

        var hint = plaintextKey.Length <= 8
            ? new string('•', plaintextKey.Length)
            : $"…{plaintextKey[^4..]}";
        var encrypted = _protector.Protect(plaintextKey);

        if (row is null)
        {
            row = new UserAiCredential
            {
                Id = Guid.NewGuid().ToString("N"),
                UserId = userId,
                AuthAccountId = authAccountId,
                ProviderCode = providerCode,
                EncryptedKey = encrypted,
                KeyHint = hint,
                ModelAllowlistCsv = modelAllowlistCsv ?? string.Empty,
                Status = AiCredentialStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.UserAiCredentials.Add(row);
        }
        else
        {
            row.EncryptedKey = encrypted;
            row.KeyHint = hint;
            row.ModelAllowlistCsv = modelAllowlistCsv ?? string.Empty;
            row.Status = AiCredentialStatus.Active;
            row.LastErrorAt = null;
            row.LastErrorCode = null;
            row.CooldownUntil = null;
        }

        await db.SaveChangesAsync(ct);
        return new AiCredentialSaveResult(true, row.Id, row.KeyHint, null, null);
    }

    public async Task<IReadOnlyList<AiCredentialListItem>> ListAsync(string userId, CancellationToken ct)
    {
        var rows = await db.UserAiCredentials.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.ProviderCode)
            .ToListAsync(ct);
        return rows.Select(r => new AiCredentialListItem(
            r.Id, r.ProviderCode, r.KeyHint,
            r.Status.ToString(), r.ModelAllowlistCsv,
            r.CreatedAt, r.LastUsedAt, r.CooldownUntil)).ToList();
    }

    public async Task<bool> RevokeAsync(string userId, string credentialId, CancellationToken ct)
    {
        var row = await db.UserAiCredentials
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Id == credentialId, ct);
        if (row is null) return false;
        row.Status = AiCredentialStatus.Revoked;
        row.EncryptedKey = string.Empty; // wipe ciphertext on revoke
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<string?> ResolvePlaintextAsync(string userId, string providerCode, CancellationToken ct)
    {
        providerCode = providerCode.Trim().ToLowerInvariant();
        var row = await db.UserAiCredentials
            .FirstOrDefaultAsync(x => x.UserId == userId && x.ProviderCode == providerCode, ct);
        if (row is null) return null;
        if (row.Status != AiCredentialStatus.Active) return null;
        if (row.CooldownUntil.HasValue && row.CooldownUntil > DateTimeOffset.UtcNow) return null;
        if (string.IsNullOrEmpty(row.EncryptedKey)) return null;
        try
        {
            var plaintext = _protector.Unprotect(row.EncryptedKey);
            row.LastUsedAt = DateTimeOffset.UtcNow;
            // Fire-and-forget timestamp update; failure here is non-fatal.
            try { await db.SaveChangesAsync(ct); } catch { /* ignore */ }
            return plaintext;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to decrypt AI credential {CredentialId}; marking invalid.", row.Id);
            row.Status = AiCredentialStatus.Invalid;
            row.LastErrorAt = DateTimeOffset.UtcNow;
            row.LastErrorCode = "decrypt_failed";
            try { await db.SaveChangesAsync(ct); } catch { /* ignore */ }
            return null;
        }
    }

    public async Task MarkInvalidAsync(string credentialId, string errorCode, TimeSpan cooldown, CancellationToken ct)
    {
        var row = await db.UserAiCredentials.FirstOrDefaultAsync(x => x.Id == credentialId, ct);
        if (row is null) return;
        row.LastErrorAt = DateTimeOffset.UtcNow;
        row.LastErrorCode = errorCode;
        if (errorCode is "401" or "403" or "byok_auth")
        {
            row.Status = AiCredentialStatus.Invalid;
            row.CooldownUntil = DateTimeOffset.UtcNow.Add(cooldown);
        }
        else
        {
            // Transient error — set a short cooldown but keep Active.
            row.CooldownUntil = DateTimeOffset.UtcNow.Add(TimeSpan.FromMinutes(Math.Min(10, cooldown.TotalMinutes)));
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Very thin "does this key auth?" check. Sends a tiny request to the
    /// provider's models-list endpoint (OpenAI/Anthropic/OpenRouter all
    /// expose something cheap). Any 2xx = valid. 401/403 = rejected. Other
    /// failures are tolerated so the UX does not block on transient
    /// network hiccups.
    /// </summary>
    private async Task<(bool ok, string? error)> ValidateKeyAsync(string providerCode, string plaintextKey, CancellationToken ct)
    {
        try
        {
            using var client = httpClientFactory.CreateClient("AiCredentialValidator");
            client.Timeout = TimeSpan.FromSeconds(8);

            HttpResponseMessage response = providerCode switch
            {
                "anthropic" => await AnthropicPing(client, plaintextKey, ct),
                "openai-platform" => await OpenAiPing(client, "https://api.openai.com/v1", plaintextKey, ct),
                "openrouter" => await OpenAiPing(client, "https://openrouter.ai/api/v1", plaintextKey, ct),
                // Unknown provider: accept without validation; resolver will
                // catch a real failure on first use.
                _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK),
            };

            if (response.IsSuccessStatusCode) return (true, null);
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                or System.Net.HttpStatusCode.Forbidden)
            {
                return (false, "The provider rejected this API key (401/403).");
            }

            // Anything else (429, 5xx): treat as transient and accept.
            logger.LogInformation("BYOK validate returned {StatusCode} for {Provider}; accepting key.",
                (int)response.StatusCode, providerCode);
            return (true, null);
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "BYOK validate ping failed for {Provider}; accepting key.", providerCode);
            return (true, null);
        }
    }

    private static async Task<HttpResponseMessage> OpenAiPing(HttpClient client, string baseUrl, string key, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/models");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return await client.SendAsync(req, ct);
    }

    private static async Task<HttpResponseMessage> AnthropicPing(HttpClient client, string key, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models");
        req.Headers.Add("x-api-key", key);
        req.Headers.Add("anthropic-version", "2023-06-01");
        return await client.SendAsync(req, ct);
    }
}
