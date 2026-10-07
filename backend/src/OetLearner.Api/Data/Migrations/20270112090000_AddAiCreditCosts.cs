using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// SAMI Wave 3 (hand-authored): per-action AI Credit costs (SAMI §9.1) as
    /// admin-editable configuration. The handover baseline rows (Writing 2,
    /// Speaking card 2, full Speaking exam 4, Reading 1, Listening 1, Part A 1;
    /// deep-PDF and live-voice disabled until validated) are seeded idempotently
    /// at startup by <c>AiCreditCostService.SeedDefaultsAsync</c>.
    /// </remarks>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270112090000_AddAiCreditCosts")]
    public partial class AddAiCreditCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiCreditCosts",
                columns: table => new
                {
                    ActionCode = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Credits = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCreditCosts", x => x.ActionCode);
                });

            // Baseline rows (SAMI §9.1) are seeded by AiCreditCostService.SeedDefaultsAsync
            // at startup (idempotent) — hand migrations here carry pure DDL, matching the
            // remote-workers convention, so `migrations script` needs no model-backed data op.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AiCreditCosts");
        }
    }
}
