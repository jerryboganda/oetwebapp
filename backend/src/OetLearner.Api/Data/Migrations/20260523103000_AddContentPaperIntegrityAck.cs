using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Writing module — admin authoring workflow (spec §1C / §19).
    /// Adds two nullable columns to <c>ContentPapers</c> that capture the
    /// admin's content-integrity acknowledgement (no recalled or leaked OET
    /// exam content) on writing-task creation:
    /// <list type="bullet">
    ///   <item><c>IntegrityAcknowledgedByAdminId</c> — admin user id.</item>
    ///   <item><c>IntegrityAcknowledgedAt</c> — timestamp of the acknowledgement.</item>
    /// </list>
    ///
    /// Both columns are nullable so existing rows (seeded papers, listening /
    /// reading / speaking papers, papers created before this migration) stay
    /// valid. The gate is enforced only on the new writing-create path in
    /// <c>ContentPaperService.CreateAsync</c>.
    ///
    /// ⚠ Postgres-only SQL — uses <c>ADD COLUMN IF NOT EXISTS</c>. SQLite
    /// tests bypass migrations via <c>EnsureCreatedAsync()</c>.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260523103000_AddContentPaperIntegrityAck")]
    public partial class AddContentPaperIntegrityAck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""ContentPapers""
                    ADD COLUMN IF NOT EXISTS ""IntegrityAcknowledgedByAdminId"" varchar(64) NULL;
                ALTER TABLE ""ContentPapers""
                    ADD COLUMN IF NOT EXISTS ""IntegrityAcknowledgedAt"" timestamp with time zone NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""ContentPapers"" DROP COLUMN IF EXISTS ""IntegrityAcknowledgedByAdminId"";
                ALTER TABLE ""ContentPapers"" DROP COLUMN IF EXISTS ""IntegrityAcknowledgedAt"";
            ");
        }
    }
}
