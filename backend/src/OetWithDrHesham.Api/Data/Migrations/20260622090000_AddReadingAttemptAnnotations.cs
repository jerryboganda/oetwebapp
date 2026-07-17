using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// R08 — adds <c>ReadingAttempt.AnnotationsJson</c> (the learner's
    /// persistent rule-out / highlight payload), mirroring the existing
    /// <c>ListeningAttempt.AnnotationsJson</c> jsonb column. Hand-written
    /// idempotent SQL (matches the house style of the sibling reading
    /// migrations) so it applies cleanly on both fresh and existing databases
    /// and is safe to re-run.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260622090000_AddReadingAttemptAnnotations")]
    public partial class AddReadingAttemptAnnotations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""ReadingAttempts"" ADD COLUMN IF NOT EXISTS ""AnnotationsJson"" jsonb NULL;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""ReadingAttempts"" DROP COLUMN IF EXISTS ""AnnotationsJson"";");
        }
    }
}
