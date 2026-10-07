using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// SAMI §9 per-user companion access (hand-authored): the per-LEARNER layer on
    /// top of the per-plan <c>PlanModuleOverrides</c> and the plan's catalog module
    /// list, with provenance (admin enable / promotional grant / manual disable),
    /// an optional expiry and an operator note, so an admin can enable a
    /// non-eligible learner or disable an eligible one and the effective source and
    /// reason surface on <c>GET /v1/companion/session</c> and the operator read.
    ///
    /// <para>
    /// Purely additive: one brand-new table, no backfill and no change to any
    /// existing object. Rows live outside <c>UserModuleOverrides</c> on purpose —
    /// that table is rewritten wholesale by the admin user-access scope save, which
    /// would wipe provenance/expiry/note.
    /// </para>
    ///
    /// <para>
    /// Id <c>20270116090000</c> sorts after the current maximum in this folder
    /// (<c>20270115090000_AddAssistantProviderState</c>) and after
    /// <c>20270113090000_AddCompanionHandoffs</c>, so EF applies it last on both a
    /// fresh database and production.
    /// </para>
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270116090000_CompanionUserAccess")]
    public partial class CompanionUserAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanionUserAccesses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModuleKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanionUserAccesses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanionUserAccesses_UserId",
                table: "CompanionUserAccesses",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanionUserAccesses_UserId_ModuleKey",
                table: "CompanionUserAccesses",
                columns: new[] { "UserId", "ModuleKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CompanionUserAccesses");
        }
    }
}
