using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;

namespace OetWithDrHesham.Api.Services.AiAssistant.Safety;

/// <summary>
/// Database-backed file backup service. Stores full file content with SHA-256
/// integrity hash. Auto-cleanup of backups older than 30 days happens on list queries.
/// </summary>
public sealed class BackupService : IBackupService
{
    private readonly LearnerDbContext _db;
    private readonly ILogger<BackupService> _logger;

    public BackupService(LearnerDbContext db, ILogger<BackupService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<string> BackupFileAsync(string filePath, string userId, CancellationToken ct)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Cannot backup non-existent file: {filePath}");

        var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8, ct);
        var hash = ComputeSha256(content);
        var id = Guid.NewGuid().ToString("N");

        var backup = new AiFileBackup
        {
            Id = id,
            OriginalPath = filePath,
            BackupContent = content,
            ContentHash = hash,
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.Set<AiFileBackup>().Add(backup);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Backup created: {BackupId} for {FilePath} by {UserId}", id, filePath, userId);
        return id;
    }

    public async Task RestoreFileAsync(string backupId, CancellationToken ct)
    {
        var backup = await _db.Set<AiFileBackup>()
            .FirstOrDefaultAsync(b => b.Id == backupId, ct)
            ?? throw new InvalidOperationException($"Backup not found: {backupId}");

        // Verify integrity before restore
        var hash = ComputeSha256(backup.BackupContent);
        if (hash != backup.ContentHash)
            throw new InvalidOperationException($"Backup integrity check failed for {backupId}");

        var dir = Path.GetDirectoryName(backup.OriginalPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(backup.OriginalPath, backup.BackupContent, Encoding.UTF8, ct);
        backup.RestoredAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Backup restored: {BackupId} to {FilePath}", backupId, backup.OriginalPath);
    }

    public async Task<List<FileBackupInfo>> ListBackupsAsync(string? userId, int take, CancellationToken ct)
    {
        // Auto-cleanup: remove backups older than 30 days
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var expired = await _db.Set<AiFileBackup>()
            .Where(b => b.CreatedAt < cutoff)
            .ToListAsync(ct);

        if (expired.Count > 0)
        {
            _db.Set<AiFileBackup>().RemoveRange(expired);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Cleaned up {Count} expired backups", expired.Count);
        }

        var query = _db.Set<AiFileBackup>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(userId))
            query = query.Where(b => b.UserId == userId);

        return await query
            .OrderByDescending(b => b.CreatedAt)
            .Take(Math.Clamp(take, 1, 100))
            .Select(b => new FileBackupInfo(
                b.Id, b.OriginalPath, b.ContentHash, b.UserId, b.CreatedAt, b.RestoredAt))
            .ToListAsync(ct);
    }

    private static string ComputeSha256(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

/// <summary>
/// Entity for storing file backups in the database.
/// </summary>
public class AiFileBackup
{
    public string Id { get; set; } = default!;
    public string OriginalPath { get; set; } = default!;
    public string BackupContent { get; set; } = default!;
    public string ContentHash { get; set; } = default!;
    public string UserId { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RestoredAt { get; set; }
}
