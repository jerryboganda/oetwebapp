using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [Migration("20260806090000_AddProfessionFirstContentScopes")]
    public partial class AddProfessionFirstContentScopes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProfessionId",
                table: "MaterialFolders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScopeKind",
                table: "MaterialFolders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            if (ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql("""
                    WITH RECURSIVE ancestry AS (
                        SELECT f."Id" AS "FolderId", f."Id" AS "AncestorId", f."ParentFolderId",
                               lower(trim(f."Name")) AS "AncestorName", lower(trim(coalesce(f."SubtestCode", ''))) AS "AncestorSubtest"
                        FROM "MaterialFolders" f
                        UNION ALL
                        SELECT a."FolderId", p."Id", p."ParentFolderId",
                               lower(trim(p."Name")), lower(trim(coalesce(p."SubtestCode", '')))
                        FROM ancestry a
                        JOIN "MaterialFolders" p ON p."Id" = a."ParentFolderId"
                    ), resolved AS (
                        SELECT "FolderId",
                            bool_or("AncestorName" IN ('basic english course','basic english','general english','academic / general english')) AS "IsGeneralEnglish",
                            max(CASE WHEN "AncestorSubtest" IN ('listening','reading','writing','speaking') THEN "AncestorSubtest"
                                     WHEN "AncestorName" IN ('listening','reading','writing','speaking') THEN "AncestorName" END) AS "Subtest",
                            max(CASE WHEN "AncestorName" = 'medicine' THEN 'medicine'
                                     WHEN "AncestorName" = 'nursing' THEN 'nursing'
                                     WHEN "AncestorName" = 'pharmacy' THEN 'pharmacy'
                                     WHEN "AncestorName" = 'physiotherapy' THEN 'physiotherapy'
                                     WHEN "AncestorName" = 'dentistry' THEN 'dentistry'
                                     WHEN "AncestorName" = 'radiography' THEN 'radiography' END) AS "ProfessionId"
                        FROM ancestry GROUP BY "FolderId"
                    )
                    UPDATE "MaterialFolders" f
                    SET "SubtestCode" = coalesce(nullif(lower(trim(f."SubtestCode")), ''), r."Subtest"),
                        "ScopeKind" = CASE
                            WHEN r."IsGeneralEnglish" THEN 'general_english'
                            WHEN r."Subtest" IN ('listening','reading') THEN 'shared'
                            WHEN r."ProfessionId" IS NOT NULL THEN 'profession'
                            ELSE f."ScopeKind" END,
                        "ProfessionId" = CASE
                            WHEN r."IsGeneralEnglish" OR r."Subtest" IN ('listening','reading') THEN NULL
                            ELSE r."ProfessionId" END
                    FROM resolved r WHERE r."FolderId" = f."Id";
                    """);

                // Correct the retired Dentistry/Radiography Arabic Writing/Speaking alias.
                // IDs, Bunny ids, categories, progress, and assets are untouched.
                migrationBuilder.Sql("""
                    UPDATE "LibraryVideos"
                    SET "ProfessionIdsJson" = '["medicine","physiotherapy"]',
                        "UpdatedAt" = CURRENT_TIMESTAMP
                    WHERE lower(coalesce("Language", '')) = 'ar'
                      AND lower(coalesce("SubtestCode", '')) IN ('writing','speaking')
                      AND "ProfessionIdsJson"::jsonb ? 'medicine'
                      AND ("ProfessionIdsJson"::jsonb ? 'dentistry' OR "ProfessionIdsJson"::jsonb ? 'radiography');
                    """);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ProfessionId", table: "MaterialFolders");
            migrationBuilder.DropColumn(name: "ScopeKind", table: "MaterialFolders");
        }
    }
}
