using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Replace the fixed five task-bullet columns on speaking role-play cards
    /// with unbounded JSON arrays.
    ///
    /// <para>Why: <c>RolePlayCard.Task1..Task5</c> and
    /// <c>InterlocutorScript.PatientTask1..PatientTask5</c> could hold at most
    /// five bullets, but the owner's real OET card sets contain cards printed
    /// with six, seven and eight task bullets (and some bullets carry indented
    /// sub-items). Importing those against a five-slot schema silently dropped
    /// printed content — the card looked fine and was quietly incomplete.</para>
    ///
    /// <para>This is the EXPAND half of an expand/contract migration. The new
    /// <c>TasksJson</c> / <c>PatientTasksJson</c> columns become authoritative
    /// and are backfilled from the legacy columns; the legacy columns are left
    /// in place (and kept mirrored by the application) so instances still
    /// running the previous build keep working across the blue/green rollout.
    /// A later migration drops them once every slot runs the new build.</para>
    ///
    /// <para>⚠ <b>Postgres-only SQL.</b> The test suite uses SQLite via
    /// <c>EnsureCreatedAsync()</c> and builds straight from the model,
    /// bypassing migrations entirely.</para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261218090000_AddUnboundedSpeakingCardTasks")]
    public partial class AddUnboundedSpeakingCardTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""RolePlayCards""
                    ADD COLUMN IF NOT EXISTS ""TasksJson"" text NOT NULL DEFAULT '[]';
            ");
            migrationBuilder.Sql(@"
                ALTER TABLE ""InterlocutorScripts""
                    ADD COLUMN IF NOT EXISTS ""PatientTasksJson"" text NOT NULL DEFAULT '[]';
            ");

            // Backfill: fold the legacy columns into an ordered JSON array,
            // skipping NULL/blank slots. WITH ORDINALITY + ORDER BY guarantees
            // the printed bullet order survives.
            migrationBuilder.Sql(@"
                UPDATE ""RolePlayCards"" c
                   SET ""TasksJson"" = (
                        SELECT COALESCE(json_agg(t ORDER BY ord)::text, '[]')
                          FROM unnest(ARRAY[c.""Task1"", c.""Task2"", c.""Task3"", c.""Task4"", c.""Task5""])
                               WITH ORDINALITY AS u(t, ord)
                         WHERE t IS NOT NULL AND btrim(t) <> '')
                 WHERE c.""TasksJson"" IS NULL OR c.""TasksJson"" = '[]';
            ");
            migrationBuilder.Sql(@"
                UPDATE ""InterlocutorScripts"" s
                   SET ""PatientTasksJson"" = (
                        SELECT COALESCE(json_agg(t ORDER BY ord)::text, '[]')
                          FROM unnest(ARRAY[s.""PatientTask1"", s.""PatientTask2"", s.""PatientTask3"",
                                            s.""PatientTask4"", s.""PatientTask5""])
                               WITH ORDINALITY AS u(t, ord)
                         WHERE t IS NOT NULL AND btrim(t) <> '')
                 WHERE s.""PatientTasksJson"" IS NULL OR s.""PatientTasksJson"" = '[]';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The legacy columns were never dropped, so rolling back is just a
            // matter of removing the new ones. Any sixth-or-later bullet that
            // only ever existed in JSON is lost on downgrade — that is inherent
            // to going back to a five-slot schema.
            migrationBuilder.Sql(@"ALTER TABLE ""RolePlayCards"" DROP COLUMN IF EXISTS ""TasksJson"";");
            migrationBuilder.Sql(@"ALTER TABLE ""InterlocutorScripts"" DROP COLUMN IF EXISTS ""PatientTasksJson"";");
        }
    }
}
