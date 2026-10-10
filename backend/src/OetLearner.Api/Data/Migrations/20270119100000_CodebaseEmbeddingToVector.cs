using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Fixes <c>AiCodebaseChunks.Embedding</c>: <c>real[]</c> -> <c>vector(1536)</c>
    /// (owner directive 2026-10-09).
    ///
    /// <para>
    /// The column was created as a plain Postgres <c>real[]</c> by
    /// <c>20260520182040_AddAiAssistantEntities</c>, while the sibling embedding columns —
    /// <c>CompanionChunk.Embedding</c> (<c>LearnerDbContext.Companion.cs:71</c>) and
    /// <c>WritingScenario.Embedding</c> (<c>LearnerDbContext.WritingScenarios.cs:42</c>) — are
    /// <c>vector(1536)</c>, and the provider has pgvector enabled (<c>DatabaseConfiguration.cs:81</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Why it mattered.</b> <c>CodebaseRetriever</c> performs its vector half with the pgvector
    /// distance operator (<c>&lt;=&gt;</c>). Against a <c>real[]</c> column that operator does not
    /// exist, the exception is swallowed and retrieval silently degrades to keyword-only — so the
    /// hybrid search described in the code was never actually hybrid. That defect was invisible while
    /// the index was empty for a different reason (no source mounted in production), and would have
    /// surfaced as "semantic search seems poor" the moment source was mounted.
    /// </para>
    ///
    /// <para>
    /// <b>Safety of the type change.</b> A direct <c>ALTER TABLE ... TYPE</c> is not supported for
    /// this conversion, so the column is dropped and re-added. That is destructive <b>only</b> if
    /// rows exist. Production has never indexed anything (no source was ever mounted), so the table
    /// is empty; the guard below makes that assumption explicit rather than assumed, and refuses to
    /// drop data it did not verify was disposable.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270119100000_CodebaseEmbeddingToVector")]
    public partial class CodebaseEmbeddingToVector : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Explicit, loud and safe: only proceed when there is nothing to lose. If a deployment
            // ever does hold chunks this aborts loudly, and a re-index is scheduled instead — far
            // better than silently discarding an index that took hours to build.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "AiCodebaseChunks" LIMIT 1) THEN
                        RAISE EXCEPTION
                            'AiCodebaseChunks is not empty; converting Embedding to vector(1536) would drop rows. Re-index from a source mount instead.';
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "AiCodebaseChunks"
                    ALTER COLUMN "Embedding" DROP DEFAULT,
                    DROP COLUMN "Embedding";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "AiCodebaseChunks"
                    ADD COLUMN "Embedding" vector(1536) NULL;
                """);

            // Index on ContentHash, which the indexer queries on EVERY re-index to skip unchanged
            // chunks (CodebaseIndexer: "Where(c => hashes.Contains(c.ContentHash))"). It never had
            // one. That was survivable while the index was always empty; with a mounted source tree
            // and a re-index every 6h it becomes the hot path, so the table would be fully scanned
            // each cycle. Declared in the model too, so the two agree.
            migrationBuilder.CreateIndex(
                name: "IX_AiCodebaseChunks_ContentHash",
                table: "AiCodebaseChunks",
                column: "ContentHash");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse direction only. Anything indexed under the vector column is lost, which is
            // acceptable because the Down path exists for emergency rollback, not routine use: the
            // index is rebuildable from a source mount at any time.
            migrationBuilder.Sql("""
                ALTER TABLE "AiCodebaseChunks"
                    ALTER COLUMN "Embedding" DROP DEFAULT,
                    DROP COLUMN "Embedding";
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "AiCodebaseChunks"
                    ADD COLUMN "Embedding" real[] NULL;
                """);

            migrationBuilder.DropIndex(
                name: "IX_AiCodebaseChunks_ContentHash",
                table: "AiCodebaseChunks");
        }
    }
}