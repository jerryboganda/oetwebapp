using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Phase Q1 — Qwen3 Voice Studio + per-term audio provenance.
    /// Adds three settings columns so the admin can pin the active Qwen3
    /// model variant ("flash" preset OR "voicedesign" instructions) plus
    /// four <see cref="OetLearner.Api.Domain.VocabularyTerm"/> columns that
    /// record which provider/variant/voice generated the current audio and
    /// when. The provenance fields power the "Regenerate where voice ≠
    /// current" admin action so we don't re-synthesise terms that already
    /// match the desired voice.
    ///
    /// Hand-written Postgres SQL (no EF Core tool on the build host).
    /// SQLite test runs bypass migrations via <c>EnsureCreatedAsync()</c>.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260524000000_AddQwen3VoiceStudioAndAudioTracking")]
    public partial class AddQwen3VoiceStudioAndAudioTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""ConversationSettings""
                    ADD COLUMN IF NOT EXISTS ""Qwen3ModelVariant"" varchar(32) NULL,
                    ADD COLUMN IF NOT EXISTS ""Qwen3VoiceId"" varchar(64) NULL,
                    ADD COLUMN IF NOT EXISTS ""Qwen3VoiceInstructions"" text NULL;

                ALTER TABLE ""VocabularyTerms""
                    ADD COLUMN IF NOT EXISTS ""AudioProvider"" varchar(32) NULL,
                    ADD COLUMN IF NOT EXISTS ""AudioVoice"" varchar(64) NULL,
                    ADD COLUMN IF NOT EXISTS ""AudioModelVariant"" varchar(32) NULL,
                    ADD COLUMN IF NOT EXISTS ""AudioGeneratedAt"" timestamptz NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""ConversationSettings""
                    DROP COLUMN IF EXISTS ""Qwen3ModelVariant"",
                    DROP COLUMN IF EXISTS ""Qwen3VoiceId"",
                    DROP COLUMN IF EXISTS ""Qwen3VoiceInstructions"";

                ALTER TABLE ""VocabularyTerms""
                    DROP COLUMN IF EXISTS ""AudioProvider"",
                    DROP COLUMN IF EXISTS ""AudioVoice"",
                    DROP COLUMN IF EXISTS ""AudioModelVariant"",
                    DROP COLUMN IF EXISTS ""AudioGeneratedAt"";
            ");
        }
    }
}
