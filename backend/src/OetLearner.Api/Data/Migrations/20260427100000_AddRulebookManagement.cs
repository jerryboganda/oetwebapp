using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds the three tables that back admin-managed rulebooks:
    /// RulebookVersions, RulebookSectionRows, RulebookRuleRows.
    ///
    /// Idempotent CREATE TABLE IF NOT EXISTS, mirroring the recovery-migration
    /// pattern in 20260425060000_RecoverMissingTables, so re-running on an
    /// environment that has these tables (created out-of-band) is a no-op.
    /// </summary>
    public partial class AddRulebookManagement : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""RulebookVersions"" (
    ""Id"" character varying(128) NOT NULL,
    ""Kind"" character varying(32) NOT NULL,
    ""Profession"" character varying(32) NOT NULL,
    ""Version"" text NOT NULL,
    ""Status"" character varying(16) NOT NULL,
    ""AuthoritySource"" text NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    ""PublishedAt"" timestamp with time zone NULL,
    ""UpdatedByUserId"" text NULL,
    CONSTRAINT ""PK_RulebookVersions"" PRIMARY KEY (""Id"")
);

CREATE INDEX IF NOT EXISTS ""IX_RulebookVersions_Kind_Profession_Status""
    ON ""RulebookVersions"" (""Kind"", ""Profession"", ""Status"");

CREATE TABLE IF NOT EXISTS ""RulebookSectionRows"" (
    ""Id"" text NOT NULL,
    ""RulebookVersionId"" character varying(128) NOT NULL,
    ""Code"" text NOT NULL,
    ""Title"" text NOT NULL,
    ""OrderIndex"" integer NOT NULL,
    CONSTRAINT ""PK_RulebookSectionRows"" PRIMARY KEY (""Id"")
);

CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RulebookSectionRows_RulebookVersionId_Code""
    ON ""RulebookSectionRows"" (""RulebookVersionId"", ""Code"");

CREATE TABLE IF NOT EXISTS ""RulebookRuleRows"" (
    ""Id"" text NOT NULL,
    ""RulebookVersionId"" character varying(128) NOT NULL,
    ""Code"" text NOT NULL,
    ""SectionCode"" text NOT NULL,
    ""Title"" text NOT NULL,
    ""Body"" text NOT NULL,
    ""Severity"" text NOT NULL,
    ""AppliesToJson"" text NOT NULL,
    ""TurnStage"" text NULL,
    ""ExemplarPhrasesJson"" text NULL,
    ""ForbiddenPatternsJson"" text NULL,
    ""CheckId"" text NULL,
    ""ParamsJson"" text NULL,
    ""ExamplesJson"" text NULL,
    ""OrderIndex"" integer NOT NULL,
    CONSTRAINT ""PK_RulebookRuleRows"" PRIMARY KEY (""Id"")
);

CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RulebookRuleRows_RulebookVersionId_Code""
    ON ""RulebookRuleRows"" (""RulebookVersionId"", ""Code"");

CREATE INDEX IF NOT EXISTS ""IX_RulebookRuleRows_RulebookVersionId_SectionCode""
    ON ""RulebookRuleRows"" (""RulebookVersionId"", ""SectionCode"");
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RulebookRuleRows");
            migrationBuilder.DropTable(name: "RulebookSectionRows");
            migrationBuilder.DropTable(name: "RulebookVersions");
        }
    }
}
