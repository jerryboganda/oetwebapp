using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Removes the five writing-task field groups from the schema:
    ///   • Recipient            → WritingScenarios.RecipientJson
    ///   • Case notes           → WritingScenarios.CaseNotesMarkdown / CaseNotesStructuredJson / CaseNoteSectionsJson
    ///   • Model answer          → WritingScenarios.ModelAnswerExemplarId + the WritingExemplar* tables
    ///   • Key content checklist → WritingContentChecklistItems (required/optional rows)
    ///   • Distractors           → WritingContentChecklistItems (irrelevant rows)
    ///
    /// Hand-written idempotent SQL (house style) so it applies cleanly on both fresh
    /// and existing databases and is safe to re-run. The Down() best-effort recreates
    /// the dropped columns and tables (data is not restored).
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260629090000_RemoveWritingTaskFieldGroups")]
    public partial class RemoveWritingTaskFieldGroups : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Scenario columns ────────────────────────────────────────────────
            migrationBuilder.Sql(@"ALTER TABLE ""WritingScenarios"" DROP COLUMN IF EXISTS ""RecipientJson"";");
            migrationBuilder.Sql(@"ALTER TABLE ""WritingScenarios"" DROP COLUMN IF EXISTS ""CaseNoteSectionsJson"";");
            migrationBuilder.Sql(@"ALTER TABLE ""WritingScenarios"" DROP COLUMN IF EXISTS ""CaseNotesMarkdown"";");
            migrationBuilder.Sql(@"ALTER TABLE ""WritingScenarios"" DROP COLUMN IF EXISTS ""CaseNotesStructuredJson"";");
            migrationBuilder.Sql(@"ALTER TABLE ""WritingScenarios"" DROP COLUMN IF EXISTS ""ModelAnswerExemplarId"";");

            // ── Content checklist (key content + distractors) ───────────────────
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""WritingContentChecklistItems"";");

            // ── Model-answer / exemplar library (FK order: embeddings → annotations → exemplars) ──
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""WritingExemplarEmbeddings"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""WritingExemplarAnnotations"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""WritingExemplars"";");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ── Scenario columns ────────────────────────────────────────────────
            migrationBuilder.Sql(@"ALTER TABLE ""WritingScenarios"" ADD COLUMN IF NOT EXISTS ""CaseNotesMarkdown"" text NOT NULL DEFAULT '';");
            migrationBuilder.Sql(@"ALTER TABLE ""WritingScenarios"" ADD COLUMN IF NOT EXISTS ""CaseNotesStructuredJson"" jsonb NULL;");
            migrationBuilder.Sql(@"ALTER TABLE ""WritingScenarios"" ADD COLUMN IF NOT EXISTS ""RecipientJson"" jsonb NULL;");
            migrationBuilder.Sql(@"ALTER TABLE ""WritingScenarios"" ADD COLUMN IF NOT EXISTS ""CaseNoteSectionsJson"" jsonb NULL;");
            migrationBuilder.Sql(@"ALTER TABLE ""WritingScenarios"" ADD COLUMN IF NOT EXISTS ""ModelAnswerExemplarId"" uuid NULL;");

            // ── Content checklist ───────────────────────────────────────────────
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""WritingContentChecklistItems"" (
    ""Id"" uuid NOT NULL,
    ""ScenarioId"" uuid NOT NULL,
    ""ItemText"" character varying(500) NOT NULL,
    ""Category"" character varying(64) NOT NULL,
    ""Importance"" character varying(16) NOT NULL,
    ""RequiredStatus"" character varying(16) NOT NULL,
    ""LinkedCaseNoteSection"" character varying(200) NULL,
    ""ExpectedRepresentation"" character varying(1000) NULL,
    ""CommonError"" character varying(1000) NULL,
    ""Ordinal"" integer NOT NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_WritingContentChecklistItems"" PRIMARY KEY (""Id"")
);");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_WritingContentChecklistItems_ScenarioId_Ordinal"" ON ""WritingContentChecklistItems"" (""ScenarioId"", ""Ordinal"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_WritingContentChecklistItems_ScenarioId_RequiredStatus"" ON ""WritingContentChecklistItems"" (""ScenarioId"", ""RequiredStatus"");");

            // ── Exemplar library ────────────────────────────────────────────────
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""WritingExemplars"" (
    ""Id"" uuid NOT NULL,
    ""ScenarioId"" uuid NULL,
    ""LetterType"" character varying(8) NOT NULL,
    ""Profession"" character varying(64) NOT NULL,
    ""LetterContent"" text NOT NULL,
    ""AnnotationsJson"" jsonb NOT NULL DEFAULT '[]',
    ""TargetBand"" character varying(8) NOT NULL,
    ""Status"" character varying(16) NOT NULL,
    ""AuthorId"" character varying(64) NOT NULL,
    ""PublishedAt"" timestamp with time zone NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_WritingExemplars"" PRIMARY KEY (""Id"")
);");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_WritingExemplars_Profession_LetterType"" ON ""WritingExemplars"" (""Profession"", ""LetterType"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_WritingExemplars_Status"" ON ""WritingExemplars"" (""Status"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_WritingExemplars_ScenarioId"" ON ""WritingExemplars"" (""ScenarioId"");");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""WritingExemplarAnnotations"" (
    ""Id"" uuid NOT NULL,
    ""ExemplarId"" uuid NOT NULL,
    ""Ordinal"" integer NOT NULL,
    ""CharStart"" integer NULL,
    ""CharEnd"" integer NULL,
    ""AnnotationType"" character varying(64) NOT NULL,
    ""RuleId"" character varying(16) NULL,
    ""Note"" character varying(1000) NOT NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_WritingExemplarAnnotations"" PRIMARY KEY (""Id"")
);");
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_WritingExemplarAnnotations_ExemplarId_Ordinal"" ON ""WritingExemplarAnnotations"" (""ExemplarId"", ""Ordinal"");");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""WritingExemplarEmbeddings"" (
    ""Id"" uuid NOT NULL,
    ""ExemplarId"" uuid NOT NULL,
    ""ModelId"" character varying(64) NOT NULL,
    ""Dimensions"" integer NOT NULL,
    ""EmbeddingJson"" text NOT NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_WritingExemplarEmbeddings"" PRIMARY KEY (""Id"")
);");
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_WritingExemplarEmbeddings_ExemplarId"" ON ""WritingExemplarEmbeddings"" (""ExemplarId"");");
        }
    }
}
