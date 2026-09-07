using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelSnapshotNoSchemaChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: every table/column in the current model was already
            // created by an earlier hand-authored migration; only the model snapshot
            // was stale. This migration resyncs the snapshot with zero schema changes.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: every table/column in the current model was already
            // created by an earlier hand-authored migration; only the model snapshot
            // was stale. This migration resyncs the snapshot with zero schema changes.
        }
    }
}
