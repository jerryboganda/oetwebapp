using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// 2026-05-28 rulebook compliance audit — additive-only migration.
    ///
    /// Adds:
    ///   - RulebookVersion (varchar(32)) to SpeakingSessions, ListeningAttempts, ReadingAttempts
    ///     for post-hoc audit traceability (which rulebook version applied to a graded attempt).
    ///   - SpeakingWhisperApiKeyEncrypted/BaseUrl/Model to RuntimeSettings so admins can
    ///     rotate the Whisper API key from the runtime-settings UI.
    ///   - ParagraphIndex (int) to ReadingQuestions for the R07.6 paragraph-order lint.
    ///
    /// All columns are nullable; no data loss; no FK changes; no index drops. The
    /// scaffolded migration was replaced by this hand-written version because the
    /// EF model snapshot had pre-existing drift that caused the scaffolder to emit
    /// a full database rebuild rather than the delta.
    /// </summary>
    public partial class Audit20260528_RulebookCompliance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RulebookVersion",
                table: "SpeakingSessions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RulebookVersion",
                table: "ListeningAttempts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RulebookVersion",
                table: "ReadingAttempts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpeakingWhisperApiKeyEncrypted",
                table: "RuntimeSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpeakingWhisperBaseUrl",
                table: "RuntimeSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpeakingWhisperModel",
                table: "RuntimeSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParagraphIndex",
                table: "ReadingQuestions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ParagraphIndex", table: "ReadingQuestions");
            migrationBuilder.DropColumn(name: "SpeakingWhisperModel", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SpeakingWhisperBaseUrl", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "SpeakingWhisperApiKeyEncrypted", table: "RuntimeSettings");
            migrationBuilder.DropColumn(name: "RulebookVersion", table: "ReadingAttempts");
            migrationBuilder.DropColumn(name: "RulebookVersion", table: "ListeningAttempts");
            migrationBuilder.DropColumn(name: "RulebookVersion", table: "SpeakingSessions");
        }
    }
}
