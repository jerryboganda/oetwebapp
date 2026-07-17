using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiAssistantEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "AllowedModelsCsv",
                table: "AiProviders",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024);

            migrationBuilder.CreateTable(
                name: "AiAssistantThreads",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ModelOverride = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAssistantThreads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiCodebaseChunks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChunkType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SymbolName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    StartLine = table.Column<int>(type: "integer", nullable: false),
                    EndLine = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    TokenCount = table.Column<int>(type: "integer", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TsVectorConfig = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Embedding = table.Column<float[]>(type: "real[]", nullable: true),
                    IndexedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EmbeddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCodebaseChunks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiFileBackups",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MessageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FilePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    OriginalContent = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    AutosaveBranch = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiFileBackups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiAssistantMessages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ThreadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: true),
                    ToolCallsJson = table.Column<string>(type: "text", nullable: true),
                    ToolCallId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ToolName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TokenCount = table.Column<int>(type: "integer", nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AiUsageRecordId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAssistantMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiAssistantMessages_AiAssistantThreads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "AiAssistantThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantMessages_ThreadId_CreatedAt",
                table: "AiAssistantMessages",
                columns: new[] { "ThreadId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantThreads_UserId_Role",
                table: "AiAssistantThreads",
                columns: new[] { "UserId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantThreads_UserId_UpdatedAt",
                table: "AiAssistantThreads",
                columns: new[] { "UserId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiCodebaseChunks_FilePath",
                table: "AiCodebaseChunks",
                column: "FilePath");

            migrationBuilder.CreateIndex(
                name: "IX_AiCodebaseChunks_Language",
                table: "AiCodebaseChunks",
                column: "Language");

            migrationBuilder.CreateIndex(
                name: "IX_AiFileBackups_FilePath_CreatedAt",
                table: "AiFileBackups",
                columns: new[] { "FilePath", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiFileBackups_ThreadId_CreatedAt",
                table: "AiFileBackups",
                columns: new[] { "ThreadId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiAssistantMessages");

            migrationBuilder.DropTable(
                name: "AiCodebaseChunks");

            migrationBuilder.DropTable(
                name: "AiFileBackups");

            migrationBuilder.DropTable(
                name: "AiAssistantThreads");

            migrationBuilder.AlterColumn<string>(
                name: "AllowedModelsCsv",
                table: "AiProviders",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4096)",
                oldMaxLength: 4096);
        }
    }
}
