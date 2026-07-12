using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260726090000_AddRadiographyProfession")]
    public partial class AddRadiographyProfession : Migration
    {
        // Owner directive 2026-07-12: promote "Radiography" to a first-class profession available
        // wherever every other profession is — registration (SignupProfessionCatalog), the profession
        // reference registry (Professions), billing discipline tabs, and (crucially) the Materials
        // library discipline filter, which recognises a folder as a discipline only when its name
        // matches a row in Professions.
        //
        // WHY A MIGRATION (not just the SeedData rows): production is already seeded, so the
        // SeedReferenceData / SignupProfessionCatalog seeds never re-run there. New reference rows
        // reach prod only via a migration (same reasoning as 20260725090000). Data-only — no schema
        // change, so the model snapshot is unaffected.
        //
        // SAFETY:
        //   * Idempotent — ON CONFLICT ("Id") DO NOTHING, so re-runs and any row an admin already
        //     added via the Signup Catalog admin UI are no-ops.
        //   * SignupProfessionCatalog is the platform source of truth and normally mirrors itself
        //     into Professions (AdminService.SignupCatalog.SyncProfessionTaxonomyAsync); this backfill
        //     writes BOTH tables directly so the pair is consistent without an admin action.
        //   * No FK references the profession id (ActiveProfessionId is a plain string column), so the
        //     insert cannot violate any constraint.

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Profession reference registry (read by MaterialAccessService's discipline filter,
            // LearnerService.GetProfessionsAsync, content tagging, etc.).
            migrationBuilder.Sql(@"
INSERT INTO ""Professions"" (""Id"", ""Code"", ""Label"", ""Status"", ""SortOrder"")
VALUES ('radiography', 'radiography', 'Radiography', 'active', 6)
ON CONFLICT (""Id"") DO NOTHING;
");

            // Registration catalog (validated by AuthService.ValidateSignupSelectionAsync; a learner's
            // ActiveProfessionId can only be an Id from this table).
            migrationBuilder.Sql(@"
INSERT INTO ""SignupProfessionCatalog""
    (""Id"", ""Label"", ""Description"", ""ExamTypeIdsJson"", ""CountryTargetsJson"", ""SortOrder"", ""IsActive"")
VALUES
    ('radiography', 'Radiography', 'Radiographers and medical imaging candidates.', '[""oet""]', '[]', 6, true)
ON CONFLICT (""Id"") DO NOTHING;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DELETE FROM ""SignupProfessionCatalog"" WHERE ""Id"" = 'radiography';");
            migrationBuilder.Sql(@"DELETE FROM ""Professions"" WHERE ""Id"" = 'radiography';");
        }
    }
}
