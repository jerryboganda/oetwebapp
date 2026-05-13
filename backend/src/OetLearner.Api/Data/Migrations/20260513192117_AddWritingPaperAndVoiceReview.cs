using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWritingPaperAndVoiceReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReviewVoiceNotes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReviewRequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UploadedByReviewerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    TranscriptText = table.Column<string>(type: "text", nullable: false),
                    WrittenNotes = table.Column<string>(type: "text", nullable: false),
                    RubricJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewVoiceNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewVoiceNotes_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReviewVoiceNotes_ReviewRequests_ReviewRequestId",
                        column: x => x.ReviewRequestId,
                        principalTable: "ReviewRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WritingAttemptAssets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttemptId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssetKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: false),
                    ExtractionState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExtractedText = table.Column<string>(type: "text", nullable: false),
                    ExtractionProvider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExtractionReasonCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ExtractionMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExtractedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WritingAttemptAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WritingAttemptAssets_Attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "Attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WritingAttemptAssets_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewVoiceNotes_MediaAssetId",
                table: "ReviewVoiceNotes",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewVoiceNotes_ReviewRequestId_CreatedAt",
                table: "ReviewVoiceNotes",
                columns: new[] { "ReviewRequestId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingAttemptAssets_AttemptId_PageNumber",
                table: "WritingAttemptAssets",
                columns: new[] { "AttemptId", "PageNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_WritingAttemptAssets_MediaAssetId",
                table: "WritingAttemptAssets",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_WritingAttemptAssets_UserId_CreatedAt",
                table: "WritingAttemptAssets",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReviewVoiceNotes");

            migrationBuilder.DropTable(
                name: "WritingAttemptAssets");
        }
    }
}
