using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Remote-worker boundary (OET-RWP/1, owner decision 5 Oct 2026): four additive, idempotent
    /// tables - <c>RemoteWorkers</c>, <c>RemoteCredentials</c>, <c>RemoteJobs</c>,
    /// <c>RemoteJobOutputs</c>. Nothing reads them until a feature flag is turned on (all default OFF),
    /// so the blue/green slots overlap safely and the previous slot is unaffected.
    ///
    /// <para>
    /// HAND-AUTHORED (ADR 0001): inline <c>[Migration]</c>/<c>[DbContext]</c>, no Designer file, the
    /// ModelSnapshot is left alone. Postgres only: the claim statement uses <c>FOR UPDATE SKIP LOCKED</c>
    /// and the lease/fence arithmetic uses <c>clock_timestamp()</c>; SQLite/InMemory hosts get the
    /// entities from the model and never register the services. The DDL text lives in
    /// <see cref="RemoteJobsSchemaSql"/> so the integration tests execute the very same statements.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270110090000_AddRemoteWorkersAndJobs")]
    public partial class AddRemoteWorkersAndJobs : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (!migrationBuilder.ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)) return;

            migrationBuilder.Sql(RemoteJobsSchemaSql.Up);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (!migrationBuilder.ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)) return;

            migrationBuilder.Sql(RemoteJobsSchemaSql.Down);
        }
    }
}
