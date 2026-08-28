using Xunit;

namespace OetLearner.Api.Tests.Infrastructure;

/// <summary>
/// <see cref="FactAttribute"/> that skips (never fails) unless
/// <see cref="PostgreSqlTestDatabase.ConnectionVariable"/> is set. Use for any
/// test that needs real PostgreSQL semantics (partition/index behaviour,
/// provider-specific SQL, row-locking, …) that SQLite/InMemory cannot
/// faithfully emulate.
/// </summary>
public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgreSqlTestDatabase.ConnectionVariable)))
        {
            Skip = $"Set {PostgreSqlTestDatabase.ConnectionVariable} to run PostgreSQL provider coverage.";
        }
    }
}
