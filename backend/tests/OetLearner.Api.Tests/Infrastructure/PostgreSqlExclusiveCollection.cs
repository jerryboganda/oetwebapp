namespace OetLearner.Api.Tests.Infrastructure;

/// <summary>
/// 40-way budget/coordinator races each open one EF context per caller.
/// CI's postgres:16-alpine defaults to max_connections=100, and xUnit
/// parallelizes test classes, so two races plus the harness overflow the
/// server and AiBudgetService fail-closes as budget_store_unavailable.
/// Tests in this collection run with exclusive access to the server.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlExclusiveCollection
{
    public const string Name = "PostgreSqlExclusive";
}
