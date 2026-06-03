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
            // ── WIP changes (idempotent via raw SQL) ─────────────────────────
            // These model changes were uncommitted in the working tree when
            // this migration was generated. The dev DB may already have them;
            // use IF NOT EXISTS / column-length guards so this is safe to run
            // either way.
            migrationBuilder.Sql(@"
DO $$
BEGIN
    -- LearnerXps → ReadingLearnerXps (only if the old name still exists)
    IF EXISTS (SELECT 1 FROM information_schema.tables
               WHERE table_schema = 'public' AND table_name = 'LearnerXps') THEN
        ALTER TABLE ""LearnerXps"" RENAME TO ""ReadingLearnerXps"";
        IF EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'IX_LearnerXps_UserId') THEN
            ALTER INDEX ""IX_LearnerXps_UserId"" RENAME TO ""IX_ReadingLearnerXps_UserId"";
        END IF;
    END IF;

    -- ReadingLearnerXps PK (only if no PK exists on the table yet)
    IF NOT EXISTS (
        SELECT 1 FROM pg_index pi
        JOIN pg_class pc ON pc.oid = pi.indrelid
        WHERE pc.relname = 'ReadingLearnerXps'
        AND pi.indisprimary = true
    ) THEN
        ALTER TABLE ""ReadingLearnerXps"" ADD CONSTRAINT ""PK_ReadingLearnerXps"" PRIMARY KEY (""Id"");
    END IF;

    -- RolePlayCards column widening
    BEGIN
        IF (SELECT character_maximum_length
            FROM information_schema.columns
            WHERE table_name = 'RolePlayCards' AND column_name = 'PatientEmotion') IS DISTINCT FROM 256 THEN
            ALTER TABLE ""RolePlayCards"" ALTER COLUMN ""PatientEmotion""  TYPE character varying(256);
            ALTER TABLE ""RolePlayCards"" ALTER COLUMN ""InterlocutorRole"" TYPE character varying(256);
            ALTER TABLE ""RolePlayCards"" ALTER COLUMN ""CommunicationGoal"" TYPE character varying(256);
            ALTER TABLE ""RolePlayCards"" ALTER COLUMN ""ClinicalTopic""  TYPE character varying(256);
            ALTER TABLE ""RolePlayCards"" ALTER COLUMN ""CandidateRole""  TYPE character varying(256);
        END IF;
    END;

    -- NOTE: an idempotent hot-patch block used to also create the
    -- ReadingQuestions.{BoxExplanationsJson,ReadingSectionId} columns,
    -- ReadingPaperAnnotations, ReadingSections (+ its FK/indexes) and
    -- SpeakingResultVisibilityConfigs here. Those objects are owned by the
    -- later canonical migrations (20260603_AddSpeakingResultVisibility,
    -- 20260620_AddReadingPaperAnnotations, 20260620_AddReadingSections,
    -- 20260621_AddReadingQuestionBoxExplanations). On a brand-new database this
    -- block created them BEFORE those migrations ran, so the (non-idempotent)
    -- canonical migrations then crashed with 'already exists' (e.g.
    -- FK_ReadingQuestions_ReadingSections_ReadingSectionId). Removed here so the
    -- canonical migrations create them. Prod is unaffected: AddMaterials was
    -- already applied before this drift was added, so AutoMigrate never runs
    -- this body again, and prod got the schema from the canonical migrations.
END
$$;
");

            // ── Materials tables (guaranteed new) ─────────────────────────────

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MaterialFiles");
            migrationBuilder.DropTable(name: "MaterialFolderAudiences");
            migrationBuilder.DropTable(name: "MaterialFolders");
        }
    }
}
