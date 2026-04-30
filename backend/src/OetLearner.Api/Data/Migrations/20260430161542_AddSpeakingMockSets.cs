using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSpeakingMockSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxSpeakingMockSets",
                table: "FreeTierConfigs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "SpeakingMockSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockSetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Attempt1Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Attempt2Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReadinessBandSnapshot = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CombinedScaledSnapshot = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingMockSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingMockSets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RolePlay1ContentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RolePlay2ContentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Difficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CriteriaFocus = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Tags = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingMockSets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingMockSessions_MockSetId",
                table: "SpeakingMockSessions",
                column: "MockSetId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingMockSessions_UserId_StartedAt",
                table: "SpeakingMockSessions",
                columns: new[] { "UserId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingMockSets_Status_SortOrder",
                table: "SpeakingMockSets",
                columns: new[] { "Status", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpeakingMockSessions");

            migrationBuilder.DropTable(
                name: "SpeakingMockSets");

            migrationBuilder.DropColumn(
                name: "MaxSpeakingMockSets",
                table: "FreeTierConfigs");
        }
    }
}
