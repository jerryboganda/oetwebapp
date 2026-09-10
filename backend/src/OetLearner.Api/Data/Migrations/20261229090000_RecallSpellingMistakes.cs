using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Recall vocabulary — Practice Spelling and the mini Spelling Test
    /// (developer brief §3B/§3C/§3D).
    ///
    /// One row per (learner, recall word) that the learner has spelled
    /// incorrectly. A word is inserted on the first miss, its
    /// <c>WrongAttemptCount</c> incremented on every later miss, and the row is
    /// <b>deleted</b> when the learner spells it correctly from Review Mistakes.
    ///
    /// Storage rules from the brief:
    /// <list type="bullet">
    /// <item>Only a reference to the existing recall word is kept
    /// (<c>VocabularyTermId</c>) — the canonical spelling and the ElevenLabs audio
    /// live on <c>VocabularyTerms</c> and are never duplicated.</item>
    /// <item>Server-side, so the list survives logout, app restart and a switch of
    /// device. No AI/LLM is involved: pass/fail is a direct string comparison.</item>
    /// </list>
    ///
    /// Purely additive and idempotent (<c>CREATE TABLE IF NOT EXISTS</c>); no
    /// existing table is touched. <c>Down</c> drops only this table.
    ///
    /// Deliberately no FK to <c>VocabularyTerms</c>: every other learner-owned
    /// reference in this schema (e.g. <c>RecallBookmarks.VocabularyTermId</c>) is a
    /// plain indexed string, and recall content is bulk-replaced by the admin
    /// uploader, so a hard FK would make content refreshes fail. The service always
    /// resolves the term before writing a row, and the unique index keeps the
    /// per-learner list free of duplicates.
    ///
    /// ⚠ Postgres-only SQL. The test suite uses SQLite via EnsureCreatedAsync() and
    /// builds from the model, bypassing migrations entirely.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261229090000_RecallSpellingMistakes")]
    public partial class RecallSpellingMistakes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""RecallSpellingMistakes"" (
                    ""Id""                character varying(64) NOT NULL,
                    ""UserId""            character varying(64) NOT NULL,
                    ""VocabularyTermId""  character varying(64) NOT NULL,
                    ""WrongAttemptCount"" integer NOT NULL DEFAULT 1,
                    ""LastWrongAt""       timestamp with time zone NOT NULL DEFAULT NOW(),
                    ""CreatedAt""         timestamp with time zone NOT NULL DEFAULT NOW(),
                    CONSTRAINT ""PK_RecallSpellingMistakes"" PRIMARY KEY (""Id"")
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RecallSpellingMistakes_UserId_VocabularyTermId""
                    ON ""RecallSpellingMistakes"" (""UserId"", ""VocabularyTermId"");

                CREATE INDEX IF NOT EXISTS ""IX_RecallSpellingMistakes_UserId_LastWrongAt""
                    ON ""RecallSpellingMistakes"" (""UserId"", ""LastWrongAt"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""RecallSpellingMistakes"";");
        }
    }
}
