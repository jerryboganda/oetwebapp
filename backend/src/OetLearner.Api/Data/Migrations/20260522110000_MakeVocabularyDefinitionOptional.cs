using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Vocabulary Recalls / Flexible Import — Phase 1. Makes
    /// <c>VocabularyTerms.Definition</c> and <c>VocabularyTerms.ExampleSentence</c>
    /// nullable so the admin CSV importer can accept loose, term-only
    /// recall lists (single column, no header, definitions filled in later
    /// by the admin or by AI assist). Existing rows are left untouched.
    /// The publish gate (<c>EnforceVocabularyPublishGate</c>) still
    /// requires both fields to be populated before a row can flip from
    /// <c>draft</c> to <c>active</c>, so learner-facing surfaces never see
    /// a NULL definition.
    ///
    /// <para>
    /// Hand-written to match the repo's existing migration style (the EF
    /// Core <c>dotnet ef</c> tool isn't on the build host and the model
    /// snapshot has historically drifted). Postgres-only SQL — the column
    /// is altered to <c>DROP NOT NULL</c>. SQLite test runs bypass
    /// migrations via <c>EnsureCreatedAsync()</c> and pick up the change
    /// directly from the entity attributes.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260522110000_MakeVocabularyDefinitionOptional")]
    public partial class MakeVocabularyDefinitionOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""VocabularyTerms""
                    ALTER COLUMN ""Definition"" DROP NOT NULL;
                ALTER TABLE ""VocabularyTerms""
                    ALTER COLUMN ""ExampleSentence"" DROP NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort: backfill NULLs with empty strings before re-applying
            // NOT NULL so a rollback doesn't fail on existing draft rows that
            // legitimately have NULL definitions.
            migrationBuilder.Sql(@"
                UPDATE ""VocabularyTerms"" SET ""Definition"" = '' WHERE ""Definition"" IS NULL;
                UPDATE ""VocabularyTerms"" SET ""ExampleSentence"" = '' WHERE ""ExampleSentence"" IS NULL;
                ALTER TABLE ""VocabularyTerms""
                    ALTER COLUMN ""Definition"" SET NOT NULL;
                ALTER TABLE ""VocabularyTerms""
                    ALTER COLUMN ""ExampleSentence"" SET NOT NULL;
            ");
        }
    }
}
