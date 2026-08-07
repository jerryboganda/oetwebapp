using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Adds RuntimeSettings.SecurityDeviceVerificationExemptEmails (spec
    /// §3.2/§3.3 safety valve) and seeds it with the owner-designated
    /// staff/personal accounts that must never be blocked by the
    /// trusted-device OTP challenge or the risk-based step-up.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260827090000_AddDeviceVerificationExemptEmails")]
    public partial class AddDeviceVerificationExemptEmails : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (!ActiveProvider.Contains("Npgsql", System.StringComparison.OrdinalIgnoreCase)) return;

            migrationBuilder.Sql(@"
ALTER TABLE ""RuntimeSettings""
ADD COLUMN IF NOT EXISTS ""SecurityDeviceVerificationExemptEmails"" character varying(2000);

UPDATE ""RuntimeSettings""
SET ""SecurityDeviceVerificationExemptEmails"" = 'AHMEDIBRAHIMABDRABUIBRAHIM@GMAIL.COM,DRHAGERMURAD@OETWITHDRHESHAM.CO.UK,DRAHMEDHESHAM9595@GMAIL.COM,DRAHMEDHESHAM19951995@GMAIL.COM,DRHAGERMURAD2026@GMAIL.COM,TUTORCOMMERCEACADEMY2026@GMAIL.COM,HAGER111118@GMAIL.COM,SUPPORT@OETWITHDRHESHAM.CO.UK',
    ""UpdatedAt"" = NOW()
WHERE ""SecurityDeviceVerificationExemptEmails"" IS NULL;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""SecurityDeviceVerificationExemptEmails"";
");
        }
    }
}
