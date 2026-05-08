using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Phase 6 — Voice provider unification (foundation slice).
    ///
    /// Adds a <c>Category</c> column to <c>AiProviders</c> so the same
    /// table can hold text-chat, TTS, ASR, and phoneme rows. Existing
    /// rows backfill to <c>0</c> (TextChat) so behaviour is unchanged
    /// until an admin explicitly registers a voice row.
    ///
    /// A Postgres CHECK constraint locks the value to the four enum
    /// members defined in <see cref="OetLearner.Api.Domain.AiProviderCategory"/>.
    ///
    /// Selector refactor (TTS / ASR / Pronunciation ASR) lands in
    /// the follow-up Phase 6b commit. This migration is intentionally
    /// additive so it can ship without touching live voice traffic.
    ///
    /// IMPORTANT: this migration class carries both [DbContext] and
    /// [Migration] attributes — without them EF discovery silently
    /// no-ops on production boot. See repo:memory/migration-drift-note.md.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260510130000_AddAiProviderCategory")]
    public partial class AddAiProviderCategory : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Category",
                table: "AiProviders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                "ALTER TABLE \"AiProviders\" " +
                "ADD CONSTRAINT \"CK_AiProviders_Category\" " +
                "CHECK (\"Category\" IN (0, 1, 2, 3));");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"AiProviders\" DROP CONSTRAINT IF EXISTS \"CK_AiProviders_Category\";");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "AiProviders");
        }
    }
}
