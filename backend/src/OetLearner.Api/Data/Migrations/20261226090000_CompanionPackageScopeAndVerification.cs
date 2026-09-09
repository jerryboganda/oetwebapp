using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    // Knowledge Base Master Manifest 1.B (package isolation) and 1.D (official
    // factual layer).
    //
    //   • PackageScope       — Full vs Crash isolation on a knowledge source, in the
    //                          same vocabulary as LibraryVideo.VisibilityScope so one
    //                          policy (PackageScopePolicy) governs both. Without it
    //                          "content from one package must not be silently mixed
    //                          into another" is unenforceable, because every source
    //                          is unscoped.
    //   • SourceUrl          — where an official fact was read from.
    //   • VerifiedByUserId   — who checked it against that source.
    //   • VerifiedAt         — when. An OfficialCurrentFact with no verification is
    //                          staged PendingApproval and never retrieved.
    //
    // HAND-AUTHORED (repo convention, matching 20261220090000_AddCompanionKnowledge):
    // raw idempotent SQL, model snapshot untouched, runtime model comes from the
    // entity classes. SAFETY: purely additive — four nullable columns and one
    // partial index. No backfill, no rewrite; existing rows keep NULL, which the
    // retriever reads as "no package restriction", i.e. today's behaviour.
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261226090000_CompanionPackageScopeAndVerification")]
    public partial class CompanionPackageScopeAndVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""CompanionSources""
                    ADD COLUMN IF NOT EXISTS ""PackageScope"" character varying(32),
                    ADD COLUMN IF NOT EXISTS ""SourceUrl"" character varying(1024),
                    ADD COLUMN IF NOT EXISTS ""VerifiedByUserId"" character varying(64),
                    ADD COLUMN IF NOT EXISTS ""VerifiedAt"" timestamp with time zone;

                -- Partial: the overwhelming majority of sources are unscoped, and
                -- the prefilter only ever needs to find the scoped ones.
                CREATE INDEX IF NOT EXISTS ""IX_CompanionSources_PackageScope""
                    ON ""CompanionSources"" (""PackageScope"")
                    WHERE ""PackageScope"" IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ""IX_CompanionSources_PackageScope"";
                ALTER TABLE ""CompanionSources""
                    DROP COLUMN IF EXISTS ""PackageScope"",
                    DROP COLUMN IF EXISTS ""SourceUrl"",
                    DROP COLUMN IF EXISTS ""VerifiedByUserId"",
                    DROP COLUMN IF EXISTS ""VerifiedAt"";
            ");
        }
    }
}
