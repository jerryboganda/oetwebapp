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
    /// admin-editable configuration. Seeds the handover baseline: Writing
    /// assessment 2, Speaking role card 2, full two-card Speaking exam 4,
    /// Reading analysis 1, Listening analysis 1, Listening Part A 1; the deep
    /// PDF and live-voice conversions ship disabled until validated.
    /// </remarks>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270112090000_AddAiCreditCosts")]
    public partial class AddAiCreditCosts : Migration
    {
        private static readonly DateTimeOffset SeededAt = new(2027, 1, 12, 9, 0, 0, TimeSpan.Zero);

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

            migrationBuilder.InsertData(
                table: "AiCreditCosts",
                columns: new[] { "ActionCode", "Credits", "Enabled", "Description", "UpdatedAt" },
                values: new object?[,]
                {
                    { "writing.assessment", 2, true, "One Writing case note / letter assessment", SeededAt },
                    { "speaking.role_card", 2, true, "One Speaking role card assessment", SeededAt },
                    { "speaking.exam_full", 4, true, "Full two-card Speaking exam assessment", SeededAt },
                    { "reading.analysis", 1, true, "Full Reading exam analysis", SeededAt },
                    { "listening.analysis", 1, true, "Full Listening exam analysis", SeededAt },
                    { "listening.part_a", 1, true, "Listening Part A analysis", SeededAt },
                    { "pdf.deep_analysis", 0, false, "Large PDF deep analysis (configurable; charge shown before start)", SeededAt },
                    { "voice.live", 0, false, "Live voice role play / extended audio (configurable conversion)", SeededAt },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AiCreditCosts");
        }
    }
}
