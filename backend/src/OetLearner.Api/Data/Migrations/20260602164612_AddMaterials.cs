using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_LearnerXps",
                table: "LearnerXps");

            migrationBuilder.RenameTable(
                name: "LearnerXps",
                newName: "ReadingLearnerXps");

            migrationBuilder.RenameIndex(
                name: "IX_LearnerXps_UserId",
                table: "ReadingLearnerXps",
                newName: "IX_ReadingLearnerXps_UserId");

            migrationBuilder.AlterColumn<string>(
                name: "PatientEmotion",
                table: "RolePlayCards",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "InterlocutorRole",
                table: "RolePlayCards",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "CommunicationGoal",
                table: "RolePlayCards",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "ClinicalTopic",
                table: "RolePlayCards",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(96)",
                oldMaxLength: 96);

            migrationBuilder.AlterColumn<string>(
                name: "CandidateRole",
                table: "RolePlayCards",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AddColumn<string>(
                name: "BoxExplanationsJson",
                table: "ReadingQuestions",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReadingSectionId",
                table: "ReadingQuestions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReadingLearnerXps",
                table: "ReadingLearnerXps",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "MaterialFolders",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ParentFolderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AudienceMode = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialFolders_MaterialFolders_ParentFolderId",
                        column: x => x.ParentFolderId,
                        principalTable: "MaterialFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReadingPaperAnnotations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PaperId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentPaperAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    GeometryJson = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingPaperAnnotations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingPaperAnnotations_ContentPaperAssets_ContentPaperAsse~",
                        column: x => x.ContentPaperAssetId,
                        principalTable: "ContentPaperAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReadingPaperAnnotations_ContentPapers_PaperId",
                        column: x => x.PaperId,
                        principalTable: "ContentPapers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReadingSections",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReadingPartId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SectionCode = table.Column<int>(type: "integer", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    MaxRawScore = table.Column<int>(type: "integer", nullable: false),
                    ContentPaperAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingSections_ContentPaperAssets_ContentPaperAssetId",
                        column: x => x.ContentPaperAssetId,
                        principalTable: "ContentPaperAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ReadingSections_ReadingParts_ReadingPartId",
                        column: x => x.ReadingPartId,
                        principalTable: "ReadingParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpeakingResultVisibilityConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RolePlayCardId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ShowSubmissionReceived = table.Column<bool>(type: "boolean", nullable: false),
                    ShowAiEstimate = table.Column<bool>(type: "boolean", nullable: false),
                    ShowReadinessBand = table.Column<bool>(type: "boolean", nullable: false),
                    ShowTutorScore = table.Column<bool>(type: "boolean", nullable: false),
                    ShowFullCriteria = table.Column<bool>(type: "boolean", nullable: false),
                    ShowTranscript = table.Column<bool>(type: "boolean", nullable: false),
                    ShowTutorComments = table.Column<bool>(type: "boolean", nullable: false),
                    ShowRecommendedDrills = table.Column<bool>(type: "boolean", nullable: false),
                    AllowReattempt = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeakingResultVisibilityConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaterialFiles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FolderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialFiles_MaterialFolders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "MaterialFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialFiles_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaterialFolderAudiences",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FolderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialFolderAudiences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialFolderAudiences_MaterialFolders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "MaterialFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingQuestions_ReadingSectionId",
                table: "ReadingQuestions",
                column: "ReadingSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialFiles_FolderId_SortOrder",
                table: "MaterialFiles",
                columns: new[] { "FolderId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialFiles_MediaAssetId",
                table: "MaterialFiles",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialFiles_SubtestCode_Status",
                table: "MaterialFiles",
                columns: new[] { "SubtestCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialFolderAudiences_FolderId_TargetType_TargetId",
                table: "MaterialFolderAudiences",
                columns: new[] { "FolderId", "TargetType", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaterialFolders_ParentFolderId_SortOrder",
                table: "MaterialFolders",
                columns: new[] { "ParentFolderId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialFolders_Status_SortOrder",
                table: "MaterialFolders",
                columns: new[] { "Status", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingPaperAnnotations_ContentPaperAssetId",
                table: "ReadingPaperAnnotations",
                column: "ContentPaperAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingPaperAnnotations_PaperId",
                table: "ReadingPaperAnnotations",
                column: "PaperId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingPaperAnnotations_UserId_PaperId",
                table: "ReadingPaperAnnotations",
                columns: new[] { "UserId", "PaperId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingPaperAnnotations_UserId_PaperId_ContentPaperAssetId",
                table: "ReadingPaperAnnotations",
                columns: new[] { "UserId", "PaperId", "ContentPaperAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingSections_ContentPaperAssetId",
                table: "ReadingSections",
                column: "ContentPaperAssetId");

            migrationBuilder.CreateIndex(
                name: "UX_ReadingSection_Part_SectionCode",
                table: "ReadingSections",
                columns: new[] { "ReadingPartId", "SectionCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpeakingResultVisibilityConfigs_RolePlayCardId",
                table: "SpeakingResultVisibilityConfigs",
                column: "RolePlayCardId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReadingQuestions_ReadingSections_ReadingSectionId",
                table: "ReadingQuestions",
                column: "ReadingSectionId",
                principalTable: "ReadingSections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReadingQuestions_ReadingSections_ReadingSectionId",
                table: "ReadingQuestions");

            migrationBuilder.DropTable(
                name: "MaterialFiles");

            migrationBuilder.DropTable(
                name: "MaterialFolderAudiences");

            migrationBuilder.DropTable(
                name: "ReadingPaperAnnotations");

            migrationBuilder.DropTable(
                name: "ReadingSections");

            migrationBuilder.DropTable(
                name: "SpeakingResultVisibilityConfigs");

            migrationBuilder.DropTable(
                name: "MaterialFolders");

            migrationBuilder.DropIndex(
                name: "IX_ReadingQuestions_ReadingSectionId",
                table: "ReadingQuestions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ReadingLearnerXps",
                table: "ReadingLearnerXps");

            migrationBuilder.DropColumn(
                name: "BoxExplanationsJson",
                table: "ReadingQuestions");

            migrationBuilder.DropColumn(
                name: "ReadingSectionId",
                table: "ReadingQuestions");

            migrationBuilder.RenameTable(
                name: "ReadingLearnerXps",
                newName: "LearnerXps");

            migrationBuilder.RenameIndex(
                name: "IX_ReadingLearnerXps_UserId",
                table: "LearnerXps",
                newName: "IX_LearnerXps_UserId");

            migrationBuilder.AlterColumn<string>(
                name: "PatientEmotion",
                table: "RolePlayCards",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "InterlocutorRole",
                table: "RolePlayCards",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "CommunicationGoal",
                table: "RolePlayCards",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "ClinicalTopic",
                table: "RolePlayCards",
                type: "character varying(96)",
                maxLength: 96,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "CandidateRole",
                table: "RolePlayCards",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AddPrimaryKey(
                name: "PK_LearnerXps",
                table: "LearnerXps",
                column: "Id");
        }
    }
}
