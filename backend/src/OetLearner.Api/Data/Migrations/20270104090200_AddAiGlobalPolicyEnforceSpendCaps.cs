using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// WAI-02 (owner directive 2026-10-02): platform AI spend caps become one
    /// admin switch, OFF by default. Adds <c>AiGlobalPolicies.EnforceSpendCaps</c>
    /// as FALSE for the existing singleton row; the column default also covers
    /// rows inserted by an older image during the blue/green overlap. A re-run
    /// never resets an admin's choice (ADD COLUMN IF NOT EXISTS, no UPDATE).
    ///
    /// HAND-AUTHORED (repo convention): inline <c>[Migration]</c>/<c>[DbContext]</c>,
    /// no Designer file; the AiGlobalPolicy block of the model snapshot is
    /// edited by hand.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270104090200_AddAiGlobalPolicyEnforceSpendCaps")]
    public partial class AddAiGlobalPolicyEnforceSpendCaps : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "AiGlobalPolicies" ADD COLUMN IF NOT EXISTS "EnforceSpendCaps" boolean NOT NULL DEFAULT FALSE;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "AiGlobalPolicies" DROP COLUMN IF EXISTS "EnforceSpendCaps";
                """);
        }
    }
}
