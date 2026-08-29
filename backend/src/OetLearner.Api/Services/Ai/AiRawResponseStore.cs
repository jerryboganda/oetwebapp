using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Ai;

public interface IAiRawResponseStore
{
    Task CaptureAsync(
        string featureCode,
        string? operationId,
        string? providerCode,
        string plaintext,
        CancellationToken ct);

    Task<int> PurgeExpiredAsync(int batchSize, CancellationToken ct);
}

public sealed class AiRawResponseStore(
    LearnerDbContext db,
    IDataProtectionProvider dataProtection) : IAiRawResponseStore
{
    public static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(7);
    private readonly IDataProtector _protector = dataProtection.CreateProtector("AiRawResponse.v1");

    public async Task CaptureAsync(
        string featureCode,
        string? operationId,
        string? providerCode,
        string plaintext,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var bytes = Encoding.UTF8.GetBytes(plaintext ?? string.Empty);
        db.AiRawResponses.Add(new AiRawResponse
        {
            Id = Guid.NewGuid().ToString("N"),
            OperationId = operationId,
            FeatureCode = featureCode,
            ProviderCode = providerCode,
            PayloadCiphertext = _protector.Protect(plaintext ?? string.Empty),
            PayloadSha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            CreatedAt = now,
            ExpiresAt = now + RetentionWindow,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> PurgeExpiredAsync(int batchSize, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var take = Math.Max(1, batchSize);
        var rows = await db.AiRawResponses
            .Where(r => r.ExpiresAt < now && r.PayloadCiphertext != null)
            .OrderBy(r => r.ExpiresAt)
            .Take(take)
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            row.PayloadCiphertext = null;
            row.PurgedAt = now;
        }

        if (rows.Count > 0)
            await db.SaveChangesAsync(ct);

        return rows.Count;
    }
}
