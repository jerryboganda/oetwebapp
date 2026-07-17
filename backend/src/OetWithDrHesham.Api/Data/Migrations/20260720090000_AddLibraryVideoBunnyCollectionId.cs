using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Adds <c>LibraryVideos.BunnyCollectionId</c> — the per-video Bunny
    /// collection (folder) membership mirror, set by the admin Collections
    /// console and the upload wizard's collection picker. Nullable; absence
    /// means the global default collection is used at upload time. Bunny remains
    /// the source of truth for membership.
    ///
    /// <para>
    /// Hand-written Postgres-only migration, following the established pattern
    /// (see 20260718090000_AddVideoLibraryAndBunnySettings.cs): the EF model
    /// snapshot is deliberately not hand-edited. SQLite/InMemory test runs bypass
    /// migrations via EnsureCreatedAsync() and pick the column up from the entity
    /// model directly.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260720090000_AddLibraryVideoBunnyCollectionId")]
    public partial class AddLibraryVideoBunnyCollectionId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""LibraryVideos"" ADD COLUMN IF NOT EXISTS ""BunnyCollectionId"" character varying(64);
CREATE INDEX IF NOT EXISTS ""IX_LibraryVideos_BunnyCollectionId"" ON ""LibraryVideos"" (""BunnyCollectionId"");
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP INDEX IF EXISTS ""IX_LibraryVideos_BunnyCollectionId"";
ALTER TABLE ""LibraryVideos"" DROP COLUMN IF EXISTS ""BunnyCollectionId"";
");
        }
    }
}
