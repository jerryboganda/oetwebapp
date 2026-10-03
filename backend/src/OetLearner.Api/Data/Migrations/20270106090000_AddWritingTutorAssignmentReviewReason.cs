using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Jev ITEM 3 — why a Writing grade was flagged for tutor review. Adds the nullable
    /// <c>ReviewReason</c> (varchar 64) to <c>WritingTutorReviewAssignments</c>: a comma-separated
    /// list of codes from the fixed WritingJevReviewReasons vocabulary, never free text.
    /// Additive and idempotent; existing rows keep NULL (the queue hides the badge). The blue/green
    /// slots overlap, and the old slot neither reads nor writes the column.
    ///
    /// HAND-AUTHORED (repo convention): inline <c>[Migration]</c>/<c>[DbContext]</c>, no Designer
    /// file; the matching property is added to <c>LearnerDbContextModelSnapshot</c>.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270106090000_AddWritingTutorAssignmentReviewReason")]
    public partial class AddWritingTutorAssignmentReviewReason : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"WritingTutorReviewAssignments\" ADD COLUMN IF NOT EXISTS \"ReviewReason\" character varying(64);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"WritingTutorReviewAssignments\" DROP COLUMN IF EXISTS \"ReviewReason\";");
        }
    }
}
