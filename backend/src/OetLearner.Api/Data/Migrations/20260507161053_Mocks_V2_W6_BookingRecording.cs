using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Mocks V2 Wave 6 — adds three columns to <c>MockBookings</c> for the
    /// learner-side audio capture pipeline used by
    /// <c>app/mocks/speaking-room/[bookingId]/page.tsx</c>:
    /// <list type="bullet">
    ///   <item><c>RecordingManifestJson</c> — JSON array of accepted chunks
    ///         (part index, SHA-256, IFileStorage key, byte count, mime).</item>
    ///   <item><c>RecordingDurationMs</c> — total captured duration in ms.</item>
    ///   <item><c>RecordingFinalizedAt</c> — set when the learner submits.</item>
    /// </list>
    /// Hand-written because the EF Core model snapshot is drifted (see
    /// <c>memories/repo/migration-drift-note.md</c>) and an auto-generated
    /// migration would also include unrelated expert-comp tables.
    /// All three columns are nullable; recording is gated on
    /// <c>ConsentToRecording</c> and never overwrites existing data.
    ///
    /// <para>
    /// ⚠ <b>Postgres-only SQL.</b> The <c>ADD COLUMN IF NOT EXISTS</c> and
    /// <c>ALTER COLUMN ... TYPE text</c> statements are PostgreSQL syntax.
    /// The test suite uses SQLite via <c>EnsureCreatedAsync()</c> and
    /// bypasses migrations entirely, so this is safe today. If anyone enables
    /// <c>Database.MigrateAsync()</c> against SQLite in tests, this migration
    /// will fail — split into provider-specific branches at that point.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260507161053_Mocks_V2_W6_BookingRecording")]
    public partial class Mocks_V2_W6_BookingRecording : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent against partial deploys.
            // RecordingManifestJson is `text` (unbounded) — a 20-min/240-chunk
            // recording produces ~50 KB of manifest JSON, far above any
            // VARCHAR(N) cap. If a previous deploy created the column with a
            // narrower type, ALTER widens it to text.
            migrationBuilder.Sql(@"
                ALTER TABLE ""MockBookings""
                    ADD COLUMN IF NOT EXISTS ""RecordingManifestJson"" text NULL;
                ALTER TABLE ""MockBookings""
                    ALTER COLUMN ""RecordingManifestJson"" TYPE text;
                ALTER TABLE ""MockBookings""
                    ADD COLUMN IF NOT EXISTS ""RecordingDurationMs"" bigint NULL;
                ALTER TABLE ""MockBookings""
                    ADD COLUMN IF NOT EXISTS ""RecordingFinalizedAt"" timestamp with time zone NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""MockBookings"" DROP COLUMN IF EXISTS ""RecordingFinalizedAt"";
                ALTER TABLE ""MockBookings"" DROP COLUMN IF EXISTS ""RecordingDurationMs"";
                ALTER TABLE ""MockBookings"" DROP COLUMN IF EXISTS ""RecordingManifestJson"";
            ");
        }
    }
}
