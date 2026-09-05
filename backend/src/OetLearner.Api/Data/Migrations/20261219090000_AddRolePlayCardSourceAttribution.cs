using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Add <c>RolePlayCards.SourceAttribution</c> — the verbatim rights /
    /// provenance notice printed on the source card the row was transcribed
    /// from (e.g. "© Cambridge Boxhill Language Assessment / SEPTEMBER 2015").
    ///
    /// <para>Why: the bulk import of the owner's printed card corpus carries a
    /// rights-holder notice on a large share of the source pages. Dropping that
    /// string on import would delete the only record of where a card came from
    /// and strip a copyright notice off third-party material. Instead the
    /// notice is stored here, on the ADMIN projection only — it is deliberately
    /// absent from every learner-facing projection, so the printed card face a
    /// learner sees stays clean while the row keeps honest provenance.</para>
    ///
    /// <para>Purely additive: nullable, no backfill, no behaviour change for
    /// existing rows.</para>
    ///
    /// <para>⚠ <b>Postgres-only SQL.</b> The test suite uses SQLite via
    /// <c>EnsureCreatedAsync()</c> and builds straight from the model,
    /// bypassing migrations entirely.</para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20261219090000_AddRolePlayCardSourceAttribution")]
    public partial class AddRolePlayCardSourceAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""RolePlayCards""
                    ADD COLUMN IF NOT EXISTS ""SourceAttribution"" character varying(400);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""RolePlayCards"" DROP COLUMN IF EXISTS ""SourceAttribution"";");
        }
    }
}
