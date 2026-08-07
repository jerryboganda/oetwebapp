using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>Backfills the canonical Gmail address for the owner account
    /// whose legacy exemption entry used a different email address. The
    /// update is additive so admin-managed exemptions are preserved.</summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260830090000_BackfillDeviceVerificationExemptionEmail")]
    public partial class BackfillDeviceVerificationExemptionEmail : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (!ActiveProvider.Contains("Npgsql", System.StringComparison.OrdinalIgnoreCase)) return;

            migrationBuilder.Sql(@"
UPDATE ""RuntimeSettings""
SET ""SecurityDeviceVerificationExemptEmails"" = CASE
        WHEN NULLIF(BTRIM(""SecurityDeviceVerificationExemptEmails""), '') IS NULL
            THEN 'DRHAGERMURAD2026@GMAIL.COM'
        ELSE BTRIM(""SecurityDeviceVerificationExemptEmails"") || ',DRHAGERMURAD2026@GMAIL.COM'
    END,
    ""UpdatedAt"" = NOW()
WHERE ""SecurityDeviceVerificationExemptEmails"" IS NULL
   OR POSITION('DRHAGERMURAD2026@GMAIL.COM' IN UPPER(""SecurityDeviceVerificationExemptEmails"")) = 0;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately no-op: the address may have been retained or edited
            // by an administrator after this additive backfill ran.
        }
    }
}
