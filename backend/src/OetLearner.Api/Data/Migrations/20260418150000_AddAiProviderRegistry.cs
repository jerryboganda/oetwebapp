using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Slice 5 of the AI Usage Management subsystem. Adds the DB-backed
    /// provider registry so admins can add/rotate platform providers without
    /// a redeploy.
    /// </remarks>
    public partial class AddAiProviderRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiProviders",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Dialect = table.Column<int>(type: "integer", nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    EncryptedApiKey = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    ApiKeyHint = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DefaultModel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AllowedModelsCsv = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    PricePer1kPromptTokens = table.Column<decimal>(type: "numeric", nullable: false),
                    PricePer1kCompletionTokens = table.Column<decimal>(type: "numeric", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    CircuitBreakerThreshold = table.Column<int>(type: "integer", nullable: false),
                    CircuitBreakerWindowSeconds = table.Column<int>(type: "integer", nullable: false),
                    FailoverPriority = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProviders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiProviders_Code",
                table: "AiProviders",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AiProviders");
        }
    }
}
