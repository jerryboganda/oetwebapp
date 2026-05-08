using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// GitHub Copilot integration — Phase 4. Adds <c>LastTestedAt</c>,
    /// <c>LastTestStatus</c>, <c>LastTestError</c> to both
    /// <c>AiProviders</c> and <c>AiProviderAccounts</c> so the admin
    /// "Test connection" probe can persist a status pill + timestamp
    /// per row. See <c>docs/AI-COPILOT-PROGRESS.md</c> Phase 4 and
    /// <c>Services/Rulebook/AiProviderConnectionTester.cs</c>.
    /// </remarks>
    public partial class AddLastTestStatusToAiProviderAndAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastTestedAt",
                table: "AiProviders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastTestStatus",
                table: "AiProviders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastTestError",
                table: "AiProviders",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastTestedAt",
                table: "AiProviderAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastTestStatus",
                table: "AiProviderAccounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastTestError",
                table: "AiProviderAccounts",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "LastTestedAt", table: "AiProviders");
            migrationBuilder.DropColumn(name: "LastTestStatus", table: "AiProviders");
            migrationBuilder.DropColumn(name: "LastTestError", table: "AiProviders");
            migrationBuilder.DropColumn(name: "LastTestedAt", table: "AiProviderAccounts");
            migrationBuilder.DropColumn(name: "LastTestStatus", table: "AiProviderAccounts");
            migrationBuilder.DropColumn(name: "LastTestError", table: "AiProviderAccounts");
        }
    }
}
