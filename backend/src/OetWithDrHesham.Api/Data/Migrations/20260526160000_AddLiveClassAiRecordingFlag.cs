using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260526160000_AddLiveClassAiRecordingFlag")]
    public partial class AddLiveClassAiRecordingFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Wave A2 — single feature flag for the AI recording-processing
            // pipeline (transcribe / summarize / translate / embed). Defaults
            // null which the provider treats as `false` until an admin opts in.
            //
            // 2026-05-28 — idempotent so it no-ops on EnsureCreated-bootstrapped
            // databases where the column already exists, and still adds it on a
            // fresh/production database.
            migrationBuilder.Sql(
                @"ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""LiveClassesAiRecordingProcessingEnabled"" boolean;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LiveClassesAiRecordingProcessingEnabled",
                table: "RuntimeSettings");
        }
    }
}
