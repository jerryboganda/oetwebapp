using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Admin Documentation Center — schema for the evidence-grade platform
    /// documentation system (Master Dossier + 15 specialist reports + Evidence
    /// Annex), owner-only, versioned, PDF-exportable in Internal/External modes.
    ///
    /// HAND-AUTHORED (repo convention, see 20260729090000): inline
    /// <c>[Migration]</c>/<c>[DbContext]</c>, no Designer file, ModelSnapshot
    /// deliberately untouched.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270103090000_AddDocumentationCenter")]
    public partial class AddDocumentationCenter : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentationModules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentationModules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationModules_SortOrder",
                table: "DocumentationModules",
                column: "SortOrder");

            migrationBuilder.CreateTable(
                name: "DocumentationVersions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModuleId = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ContentJson = table.Column<string>(type: "jsonb", nullable: false),
                    SourceRepoCommitSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ApprovedByName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentationVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentationVersions_DocumentationModules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "DocumentationModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationVersions_ModuleId_IsCurrent",
                table: "DocumentationVersions",
                columns: new[] { "ModuleId", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationVersions_ModuleId_VersionNumber",
                table: "DocumentationVersions",
                columns: new[] { "ModuleId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "DocumentationEvidenceItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvidenceId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ModuleId = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    EvidenceType = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsInternalOnly = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentationEvidenceItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentationEvidenceItems_DocumentationModules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "DocumentationModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationEvidenceItems_EvidenceId",
                table: "DocumentationEvidenceItems",
                column: "EvidenceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationEvidenceItems_ModuleId",
                table: "DocumentationEvidenceItems",
                column: "ModuleId");

            migrationBuilder.CreateTable(
                name: "DocumentationExports",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExportType = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    ModuleId = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GeneratedByUserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GeneratedByName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Sha256Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MediaAssetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IncludedVersionIdsJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentationExports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationExports_GeneratedAt",
                table: "DocumentationExports",
                column: "GeneratedAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DocumentationExports");
            migrationBuilder.DropTable(name: "DocumentationEvidenceItems");
            migrationBuilder.DropTable(name: "DocumentationVersions");
            migrationBuilder.DropTable(name: "DocumentationModules");
        }
    }
}
