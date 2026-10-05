using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Fleet.Manager.Persistence;

/// <summary>One forward-only, hand-written upgrade step: the statements run in a single transaction.</summary>
public sealed record SchemaUpgrade(int ToVersion, IReadOnlyList<string> Statements);

/// <summary>
/// Creates and upgrades the manager database (OET-RWP/1 section 8.3). Policy:
/// <list type="bullet">
/// <item>a fresh file gets WAL journaling, the model's tables via <c>EnsureCreated</c>, and a
/// <c>schema_info</c> row stamped with <see cref="CurrentVersion"/>;</item>
/// <item>an existing file must contain <c>schema_info</c> (otherwise it is not ours and startup fails);</item>
/// <item>a file stamped NEWER than this build is refused (never downgrade, never guess);</item>
/// <item>an older file is upgraded by the numbered SQL steps in <see cref="Upgrades"/>, in order,
/// each in its own transaction, stamping the version after each one.</item>
/// </list>
/// </summary>
public sealed class SchemaManager
{
    public const int CurrentVersion = 1;

    /// <summary>Empty at version 1. Add <c>new(2, new[] { "ALTER TABLE ..." })</c> and bump <see cref="CurrentVersion"/> together.</summary>
    public static readonly IReadOnlyList<SchemaUpgrade> Upgrades = Array.Empty<SchemaUpgrade>();

    private readonly IDbContextFactory<FleetDbContext> _factory;
    private readonly TimeProvider _time;
    private readonly ILogger<SchemaManager> _logger;

    public SchemaManager(IDbContextFactory<FleetDbContext> factory, TimeProvider time, ILogger<SchemaManager> logger)
    {
        _factory = factory;
        _time = time;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = db.Database.GetDbConnection();

            // Look before touching: a file that is not ours must not even have its journal mode changed.
            var tables = await ListTablesAsync(connection, cancellationToken);
            if (tables.Count > 0 && !tables.Contains("schema_info"))
            {
                throw new InvalidOperationException("The database file is not a fleet manager database (no schema_info table). Refusing to use it.");
            }

            await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken);
            if (tables.Count == 0)
            {
                await db.Database.EnsureCreatedAsync(cancellationToken);
                db.SchemaInfo.Add(new SchemaInfoEntity { Id = 1, Version = CurrentVersion, AppliedAt = _time.GetUtcNow() });
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Created the fleet database at schema version {Version}.", CurrentVersion);
                return;
            }

            var info = await db.SchemaInfo.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, cancellationToken)
                ?? throw new InvalidOperationException("The fleet database has no schema version row. Refusing to use it.");
            if (info.Version > CurrentVersion)
            {
                throw new InvalidOperationException(
                    "The fleet database is at schema version " + info.Version + ", newer than this build (" + CurrentVersion + "). Refusing to downgrade.");
            }

            var version = info.Version;
            foreach (var upgrade in Upgrades.Where(u => u.ToVersion > version).OrderBy(u => u.ToVersion))
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                foreach (var statement in upgrade.Statements)
                {
                    await ExecuteAsync(connection, statement, cancellationToken);
                }

                await ExecuteAsync(
                    connection,
                    "UPDATE schema_info SET version = " + upgrade.ToVersion + " WHERE id = 1;",
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                _logger.LogInformation("Upgraded the fleet database to schema version {Version}.", upgrade.ToVersion);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<HashSet<string>> ListTablesAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var tables = new HashSet<string>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }
}
