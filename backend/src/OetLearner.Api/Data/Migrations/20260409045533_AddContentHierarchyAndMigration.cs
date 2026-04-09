using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContentHierarchyAndMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsVerified",
                table: "ContentContributors");

            migrationBuilder.RenameColumn(
                name: "ReviewNotesJson",
                table: "ContentSubmissions",
                newName: "ReviewNotes");

            migrationBuilder.RenameColumn(
                name: "ExamTypeCode",
                table: "ContentSubmissions",
                newName: "ExamFamilyCode");

            migrationBuilder.RenameColumn(
                name: "PublishedCount",
                table: "ContentContributors",
                newName: "SubmissionCount");

            migrationBuilder.RenameColumn(
                name: "AuthAccountId",
                table: "ContentContributors",
                newName: "UserId");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                table: "ContentSubmissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "ContentSubmissions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "ContentSubmissions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ContentSubmissions",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Difficulty",
                table: "ContentSubmissions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfessionId",
                table: "ContentSubmissions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "ContentSubmissions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "ContentSubmissions",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "ContentSubmissions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CanonicalSourcePath",
                table: "ContentItems",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CohortRelevance",
                table: "ContentItems",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentLanguage",
                table: "ContentItems",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DuplicateGroupId",
                table: "ContentItems",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FreshnessConfidence",
                table: "ContentItems",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ImportBatchId",
                table: "ContentItems",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstructionLanguage",
                table: "ContentItems",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsDiagnosticEligible",
                table: "ContentItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsMockEligible",
                table: "ContentItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPreviewEligible",
                table: "ContentItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MediaManifestJson",
                table: "ContentItems",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PackageEligibilityJson",
                table: "ContentItems",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProfessionIdsJson",
                table: "ContentItems",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "QualityScore",
                table: "ContentItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RightsStatus",
                table: "ContentItems",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceProvenance",
                table: "ContentItems",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SupersededById",
                table: "ContentItems",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovedCount",
                table: "ContentContributors",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "Rating",
                table: "ContentContributors",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "VerificationStatus",
                table: "ContentContributors",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ContentCohortOverlays",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProgramId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CohortCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CohortTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReleaseScheduleJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentCohortOverlays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentImportBatches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TotalItems = table.Column<int>(type: "integer", nullable: false),
                    ProcessedItems = table.Column<int>(type: "integer", nullable: false),
                    FailedItems = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorLogJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentLessons",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModuleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LessonType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentLessons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentModules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TrackId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    EstimatedDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    PrerequisiteModuleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentModules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentPackages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    PackageType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    InstructionLanguage = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    BillingPlanId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ComparisonFeaturesJson = table.Column<string>(type: "text", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    ExamFamilyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentPackages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentPrograms",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ProfessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    InstructionLanguage = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ProgramType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    EstimatedDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    ExamFamilyCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExamTypeCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentPrograms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentReferences",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModuleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReferenceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExternalUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentReferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentTracks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProgramId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubtestCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentTracks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FoundationResources",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContentBody = table.Column<string>(type: "text", nullable: true),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Difficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PrerequisiteResourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoundationResources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FreePreviewAssets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PreviewType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContentItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConversionCtaText = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TargetPackageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FreePreviewAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketingAssets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AssetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PackageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketingAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaAssets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OriginalFilename = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    StoragePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ThumbnailPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CaptionPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TranscriptPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PackageContentRules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PackageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RuleType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageContentRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TestimonialAssets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LearnerDisplayName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Profession = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    TestDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OverallGrade = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    SubtestGradesJson = table.Column<string>(type: "text", nullable: true),
                    TestimonialText = table.Column<string>(type: "text", nullable: true),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConsentStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DisplayApproved = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestimonialAssets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_DuplicateGroupId",
                table: "ContentItems",
                column: "DuplicateGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_ImportBatchId",
                table: "ContentItems",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_InstructionLanguage",
                table: "ContentItems",
                column: "InstructionLanguage");

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_IsPreviewEligible_Status",
                table: "ContentItems",
                columns: new[] { "IsPreviewEligible", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_SourceProvenance",
                table: "ContentItems",
                column: "SourceProvenance");

            migrationBuilder.CreateIndex(
                name: "IX_ContentCohortOverlays_ProgramId_CohortCode",
                table: "ContentCohortOverlays",
                columns: new[] { "ProgramId", "CohortCode" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentImportBatches_CreatedBy",
                table: "ContentImportBatches",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ContentImportBatches_Status_CreatedAt",
                table: "ContentImportBatches",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentLessons_ModuleId_DisplayOrder",
                table: "ContentLessons",
                columns: new[] { "ModuleId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentModules_TrackId_DisplayOrder",
                table: "ContentModules",
                columns: new[] { "TrackId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPackages_Code",
                table: "ContentPackages",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentPackages_Status_DisplayOrder",
                table: "ContentPackages",
                columns: new[] { "Status", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPrograms_Code",
                table: "ContentPrograms",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentPrograms_ProgramType_InstructionLanguage",
                table: "ContentPrograms",
                columns: new[] { "ProgramType", "InstructionLanguage" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPrograms_Status_DisplayOrder",
                table: "ContentPrograms",
                columns: new[] { "Status", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentReferences_ModuleId_DisplayOrder",
                table: "ContentReferences",
                columns: new[] { "ModuleId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentTracks_ProgramId_DisplayOrder",
                table: "ContentTracks",
                columns: new[] { "ProgramId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_FoundationResources_ResourceType_Status",
                table: "FoundationResources",
                columns: new[] { "ResourceType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FreePreviewAssets_Status_DisplayOrder",
                table: "FreePreviewAssets",
                columns: new[] { "Status", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketingAssets_AssetType_Status",
                table: "MarketingAssets",
                columns: new[] { "AssetType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_Status",
                table: "MediaAssets",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PackageContentRules_PackageId_RuleType",
                table: "PackageContentRules",
                columns: new[] { "PackageId", "RuleType" });

            migrationBuilder.CreateIndex(
                name: "IX_TestimonialAssets_DisplayApproved_DisplayOrder",
                table: "TestimonialAssets",
                columns: new[] { "DisplayApproved", "DisplayOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentCohortOverlays");

            migrationBuilder.DropTable(
                name: "ContentImportBatches");

            migrationBuilder.DropTable(
                name: "ContentLessons");

            migrationBuilder.DropTable(
                name: "ContentModules");

            migrationBuilder.DropTable(
                name: "ContentPackages");

            migrationBuilder.DropTable(
                name: "ContentPrograms");

            migrationBuilder.DropTable(
                name: "ContentReferences");

            migrationBuilder.DropTable(
                name: "ContentTracks");

            migrationBuilder.DropTable(
                name: "FoundationResources");

            migrationBuilder.DropTable(
                name: "FreePreviewAssets");

            migrationBuilder.DropTable(
                name: "MarketingAssets");

            migrationBuilder.DropTable(
                name: "MediaAssets");

            migrationBuilder.DropTable(
                name: "PackageContentRules");

            migrationBuilder.DropTable(
                name: "TestimonialAssets");

            migrationBuilder.DropIndex(
                name: "IX_ContentItems_DuplicateGroupId",
                table: "ContentItems");

            migrationBuilder.DropIndex(
                name: "IX_ContentItems_ImportBatchId",
                table: "ContentItems");

            migrationBuilder.DropIndex(
                name: "IX_ContentItems_InstructionLanguage",
                table: "ContentItems");

            migrationBuilder.DropIndex(
                name: "IX_ContentItems_IsPreviewEligible_Status",
                table: "ContentItems");

            migrationBuilder.DropIndex(
                name: "IX_ContentItems_SourceProvenance",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "ContentSubmissions");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "ContentSubmissions");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "ContentSubmissions");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "ContentSubmissions");

            migrationBuilder.DropColumn(
                name: "Difficulty",
                table: "ContentSubmissions");

            migrationBuilder.DropColumn(
                name: "ProfessionId",
                table: "ContentSubmissions");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "ContentSubmissions");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "ContentSubmissions");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "ContentSubmissions");

            migrationBuilder.DropColumn(
                name: "CanonicalSourcePath",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "CohortRelevance",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "ContentLanguage",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "DuplicateGroupId",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "FreshnessConfidence",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "InstructionLanguage",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "IsDiagnosticEligible",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "IsMockEligible",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "IsPreviewEligible",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "MediaManifestJson",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "PackageEligibilityJson",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "ProfessionIdsJson",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "QualityScore",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "RightsStatus",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "SourceProvenance",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "SupersededById",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "ApprovedCount",
                table: "ContentContributors");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "ContentContributors");

            migrationBuilder.DropColumn(
                name: "VerificationStatus",
                table: "ContentContributors");

            migrationBuilder.RenameColumn(
                name: "ReviewNotes",
                table: "ContentSubmissions",
                newName: "ReviewNotesJson");

            migrationBuilder.RenameColumn(
                name: "ExamFamilyCode",
                table: "ContentSubmissions",
                newName: "ExamTypeCode");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "ContentContributors",
                newName: "AuthAccountId");

            migrationBuilder.RenameColumn(
                name: "SubmissionCount",
                table: "ContentContributors",
                newName: "PublishedCount");

            migrationBuilder.AddColumn<bool>(
                name: "IsVerified",
                table: "ContentContributors",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
