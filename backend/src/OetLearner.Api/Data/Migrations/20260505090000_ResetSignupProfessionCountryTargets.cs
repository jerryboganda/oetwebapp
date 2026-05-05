using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Resets the seeded SignupProfessionCatalog.CountryTargetsJson values to
    /// "[]" (= "all PRD target countries allowed") so the new admin-managed
    /// CRUD surface starts from a permissive default. Admins can still narrow
    /// the per-profession country list through /admin/signup-catalog. Only
    /// rows whose JSON still matches the original seeded subsets are touched
    /// so any value already edited via the new admin UI is preserved.
    /// PostgreSQL only; the SQLite test suite uses EnsureCreated and skips
    /// migrations entirely.
    /// </summary>
    /// <inheritdoc />
    public partial class ResetSignupProfessionCountryTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "SignupProfessionCatalog"
                SET "CountryTargetsJson" = '[]'
                WHERE ("Id" = 'nursing'             AND "CountryTargetsJson" = '["Australia","New Zealand"]')
                   OR ("Id" = 'medicine'            AND "CountryTargetsJson" = '["United Kingdom","Australia"]')
                   OR ("Id" = 'pharmacy'            AND "CountryTargetsJson" = '["Ireland","Australia"]')
                   OR ("Id" = 'dentistry'           AND "CountryTargetsJson" = '["United Kingdom","New Zealand"]')
                   OR ("Id" = 'physiotherapy'       AND "CountryTargetsJson" = '["United Kingdom","Australia","New Zealand"]')
                   OR ("Id" = 'other-allied-health' AND "CountryTargetsJson" = '["United Kingdom","Australia","New Zealand","Ireland","Canada"]')
                   OR ("Id" = 'academic-english'    AND "CountryTargetsJson" = '["Canada","United Kingdom","Australia"]');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down is intentionally a no-op: restoring arbitrary historical
            // country subsets would silently overwrite admin-managed values.
        }
    }
}
