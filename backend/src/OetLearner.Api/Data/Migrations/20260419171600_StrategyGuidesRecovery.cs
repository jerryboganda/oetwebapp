using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Recovers the learner Strategies module by extending legacy guide rows
    /// with publish metadata and adding learner read/bookmark state.
    /// </remarks>
    public partial class StrategyGuidesRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "StrategyGuides",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentJson",
                table: "StrategyGuides",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPreviewEligible",
                table: "StrategyGuides",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ContentLessonId",
                table: "StrategyGuides",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceProvenance",
                table: "StrategyGuides",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RightsStatus",
                table: "StrategyGuides",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FreshnessConfidence",
                table: "StrategyGuides",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "StrategyGuides",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "StrategyGuides",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                table: "StrategyGuides",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LearnerStrategyProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StrategyGuideId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReadPercent = table.Column<int>(type: "integer", nullable: false),
                    Completed = table.Column<bool>(type: "boolean", nullable: false),
                    Bookmarked = table.Column<bool>(type: "boolean", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BookmarkedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerStrategyProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearnerStrategyProgress_StrategyGuides_StrategyGuideId",
                        column: x => x.StrategyGuideId,
                        principalTable: "StrategyGuides",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StrategyGuides_ContentLessonId",
                table: "StrategyGuides",
                column: "ContentLessonId");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyGuides_Slug",
                table: "StrategyGuides",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearnerStrategyProgress_StrategyGuideId",
                table: "LearnerStrategyProgress",
                column: "StrategyGuideId");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerStrategyProgress_UserId_StrategyGuideId",
                table: "LearnerStrategyProgress",
                columns: new[] { "UserId", "StrategyGuideId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "LearnerStrategyProgress");

            migrationBuilder.DropIndex(
                name: "IX_StrategyGuides_ContentLessonId",
                table: "StrategyGuides");

            migrationBuilder.DropIndex(
                name: "IX_StrategyGuides_Slug",
                table: "StrategyGuides");

            migrationBuilder.DropColumn(name: "ArchivedAt", table: "StrategyGuides");
            migrationBuilder.DropColumn(name: "ContentJson", table: "StrategyGuides");
            migrationBuilder.DropColumn(name: "ContentLessonId", table: "StrategyGuides");
            migrationBuilder.DropColumn(name: "CreatedAt", table: "StrategyGuides");
            migrationBuilder.DropColumn(name: "FreshnessConfidence", table: "StrategyGuides");
            migrationBuilder.DropColumn(name: "IsPreviewEligible", table: "StrategyGuides");
            migrationBuilder.DropColumn(name: "RightsStatus", table: "StrategyGuides");
            migrationBuilder.DropColumn(name: "Slug", table: "StrategyGuides");
            migrationBuilder.DropColumn(name: "SourceProvenance", table: "StrategyGuides");
            migrationBuilder.DropColumn(name: "UpdatedAt", table: "StrategyGuides");
        }
    }
}
