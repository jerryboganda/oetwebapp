using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Adds <c>ListeningExtracts.ContextIntro</c> — the printed scenario/intro line
    /// for a Part B/C extract ("You hear a charge nurse briefing a colleague…").
    /// Rendered once per extract on the learner card so the question-paper PDF can be
    /// dropped once the questions are authored inline. Nullable; null for Part A and
    /// for extracts without an authored context line.
    ///
    /// <para>
    /// Hand-written Postgres-only migration, following the established pattern
    /// (see 20260720090000_AddLibraryVideoBunnyCollectionId.cs): the EF model
    /// snapshot is deliberately not hand-edited. SQLite/InMemory test runs bypass
    /// migrations via EnsureCreatedAsync() and pick the column up from the entity
    /// model directly.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260722090000_AddListeningExtractContextIntro")]
    public partial class AddListeningExtractContextIntro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""ListeningExtracts"" ADD COLUMN IF NOT EXISTS ""ContextIntro"" character varying(2048);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""ListeningExtracts"" DROP COLUMN IF EXISTS ""ContextIntro"";
");
        }
    }
}
