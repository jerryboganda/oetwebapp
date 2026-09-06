using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// AI Learning Companion citations (docs/ai-learning-companion/).
    ///
    /// Adds one nullable text column, <c>AiAssistantMessages.CitationsJson</c>, holding the
    /// approved sources a companion answer was grounded in. Persisted rather than streamed and
    /// forgotten: a citation the learner loses on reload is not a citation.
    ///
    /// Purely additive and idempotent — <c>ADD COLUMN IF NOT EXISTS</c> — so it is safe to run
    /// against a database where a previous partial deploy already added it. Every existing row
    /// keeps NULL, which is exactly right: admin and expert assistant messages have no retrieval
    /// step and therefore no citations.
    ///
    /// ⚠ Postgres-only SQL. The test suite uses SQLite via EnsureCreatedAsync() and builds from
    /// the model, bypassing migrations entirely.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261221090000_AddAssistantMessageCitations")]
    public partial class AddAssistantMessageCitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""AiAssistantMessages""
                    ADD COLUMN IF NOT EXISTS ""CitationsJson"" text NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""AiAssistantMessages""
                    DROP COLUMN IF EXISTS ""CitationsJson"";
            ");
        }
    }
}
