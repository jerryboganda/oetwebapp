namespace OetLearner.Api.Services.AiAssistant.Safety;

/// <summary>
/// File backup service for mutation tools. Stores file content in the database
/// before writes, enabling rollback.
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Creates a backup of the specified file. Returns the backup ID.
    /// </summary>
    Task<string> BackupFileAsync(string filePath, string userId, CancellationToken ct);

    /// <summary>
    /// Restores a file from a backup record.
    /// </summary>
    Task RestoreFileAsync(string backupId, CancellationToken ct);

    /// <summary>
    /// Lists recent backups, optionally filtered by user.
    /// </summary>
    Task<List<FileBackupInfo>> ListBackupsAsync(string? userId, int take, CancellationToken ct);
}

public sealed record FileBackupInfo(
    string Id,
    string OriginalPath,
    string ContentHash,
    string UserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RestoredAt);
