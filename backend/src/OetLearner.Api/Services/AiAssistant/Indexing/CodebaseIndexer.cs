using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.AiAssistant.Indexing;

/// <summary>
/// Background indexer that scans the repository for supported files, chunks them,
/// generates embeddings, and stores them in the database.
///
/// Supported file types: .ts, .tsx, .cs, .json, .md, .yaml, .yml
/// Skipped directories: node_modules, .git, dist, coverage, .next, bin, obj
///
/// Uses SHA-256 content hashing for deduplication — unchanged chunks are not re-indexed.
/// Processes files in batches of 10.
/// </summary>
public sealed class CodebaseIndexer : ICodebaseIndexer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICodeChunker _chunker;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<CodebaseIndexer> _logger;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ts", ".tsx", ".cs", ".json", ".md", ".yaml", ".yml"
    };

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "dist", "coverage", ".next", "bin", "obj",
        ".vs", ".idea", "wwwroot", "Migrations"
    };

    private const int FileBatchSize = 10;

    // In-memory progress tracking
    private volatile bool _isRunning;
    private int _totalFiles;
    private int _indexedFiles;
    private DateTimeOffset? _lastCompleted;

    public CodebaseIndexer(
        IServiceScopeFactory scopeFactory,
        ICodeChunker chunker,
        IEmbeddingService embeddingService,
        ILogger<CodebaseIndexer> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _chunker = chunker ?? throw new ArgumentNullException(nameof(chunker));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IndexingStatus> GetStatusAsync(CancellationToken ct)
    {
        return Task.FromResult(new IndexingStatus(_isRunning, _totalFiles, _indexedFiles, _lastCompleted));
    }

    public async Task IndexFullAsync(CancellationToken ct)
    {
        if (_isRunning)
        {
            _logger.LogWarning("Indexing already in progress — skipping.");
            return;
        }

        _isRunning = true;
        _indexedFiles = 0;

        try
        {
            var repoRoot = FindRepositoryRoot();
            if (repoRoot == null)
            {
                _logger.LogWarning("Could not locate repository root for indexing.");
                return;
            }

            var files = DiscoverFiles(repoRoot);
            _totalFiles = files.Count;
            _logger.LogInformation("Starting full codebase indexing: {Count} files found.", files.Count);

            for (int i = 0; i < files.Count; i += FileBatchSize)
            {
                ct.ThrowIfCancellationRequested();

                var batch = files.Skip(i).Take(FileBatchSize).ToList();
                await IndexBatchAsync(batch, repoRoot, ct);
                _indexedFiles += batch.Count;
            }

            _lastCompleted = DateTimeOffset.UtcNow;
            _logger.LogInformation("Full codebase indexing completed. {Count} files indexed.", _indexedFiles);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Codebase indexing was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during full codebase indexing.");
        }
        finally
        {
            _isRunning = false;
        }
    }

    public async Task IndexFileAsync(string filePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        var repoRoot = FindRepositoryRoot();
        if (repoRoot == null)
        {
            _logger.LogWarning("Could not locate repository root for single file indexing.");
            return;
        }

        var fullPath = Path.IsPathRooted(filePath)
            ? filePath
            : Path.Combine(repoRoot, filePath);

        if (!File.Exists(fullPath))
        {
            _logger.LogWarning("File not found for indexing: {FilePath}", filePath);
            return;
        }

        await IndexBatchAsync(new[] { fullPath }, repoRoot, ct);
    }

    private async Task IndexBatchAsync(IReadOnlyList<string> filePaths, string repoRoot, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var allChunks = new List<(CodeChunk chunk, string relativePath, string hash)>();

        foreach (var filePath in filePaths)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (!File.Exists(filePath))
                    continue;

                var content = await File.ReadAllTextAsync(filePath, ct);
                if (string.IsNullOrWhiteSpace(content))
                    continue;

                var relativePath = Path.GetRelativePath(repoRoot, filePath).Replace('\\', '/');
                var language = GetLanguageFromExtension(Path.GetExtension(filePath));
                var chunks = _chunker.ChunkFile(relativePath, content, language);

                foreach (var chunk in chunks)
                {
                    var hash = ComputeHash(chunk.Content);
                    allChunks.Add((chunk, relativePath, hash));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process file: {FilePath}", filePath);
            }
        }

        if (allChunks.Count == 0)
            return;

        // Check which chunks already exist with same hash (dedup)
        var hashes = allChunks.Select(c => c.hash).Distinct().ToList();
        var existingHashes = await db.AiCodebaseChunks
            .Where(c => hashes.Contains(c.ContentHash))
            .Select(c => c.ContentHash)
            .ToListAsync(ct);

        var existingHashSet = new HashSet<string>(existingHashes);

        var newChunks = allChunks.Where(c => !existingHashSet.Contains(c.hash)).ToList();
        if (newChunks.Count == 0)
            return;

        // Remove old chunks for files we're re-indexing
        var filesToReindex = newChunks.Select(c => c.relativePath).Distinct().ToList();
        var oldChunks = await db.AiCodebaseChunks
            .Where(c => filesToReindex.Contains(c.FilePath))
            .ToListAsync(ct);
        if (oldChunks.Count > 0)
        {
            db.AiCodebaseChunks.RemoveRange(oldChunks);
        }

        // Generate embeddings for new chunks
        var texts = newChunks.Select(c => c.chunk.Content).ToList();
        var embeddings = await _embeddingService.EmbedBatchAsync(texts, ct);

        // Store new chunks
        for (int i = 0; i < newChunks.Count; i++)
        {
            var (chunk, relativePath, hash) = newChunks[i];
            var entity = new AiCodebaseChunk
            {
                Id = Guid.NewGuid().ToString("N"),
                FilePath = relativePath,
                StartLine = chunk.StartLine,
                EndLine = chunk.EndLine,
                Content = chunk.Content,
                Language = chunk.Language,
                ChunkType = "block",
                SymbolName = chunk.Symbol,
                Embedding = embeddings[i],
                ContentHash = hash,
                IndexedAt = DateTimeOffset.UtcNow
            };
            db.AiCodebaseChunks.Add(entity);
        }

        await db.SaveChangesAsync(ct);
        _logger.LogDebug("Indexed {Count} new chunks from {FileCount} files.", newChunks.Count, filesToReindex.Count);
    }

    private List<string> DiscoverFiles(string rootPath)
    {
        var files = new List<string>();

        try
        {
            foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                if (!SupportedExtensions.Contains(ext))
                    continue;

                // Check if any segment of the path is a skipped directory
                var relativePath = Path.GetRelativePath(rootPath, file);
                var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (segments.Any(s => SkippedDirectories.Contains(s)))
                    continue;

                // Skip very large files (> 100KB)
                var fileInfo = new FileInfo(file);
                if (fileInfo.Length > 100 * 1024)
                    continue;

                files.Add(file);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error discovering files in {Root}", rootPath);
        }

        return files;
    }

    private static string? FindRepositoryRoot()
    {
        // Walk up from the current directory looking for .git
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        // Fallback: check common dev paths
        var cwd = Directory.GetCurrentDirectory();
        while (cwd != null)
        {
            if (Directory.Exists(Path.Combine(cwd, ".git")))
                return cwd;
            cwd = Directory.GetParent(cwd)?.FullName;
        }

        return null;
    }

    private static string GetLanguageFromExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".ts" => "typescript",
        ".tsx" => "tsx",
        ".cs" => "csharp",
        ".json" => "json",
        ".md" => "markdown",
        ".yaml" or ".yml" => "yaml",
        ".js" => "javascript",
        ".jsx" => "jsx",
        _ => "unknown"
    };

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
