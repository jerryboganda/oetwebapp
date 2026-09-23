using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    // HAND-AUTHORED (repo convention). The v1.1 persona-runtime and card-timing
    // snapshots declared CardSlot as varchar(2) while every standalone practice
    // session writes "standalone" — so creating any practice/free Speaking
    // session failed in production with 22001 (value too long). Widen to 16,
    // matching SpeakingSimulationV11AssessmentReports.CardSlot. The ModelSnapshot
    // IS updated.
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270102090300_WidenSpeakingV11CardSlot")]
    public partial class WidenSpeakingV11CardSlot : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "SpeakingSimulationV11PersonaRuntimeSnapshots" ALTER COLUMN "CardSlot" TYPE character varying(16);
                ALTER TABLE "SpeakingSimulationV11CardTimingSnapshots" ALTER COLUMN "CardSlot" TYPE character varying(16);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not narrowed back: existing "standalone" rows would not fit varchar(2).
        }
    }
}
