using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Drops <c>Difficulty</c> and <c>OetSubtestTagsJson</c> columns from
    /// <c>VocabularyTerms</c>. These fields are no longer surfaced in any
    /// admin or learner UI and have been removed from the domain entity.
    ///
    /// <para>
    /// Hand-written Postgres-only migration. SQLite test runs bypass
    /// migrations via <c>EnsureCreatedAsync()</c> and pick up the change
    /// directly from entity attributes (columns no longer defined).
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260607120000_DropVocabularyDifficultyAndSubtestTags")]
    public partial class DropVocabularyDifficultyAndSubtestTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""VocabularyTerms"" DROP COLUMN IF EXISTS ""Difficulty"";
                ALTER TABLE ""VocabularyTerms"" DROP COLUMN IF EXISTS ""OetSubtestTagsJson"";
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""VocabularyTerms""
                    ADD COLUMN ""Difficulty"" character varying(16) NOT NULL DEFAULT 'medium';
                ALTER TABLE ""VocabularyTerms""
                    ADD COLUMN ""OetSubtestTagsJson"" text NOT NULL DEFAULT '[]';
            ");
        }
    }
}
