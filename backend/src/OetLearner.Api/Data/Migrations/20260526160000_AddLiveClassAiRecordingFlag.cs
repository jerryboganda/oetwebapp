using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
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
            migrationBuilder.AddColumn<bool>(
                name: "LiveClassesAiRecordingProcessingEnabled",
                table: "RuntimeSettings",
                type: "boolean",
                nullable: true);
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
