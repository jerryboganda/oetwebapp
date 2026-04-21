using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMockBundles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MockBundleId",
                table: "MockAttempts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MockType",
                table: "MockAttempts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "full");

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "MockAttempts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "exam");

            migrationBuilder.AddColumn<string>(
                name: "Profession",
                table: "MockAttempts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "medicine");

            migrationBuilder.AddColumn<int>(
                name: "ReservedReviewCredits",
                table: "MockAttempts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReviewSelection",
                table: "MockAttempts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "none");

            migrationBuilder.AddColumn<bool>(
                name: "StrictTimer",
                table: "MockAttempts",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "SubtestCode",
                table: "MockAttempts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MockBundles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExamFamilyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MockType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AppliesToAllProfessions = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    EstimatedDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    TagsCsv = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SourceProvenance = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedByAdminId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockBundles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MockBundleSections",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockBundleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SectionOrder = table.Column<int>(type: "integer", nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContentPaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TimeLimitMinutes = table.Column<int>(type: "integer", nullable: false),
                    ReviewEligible = table.Column<bool>(type: "boolean", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockBundleSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MockBundleSections_ContentPapers_ContentPaperId",
                        column: x => x.ContentPaperId,
                        principalTable: "ContentPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MockBundleSections_MockBundles_MockBundleId",
                        column: x => x.MockBundleId,
                        principalTable: "MockBundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MockReviewReservations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WalletId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ReservedCredits = table.Column<int>(type: "integer", nullable: false),
                    ConsumedCredits = table.Column<int>(type: "integer", nullable: false),
                    ReleasedCredits = table.Column<int>(type: "integer", nullable: false),
                    Selection = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DebitTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReleaseTransactionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockReviewReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MockReviewReservations_MockAttempts_MockAttemptId",
                        column: x => x.MockAttemptId,
                        principalTable: "MockAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MockSectionAttempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MockBundleSectionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ContentPaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LaunchRoute = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ContentAttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RawScore = table.Column<int>(type: "integer", nullable: true),
                    RawScoreMax = table.Column<int>(type: "integer", nullable: true),
                    ScaledScore = table.Column<int>(type: "integer", nullable: true),
                    Grade = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    FeedbackJson = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeadlineAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockSectionAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MockSectionAttempts_MockAttempts_MockAttemptId",
                        column: x => x.MockAttemptId,
                        principalTable: "MockAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MockSectionAttempts_MockBundleSections_MockBundleSectionId",
                        column: x => x.MockBundleSectionId,
                        principalTable: "MockBundleSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MockAttempts_MockBundleId",
                table: "MockAttempts",
                column: "MockBundleId");

            migrationBuilder.CreateIndex(
                name: "IX_MockAttempts_UserId_State",
                table: "MockAttempts",
                columns: new[] { "UserId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_MockBundles_ProfessionId_MockType",
                table: "MockBundles",
                columns: new[] { "ProfessionId", "MockType" });

            migrationBuilder.CreateIndex(
                name: "IX_MockBundles_Slug",
                table: "MockBundles",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MockBundles_Status_MockType",
                table: "MockBundles",
                columns: new[] { "Status", "MockType" });

            migrationBuilder.CreateIndex(
                name: "IX_MockBundleSections_ContentPaperId",
                table: "MockBundleSections",
                column: "ContentPaperId");

            migrationBuilder.CreateIndex(
                name: "IX_MockBundleSections_MockBundleId_SectionOrder",
                table: "MockBundleSections",
                columns: new[] { "MockBundleId", "SectionOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MockBundleSections_MockBundleId_SubtestCode",
                table: "MockBundleSections",
                columns: new[] { "MockBundleId", "SubtestCode" });

            migrationBuilder.CreateIndex(
                name: "IX_MockReviewReservations_MockAttemptId",
                table: "MockReviewReservations",
                column: "MockAttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MockReviewReservations_UserId_State",
                table: "MockReviewReservations",
                columns: new[] { "UserId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_MockSectionAttempts_ContentAttemptId",
                table: "MockSectionAttempts",
                column: "ContentAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_MockSectionAttempts_MockAttemptId_MockBundleSectionId",
                table: "MockSectionAttempts",
                columns: new[] { "MockAttemptId", "MockBundleSectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MockSectionAttempts_MockAttemptId_SubtestCode",
                table: "MockSectionAttempts",
                columns: new[] { "MockAttemptId", "SubtestCode" });

            migrationBuilder.CreateIndex(
                name: "IX_MockSectionAttempts_MockBundleSectionId",
                table: "MockSectionAttempts",
                column: "MockBundleSectionId");

            migrationBuilder.AddForeignKey(
                name: "FK_MockAttempts_MockBundles_MockBundleId",
                table: "MockAttempts",
                column: "MockBundleId",
                principalTable: "MockBundles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MockAttempts_MockBundles_MockBundleId",
                table: "MockAttempts");

            migrationBuilder.DropTable(name: "MockSectionAttempts");
            migrationBuilder.DropTable(name: "MockReviewReservations");
            migrationBuilder.DropTable(name: "MockBundleSections");
            migrationBuilder.DropTable(name: "MockBundles");

            migrationBuilder.DropIndex(
                name: "IX_MockAttempts_MockBundleId",
                table: "MockAttempts");

            migrationBuilder.DropIndex(
                name: "IX_MockAttempts_UserId_State",
                table: "MockAttempts");

            migrationBuilder.DropColumn(name: "MockBundleId", table: "MockAttempts");
            migrationBuilder.DropColumn(name: "MockType", table: "MockAttempts");
            migrationBuilder.DropColumn(name: "Mode", table: "MockAttempts");
            migrationBuilder.DropColumn(name: "Profession", table: "MockAttempts");
            migrationBuilder.DropColumn(name: "ReservedReviewCredits", table: "MockAttempts");
            migrationBuilder.DropColumn(name: "ReviewSelection", table: "MockAttempts");
            migrationBuilder.DropColumn(name: "StrictTimer", table: "MockAttempts");
            migrationBuilder.DropColumn(name: "SubtestCode", table: "MockAttempts");
        }
    }
}
