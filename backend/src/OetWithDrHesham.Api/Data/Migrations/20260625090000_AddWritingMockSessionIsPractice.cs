using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Adds <c>WritingMockSession.IsPractice</c> — true when the session was
    /// started as the relaxed practice variant (?practice=1). It lets the learner
    /// skip the reading window early in practice; strict mocks still wait it out
    /// server-side. Hand-written idempotent SQL (matches the house style of the
    /// sibling writing/reading migrations) so it applies cleanly on fresh and
    /// existing databases and is safe to re-run.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260625090000_AddWritingMockSessionIsPractice")]
    public partial class AddWritingMockSessionIsPractice : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""WritingMockSessions"" ADD COLUMN IF NOT EXISTS ""IsPractice"" boolean NOT NULL DEFAULT false;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""WritingMockSessions"" DROP COLUMN IF EXISTS ""IsPractice"";");
        }
    }
}
