using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// AI Learning Companion knowledge index (docs/ai-learning-companion/).
    ///
    /// Creates the three tables the companion retrieves from:
    ///   • CompanionSources           — approvable, versioned, authority-classed sources.
    ///                                  Entitlement scope lives here so the retrieval
    ///                                  prefilter can exclude a source before any search runs.
    ///   • CompanionChunks            — pedagogically chunked text with exact source location
    ///                                  (page / timestamp) and a pgvector(1536) embedding.
    ///   • CompanionKnowledgeReleases — published index snapshots, so knowledge can be rolled
    ///                                  back independently of an application deploy.
    ///
    /// The `vector` extension is already enabled for this database (see
    /// 20260612120000_AddPgvectorEmbeddingColumns and LearnerDbContext.HasPostgresExtension),
    /// and WritingScenarioEmbedding already uses vector(1536) in production. This migration is
    /// therefore purely additive: no existing table is touched.
    ///
    /// ⚠ Postgres-only SQL. The test suite uses SQLite via EnsureCreatedAsync() and builds
    /// straight from the model, bypassing migrations entirely — which is why
    /// LearnerDbContext.Companion.cs ignores the vector property on non-Npgsql providers.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261220090000_AddCompanionKnowledge")]
    public partial class AddCompanionKnowledge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE EXTENSION IF NOT EXISTS vector;

                CREATE TABLE IF NOT EXISTS ""CompanionSources"" (
                    ""Id""                       uuid NOT NULL,
                    ""SourceKey""                character varying(256) NOT NULL,
                    ""SourceType""               character varying(64)  NOT NULL,
                    ""Title""                    character varying(512) NOT NULL,
                    ""AuthorityClass""           integer NOT NULL DEFAULT 1,
                    ""State""                    integer NOT NULL DEFAULT 0,
                    ""ExamTypeCode""             character varying(32),
                    ""Version""                  character varying(64)  NOT NULL DEFAULT 'v1',
                    ""EffectiveFrom""            timestamp with time zone,
                    ""EffectiveTo""              timestamp with time zone,
                    ""ProfessionId""             character varying(64),
                    ""SubtestCode""              character varying(32),
                    ""RequiredEntitlementScope"" character varying(128),
                    ""IsProprietary""            boolean NOT NULL DEFAULT false,
                    ""CanaryTag""                character varying(128),
                    ""Checksum""                 character varying(128),
                    ""StorageLocator""           character varying(1024),
                    ""ApprovedByUserId""         character varying(64),
                    ""ApprovedAt""               timestamp with time zone,
                    ""SupersededBySourceId""     uuid,
                    ""CreatedAt""                timestamp with time zone NOT NULL DEFAULT now(),
                    ""UpdatedAt""                timestamp with time zone NOT NULL DEFAULT now(),
                    CONSTRAINT ""PK_CompanionSources"" PRIMARY KEY (""Id"")
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ""UX_CompanionSources_SourceKey_Version""
                    ON ""CompanionSources"" (""SourceKey"", ""Version"");

                -- Matches the retrieval prefilter predicate exactly: approved state,
                -- authority class, profession and subtest are all evaluated before search.
                CREATE INDEX IF NOT EXISTS ""IX_CompanionSources_Prefilter""
                    ON ""CompanionSources"" (""State"", ""AuthorityClass"", ""ProfessionId"", ""SubtestCode"");

                CREATE INDEX IF NOT EXISTS ""IX_CompanionSources_RequiredEntitlementScope""
                    ON ""CompanionSources"" (""RequiredEntitlementScope"");

                CREATE TABLE IF NOT EXISTS ""CompanionKnowledgeReleases"" (
                    ""Id""                      uuid NOT NULL,
                    ""ReleaseVersion""          character varying(64) NOT NULL,
                    ""Status""                  character varying(64) NOT NULL DEFAULT 'draft',
                    ""SourceCount""             integer NOT NULL DEFAULT 0,
                    ""ChunkCount""              integer NOT NULL DEFAULT 0,
                    ""IndexChecksum""           character varying(128),
                    ""EvaluationReportRef""     character varying(256),
                    ""ApprovedByUserId""        character varying(64),
                    ""PublishedAt""             timestamp with time zone,
                    ""RollbackTargetReleaseId"" uuid,
                    ""Changelog""               character varying(2048),
                    ""CreatedAt""               timestamp with time zone NOT NULL DEFAULT now(),
                    ""UpdatedAt""               timestamp with time zone NOT NULL DEFAULT now(),
                    CONSTRAINT ""PK_CompanionKnowledgeReleases"" PRIMARY KEY (""Id"")
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ""UX_CompanionKnowledgeReleases_ReleaseVersion""
                    ON ""CompanionKnowledgeReleases"" (""ReleaseVersion"");

                CREATE INDEX IF NOT EXISTS ""IX_CompanionKnowledgeReleases_Status""
                    ON ""CompanionKnowledgeReleases"" (""Status"");

                CREATE TABLE IF NOT EXISTS ""CompanionChunks"" (
                    ""Id""                uuid NOT NULL,
                    ""SourceId""          uuid NOT NULL,
                    ""Ordinal""           integer NOT NULL,
                    ""Heading""           character varying(512),
                    ""Text""              text NOT NULL DEFAULT '',
                    ""PageNumber""        integer,
                    ""TimestampSeconds""  integer,
                    ""ContentHash""       character varying(128) NOT NULL,
                    ""EmbeddingModelId""  character varying(64) NOT NULL DEFAULT 'text-embedding-3-small',
                    ""Embedding""         vector(1536),
                    ""ReleaseId""         uuid,
                    ""CreatedAt""         timestamp with time zone NOT NULL DEFAULT now(),
                    ""UpdatedAt""         timestamp with time zone NOT NULL DEFAULT now(),
                    CONSTRAINT ""PK_CompanionChunks"" PRIMARY KEY (""Id""),
                    CONSTRAINT ""FK_CompanionChunks_CompanionSources_SourceId""
                        FOREIGN KEY (""SourceId"") REFERENCES ""CompanionSources"" (""Id"") ON DELETE CASCADE
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ""UX_CompanionChunks_SourceId_Ordinal""
                    ON ""CompanionChunks"" (""SourceId"", ""Ordinal"");

                CREATE INDEX IF NOT EXISTS ""IX_CompanionChunks_ContentHash""
                    ON ""CompanionChunks"" (""ContentHash"");

                CREATE INDEX IF NOT EXISTS ""IX_CompanionChunks_ReleaseId""
                    ON ""CompanionChunks"" (""ReleaseId"");
            ");

            // HNSW build is separated so a failure here (e.g. an older pgvector without
            // HNSW) cannot roll back the table creation. Retrieval degrades to an exact
            // scan plus the keyword path, which CompanionRetriever already tolerates —
            // the same graceful-fallback posture as CodebaseRetriever.
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    CREATE INDEX IF NOT EXISTS ""IX_CompanionChunks_Embedding_Hnsw""
                        ON ""CompanionChunks"" USING hnsw (""Embedding"" vector_cosine_ops);
                EXCEPTION WHEN OTHERS THEN
                    RAISE NOTICE 'Skipped HNSW index on CompanionChunks.Embedding: %', SQLERRM;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ""IX_CompanionChunks_Embedding_Hnsw"";
                DROP TABLE IF EXISTS ""CompanionChunks"";
                DROP TABLE IF EXISTS ""CompanionKnowledgeReleases"";
                DROP TABLE IF EXISTS ""CompanionSources"";
            ");
        }
    }
}
