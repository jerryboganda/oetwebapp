using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.AiTools;

namespace OetWithDrHesham.Api.Services.AiAssistant.Tools;

/// <summary>
/// Read-only database query tool (admin only).
/// CRITICAL: Only allows SELECT statements. Rejects all DDL/DML.
/// Wraps execution in a transaction that is always rolled back for safety.
/// Returns results as JSON array (max 100 rows).
/// </summary>
public sealed class QueryDatabaseTool : IAiToolExecutor
{
    public string Code => "query_database";
    public AiToolCategory Category => AiToolCategory.Read;
    public string JsonSchemaArgs => """
    {
      "type":"object",
      "properties":{
        "sql":{"type":"string","minLength":1,"maxLength":2000}
      },
      "required":["sql"],
      "additionalProperties":false
    }
    """;

    private const int MaxRows = 100;
    private const int TimeoutSeconds = 15;

    /// <summary>
    /// SQL keywords that indicate mutation/DDL. These are blocked unconditionally.
    /// </summary>
    private static readonly string[] ForbiddenKeywords =
    {
        "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "TRUNCATE", "CREATE",
        "EXEC", "EXECUTE", "GRANT", "REVOKE", "MERGE", "CALL", "INTO"
    };

    private readonly LearnerDbContext _db;
    private readonly ILogger<QueryDatabaseTool> _logger;

    public QueryDatabaseTool(LearnerDbContext db, ILogger<QueryDatabaseTool> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<AiToolExecutionResult> ExecuteAsync(JsonElement args, AiToolContext ctx, CancellationToken ct)
    {
        var sql = args.GetProperty("sql").GetString()!.Trim();

        // Validate: must start with SELECT (case-insensitive)
        if (!sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            return new AiToolExecutionResult(
                AiToolOutcome.RbacDenied, null, "not_select",
                "Only SELECT statements are allowed.");
        }

        // Validate: check for forbidden keywords anywhere in the query
        var upperSql = sql.ToUpperInvariant();
        foreach (var keyword in ForbiddenKeywords)
        {
            // Use word-boundary matching to avoid false positives like "UPDATED_AT"
            if (Regex.IsMatch(upperSql, $@"\b{keyword}\b"))
            {
                return new AiToolExecutionResult(
                    AiToolOutcome.RbacDenied, null, "forbidden_keyword",
                    $"SQL contains forbidden keyword: {keyword}. Only read-only SELECT statements are allowed.");
            }
        }

        // Reject multiple statements (semicolons outside strings suggest chaining)
        var withoutStrings = Regex.Replace(sql, @"'[^']*'", ""); // strip string literals
        if (withoutStrings.Contains(';'))
        {
            return new AiToolExecutionResult(
                AiToolOutcome.RbacDenied, null, "multiple_statements",
                "Multiple SQL statements are not allowed.");
        }

        try
        {
            var connection = _db.Database.GetDbConnection();
            await connection.OpenAsync(ct);

            // Use a transaction that we will ALWAYS roll back
            await using var transaction = await connection.BeginTransactionAsync(ct);
            try
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"SELECT * FROM ({sql}) AS __q LIMIT {MaxRows}";
                command.CommandTimeout = TimeoutSeconds;

                await using var reader = await command.ExecuteReaderAsync(ct);

                var results = new List<Dictionary<string, object?>>();
                var columnNames = new List<string>();

                for (var i = 0; i < reader.FieldCount; i++)
                {
                    columnNames.Add(reader.GetName(i));
                }

                while (await reader.ReadAsync(ct) && results.Count < MaxRows)
                {
                    var row = new Dictionary<string, object?>();
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        row[columnNames[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    results.Add(row);
                }

                return new AiToolExecutionResult(
                    AiToolOutcome.Success,
                    ToJson(new
                    {
                        rowCount = results.Count,
                        columns = columnNames,
                        rows = results,
                        truncated = results.Count >= MaxRows
                    }));
            }
            finally
            {
                // ALWAYS roll back — this tool is read-only by design
                await transaction.RollbackAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            return new AiToolExecutionResult(
                AiToolOutcome.ProviderError, null, "timeout",
                "Query execution timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QueryDatabaseTool execution error for SQL: {Sql}", sql[..Math.Min(sql.Length, 100)]);
            return new AiToolExecutionResult(
                AiToolOutcome.ProviderError, null, "execution_error",
                $"Query failed: {ex.Message}");
        }
    }

    private static JsonElement ToJson(object payload) =>
        JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement.Clone();
}
