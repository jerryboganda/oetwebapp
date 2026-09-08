using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// 2026-09-09 — classifier repair (§8B) provenance columns on
/// <c>RolePlayCards</c>: <c>CategorySource</c> (legacy | classifier |
/// reviewed | manual | seed), <c>CategoryClassifierVersion</c>, and
/// <c>CategoryClassifiedAt</c>. Every existing row predates provenance
/// tracking, so it backfills as <c>CategorySource = 'legacy'</c> — the
/// classifier-repair sweep (a follow-up migration) reclassifies from there
/// and records the outcome per row.
///
/// <para>Hand-written (columns only), per repo convention — the model
/// snapshot was updated by hand to match.</para>
///
/// <para>⚠ Postgres-only SQL. The test suite uses SQLite via
/// <c>EnsureCreatedAsync()</c> and builds straight from the model, bypassing
/// migrations entirely.</para>
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260909090000_SpeakingCardCategoryProvenance")]
public partial class SpeakingCardCategoryProvenance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
ALTER TABLE "RolePlayCards" ADD COLUMN IF NOT EXISTS "CategorySource" character varying(16) NOT NULL DEFAULT 'legacy';
""");
        migrationBuilder.Sql("""
ALTER TABLE "RolePlayCards" ADD COLUMN IF NOT EXISTS "CategoryClassifierVersion" character varying(32) NULL;
""");
        migrationBuilder.Sql("""
ALTER TABLE "RolePlayCards" ADD COLUMN IF NOT EXISTS "CategoryClassifiedAt" timestamp with time zone NULL;
""");

        // The twelve system-seeded cards (see SpeakingCardCategoryColumns) were
        // hand-authored, not classifier output.
        migrationBuilder.Sql("""
UPDATE "RolePlayCards" SET "CategorySource" = 'seed' WHERE "Id" LIKE 'rpc-seed-%';
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""ALTER TABLE "RolePlayCards" DROP COLUMN IF EXISTS "CategoryClassifiedAt";""");
        migrationBuilder.Sql("""ALTER TABLE "RolePlayCards" DROP COLUMN IF EXISTS "CategoryClassifierVersion";""");
        migrationBuilder.Sql("""ALTER TABLE "RolePlayCards" DROP COLUMN IF EXISTS "CategorySource";""");
    }
}
