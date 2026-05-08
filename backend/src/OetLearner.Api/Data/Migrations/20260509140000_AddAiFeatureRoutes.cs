using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// GitHub Copilot integration — Phase 7. Adds the
    /// <c>AiFeatureRoutes</c> table so admins can pin individual feature
    /// codes to specific providers, overriding the global failover-priority
    /// default. See <c>docs/AI-COPILOT-PROGRESS.md</c> Phase 7 and
    /// <c>Services/Rulebook/AiFeatureRouteResolver.cs</c>.
    /// </remarks>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260509140000_AddAiFeatureRoutes")]
    public partial class AddAiFeatureRoutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiFeatureRoutes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FeatureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiFeatureRoutes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiFeatureRoutes_FeatureCode",
                table: "AiFeatureRoutes",
                column: "FeatureCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AiFeatureRoutes");
        }
    }
}
