using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Drops the four single-column indexes on ContentItem and Attempt for
    /// <c>ExamFamilyCode</c> and <c>ExamTypeCode</c>. Every row currently
    /// stores the constant <c>"oet"</c> in both columns, so the indexes had
    /// ~0 selectivity while still paying full write-amplification cost.
    ///
    /// When a second exam family ships, re-introduce them as partial indexes
    /// (e.g. <c>WHERE "ExamTypeCode" &lt;&gt; 'oet'</c>) so non-OET rows get a
    /// dedicated probe without burdening the hot OET path.
    ///
    /// Idempotent (DROP INDEX IF EXISTS) so re-application is safe.
    /// </summary>
    public partial class DropLowSelectivityExamIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ContentItems_ExamFamilyCode\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ContentItems_ExamTypeCode\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Attempts_ExamFamilyCode\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Attempts_ExamTypeCode\";");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_ContentItems_ExamFamilyCode\" " +
                "ON \"ContentItems\" (\"ExamFamilyCode\");");
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_ContentItems_ExamTypeCode\" " +
                "ON \"ContentItems\" (\"ExamTypeCode\");");
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_Attempts_ExamFamilyCode\" " +
                "ON \"Attempts\" (\"ExamFamilyCode\");");
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_Attempts_ExamTypeCode\" " +
                "ON \"Attempts\" (\"ExamTypeCode\");");
        }
    }
}
