using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260801090000_AddLibraryVideoLanguage")]
    public partial class AddLibraryVideoLanguage : Migration
    {
        // Course Videos content model (owner chart 2026-07-18): a Video Library video now
        // carries a first-class instruction language ("en" | "ar") so learners can filter the
        // library by English / Arabic. This is independent of profession gating —
        // ProfessionIdsJson still decides *who* sees a video (English = [] visible to all;
        // Arabic Medicine set aliases Physiotherapy/Dentistry/Radiography). The column only
        // powers the learner EN/AR toggle + the admin badge.
        //
        // WHY A HAND-AUTHORED MIGRATION (not `dotnet ef migrations add`): production is live and
        // the auto-generated output re-creates existing tables. This is a single additive,
        // nullable column — safe to backfill later via the admin API (the retag script). The
        // model snapshot is intentionally left untouched (project convention).
        //
        // SAFETY: idempotent (ADD/DROP COLUMN IF [NOT] EXISTS); nullable so existing rows are
        // valid immediately; no default, no data rewrite, no lock beyond a fast catalog update.

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""LibraryVideos"" ADD COLUMN IF NOT EXISTS ""Language"" character varying(8);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""LibraryVideos"" DROP COLUMN IF EXISTS ""Language"";");
        }
    }
}
