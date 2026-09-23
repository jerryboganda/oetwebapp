using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Fixes a live seed failure: several real, legitimate evidence citations (multi-file
    /// test-class lists) exceeded the original 256-char SourceReference column, which
    /// rolled back the entire DocumentationCenterSeeder insert (all 15 modules, not just
    /// the 3 long rows) with Postgres error 22001. Widening to 512 rather than shortening
    /// the citations, which are real and useful.
    ///
    /// HAND-AUTHORED (repo convention, see 20260729090000): inline
    /// <c>[Migration]</c>/<c>[DbContext]</c>, no Designer file, ModelSnapshot
    /// deliberately untouched.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270103090100_WidenDocumentationEvidenceSourceReference")]
    public partial class WidenDocumentationEvidenceSourceReference : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""DocumentationEvidenceItems"" ALTER COLUMN ""SourceReference"" TYPE character varying(512);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""DocumentationEvidenceItems"" ALTER COLUMN ""SourceReference"" TYPE character varying(256);");
        }
    }
}
