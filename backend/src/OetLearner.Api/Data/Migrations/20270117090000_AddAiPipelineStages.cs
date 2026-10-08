using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// AI Pipeline Control Center (hand-authored, purely additive): the saved per-stage provider order and its
    /// immutable revision history (audit trail, last-known-good, rollback). Two brand-new tables, no backfill,
    /// no change to any existing object; old code keeps running against the new schema during blue/green overlap.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270117090000_AddAiPipelineStages")]
    public partial class AddAiPipelineStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiPipelineStages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StageKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChainJson = table.Column<string>(type: "text", nullable: false),
                    StageEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiPipelineStages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiPipelineStages_StageKey",
                table: "AiPipelineStages",
                column: "StageKey",
                unique: true);

            migrationBuilder.CreateTable(
                name: "AiPipelineStageRevisions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StageKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ChainJson = table.Column<string>(type: "text", nullable: false),
                    StageEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ChangedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ChangedByName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiPipelineStageRevisions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiPipelineStageRevisions_StageKey_Version",
                table: "AiPipelineStageRevisions",
                columns: new[] { "StageKey", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AiPipelineStageRevisions");
            migrationBuilder.DropTable(name: "AiPipelineStages");
        }
    }
}
