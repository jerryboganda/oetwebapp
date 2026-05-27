using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPgvectorEmbeddingColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent — `CREATE EXTENSION IF NOT EXISTS vector` is safe to
            // re-run on databases where pgvector is already installed (eg.
            // shared infra where another module created it first).
            //
            // REQUIREMENT: the running Postgres instance must ship pgvector.
            // The default `postgres:17-alpine` image does NOT — the local
            // docker-compose stack uses `pgvector/pgvector:pg17` for that
            // reason. Production deployments (managed Postgres, DigitalOcean
            // Managed DB, etc.) typically expose pgvector behind a feature
            // flag; flip it on once before applying this migration or the
            // statement below will fail with `extension "vector" is not
            // available`.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            // Add Embedding column to WritingExemplarEmbeddings + index.
            migrationBuilder.Sql(
                "ALTER TABLE \"WritingExemplarEmbeddings\" ADD COLUMN IF NOT EXISTS \"Embedding\" vector(1536) NULL;");
            // HNSW cosine index — ANN nearest-neighbour over the new column.
            // Partial filter excludes legacy rows that have not been
            // backfilled yet so the index stays compact.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_WritingExemplarEmbeddings_Embedding_hnsw\" " +
                "ON \"WritingExemplarEmbeddings\" USING hnsw (\"Embedding\" vector_cosine_ops) " +
                "WHERE \"Embedding\" IS NOT NULL;");

            // Add Embedding column to WritingScenarioEmbeddings + index.
            migrationBuilder.Sql(
                "ALTER TABLE \"WritingScenarioEmbeddings\" ADD COLUMN IF NOT EXISTS \"Embedding\" vector(1536) NULL;");
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_WritingScenarioEmbeddings_Embedding_hnsw\" " +
                "ON \"WritingScenarioEmbeddings\" USING hnsw (\"Embedding\" vector_cosine_ops) " +
                "WHERE \"Embedding\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop indexes + columns. Intentionally DO NOT drop the `vector`
            // extension — other modules (eg. class recording embeddings,
            // future codebase chunks) may share it. Dropping the extension
            // would cascade and remove every dependent column across the
            // schema.
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_WritingScenarioEmbeddings_Embedding_hnsw\";");
            migrationBuilder.Sql("ALTER TABLE \"WritingScenarioEmbeddings\" DROP COLUMN IF EXISTS \"Embedding\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_WritingExemplarEmbeddings_Embedding_hnsw\";");
            migrationBuilder.Sql("ALTER TABLE \"WritingExemplarEmbeddings\" DROP COLUMN IF EXISTS \"Embedding\";");
        }
    }
}
