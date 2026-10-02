using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using OetLearner.Api.Data.Migrations;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Platform;

/// <summary>
/// WAI-06: <see cref="AddWritingDraftResumeState"/> against real PostgreSQL —
/// columns get their blue/green-safe defaults and the legacy backfill marks
/// only drafts consumed by a later submission as submitted, never deletes,
/// never touches a new-style attempt, and is re-runnable. Skips without
/// <c>OET_TEST_POSTGRES_CONNECTION</c> (qa-smoke runs it).
/// </summary>
public sealed class WritingDraftResumeStatePostgreSqlTests
{
    [PostgreSqlFact]
    public async Task Backfill_MarksOnlyConsumedLegacyDrafts_AndIsRerunnable()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await database.ExecuteAsync(
            """
            CREATE TABLE "WritingDraftsV2" (
                "Id" uuid PRIMARY KEY,
                "UserId" character varying(64) NOT NULL,
                "ScenarioId" uuid NOT NULL,
                "Mode" character varying(16) NOT NULL,
                "Content" text NOT NULL,
                "WordCount" integer NOT NULL,
                "TimeSpentSeconds" integer NOT NULL,
                "LastSavedAt" timestamp with time zone NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL
            );
            CREATE TABLE "WritingSubmissions" (
                "Id" uuid PRIMARY KEY,
                "UserId" character varying(64) NOT NULL,
                "ScenarioId" uuid NOT NULL,
                "Mode" character varying(16) NOT NULL,
                "IsRevision" boolean NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL
            );
            INSERT INTO "WritingDraftsV2" VALUES
                ('00000000-0000-0000-0000-00000000000a', 'u1', '10000000-0000-0000-0000-000000000001', 'practice', 'submitted text', 2, 60, now() - interval '1 hour', now() - interval '2 hours'),
                ('00000000-0000-0000-0000-00000000000b', 'u1', '10000000-0000-0000-0000-000000000002', 'practice', 'still writing', 2, 60, now() - interval '1 hour', now() - interval '2 hours'),
                ('00000000-0000-0000-0000-00000000000c', 'u1', '10000000-0000-0000-0000-000000000003', 'practice', 'late autosave', 2, 60, now() - interval '30 seconds', now() - interval '2 hours'),
                ('00000000-0000-0000-0000-00000000000d', 'u2', '10000000-0000-0000-0000-000000000001', 'practice', 'other learner', 2, 60, now() - interval '1 hour', now() - interval '2 hours');
            INSERT INTO "WritingSubmissions" VALUES
                -- a: submitted 10 min after its last save, then once more later
                ('20000000-0000-0000-0000-00000000000a', 'u1', '10000000-0000-0000-0000-000000000001', 'practice', false, now() - interval '50 minutes'),
                ('20000000-0000-0000-0000-00000000000e', 'u1', '10000000-0000-0000-0000-000000000001', 'practice', false, now() - interval '5 minutes'),
                -- b: only an OLDER submission, a revision and a mock: the draft is a live new attempt
                ('20000000-0000-0000-0000-00000000000b', 'u1', '10000000-0000-0000-0000-000000000002', 'practice', false, now() - interval '3 hours'),
                ('20000000-0000-0000-0000-00000000000f', 'u1', '10000000-0000-0000-0000-000000000002', 'practice', true, now()),
                ('20000000-0000-0000-0000-000000000010', 'u1', '10000000-0000-0000-0000-000000000002', 'mock', false, now()),
                -- c: the autosave PUT landed a few seconds after the submit
                ('20000000-0000-0000-0000-00000000000c', 'u1', '10000000-0000-0000-0000-000000000003', 'practice', false, now() - interval '40 seconds');
            """);

        var upSql = MigrationUpSql();
        await database.ExecuteAsync(upSql);
        // A new-style attempt (AttemptStartedAt set) is never re-consumed by a re-run.
        await database.ExecuteAsync(
            """
            UPDATE "WritingDraftsV2" SET "Status" = 'active', "SubmissionId" = NULL, "AttemptStartedAt" = now()
            WHERE "Id" = '00000000-0000-0000-0000-00000000000c';
            """);
        await database.ExecuteAsync(upSql);

        await using var command = database.Command(
            """
            SELECT "Id"::text, "Status", "SubmissionId"::text, "Version", "Content" FROM "WritingDraftsV2" ORDER BY "Id";
            """);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string Id, string Status, string? SubmissionId, int Version, string Content)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetInt32(3), reader.GetString(4)));
        }

        Assert.Equal(4, rows.Count); // never deletes
        Assert.Equal(("submitted", "20000000-0000-0000-0000-00000000000a"), (rows[0].Status, rows[0].SubmissionId));
        Assert.Equal(("active", (string?)null), (rows[1].Status, rows[1].SubmissionId));
        Assert.Equal(("active", (string?)null), (rows[2].Status, rows[2].SubmissionId));
        Assert.Equal(("active", (string?)null), (rows[3].Status, rows[3].SubmissionId));
        Assert.All(rows, row => Assert.Equal(1, row.Version));
        Assert.Equal("submitted text", rows[0].Content);
    }

    private static string MigrationUpSql()
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var up = typeof(AddWritingDraftResumeState).GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("AddWritingDraftResumeState.Up was not found.");
        up.Invoke(new AddWritingDraftResumeState(), [builder]);
        return Assert.Single(builder.Operations.OfType<SqlOperation>()).Sql;
    }
}
