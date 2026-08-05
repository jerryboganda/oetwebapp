using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>Persists the optional per-learner approved client-identity
    /// limit. Null uses the strict one-identity default; the admin API accepts
    /// only positive values from 1 through 5.</summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260828090000_AddLearnerDeviceLimitOverride")]
    public partial class AddLearnerDeviceLimitOverride : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (!ActiveProvider.Contains("Npgsql", System.StringComparison.OrdinalIgnoreCase)) return;

            migrationBuilder.Sql(@"
ALTER TABLE ""ApplicationUserAccounts""
ADD COLUMN IF NOT EXISTS ""MaxDevicesOverride"" integer;

UPDATE ""ApplicationUserAccounts""
SET ""MaxDevicesOverride"" = NULL
WHERE ""MaxDevicesOverride"" IS NOT NULL
  AND (""MaxDevicesOverride"" <= 0 OR ""MaxDevicesOverride"" > 5);
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (!ActiveProvider.Contains("Npgsql", System.StringComparison.OrdinalIgnoreCase)) return;

            migrationBuilder.Sql(@"
ALTER TABLE ""ApplicationUserAccounts""
DROP COLUMN IF EXISTS ""MaxDevicesOverride"";
");
        }
    }
}
