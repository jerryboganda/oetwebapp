using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// GitHub Copilot integration — Phase 2 Slice 2a. Adds the
    /// <c>AiProviderAccount</c> table that holds many credential / quota
    /// slots under one <c>AiProvider</c> row. The Copilot adapter walks
    /// these accounts in (Priority asc, RequestsUsedThisMonth asc) order
    /// and atomically reserves the next available slot via
    /// <c>ExecuteUpdateAsync</c>. See
    /// <c>backend/src/OetLearner.Api/Services/Rulebook/AiProviderAccountRegistry.cs</c>
    /// and <c>docs/AI-COPILOT-PROGRESS.md</c> Phase 2.
    /// </remarks>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260508120000_AddAiProviderAccount")]
    public partial class AddAiProviderAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiProviderAccounts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EncryptedApiKey = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    ApiKeyHint = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MonthlyRequestCap = table.Column<int>(type: "integer", nullable: true),
                    RequestsUsedThisMonth = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    ExhaustedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    PeriodMonthKey = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProviderAccounts", x => x.Id);
                });

            // Composite indexes mirror the ordering used by
            // PickAndReserveAsync's candidate query.
            migrationBuilder.CreateIndex(
                name: "IX_AiProviderAccounts_ProviderId_Priority",
                table: "AiProviderAccounts",
                columns: new[] { "ProviderId", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderAccounts_ProviderId_IsActive",
                table: "AiProviderAccounts",
                columns: new[] { "ProviderId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AiProviderAccounts");
        }
    }
}
