using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260620100000_AddReadingSections")]
    public partial class AddReadingSections : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""ReadingSections"" (
    ""Id"" character varying(64) NOT NULL,
    ""ReadingPartId"" character varying(64) NOT NULL,
    ""SectionCode"" integer NOT NULL,
    ""DisplayOrder"" integer NOT NULL,
    ""MaxRawScore"" integer NOT NULL,
    ""ContentPaperAssetId"" character varying(64) NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_ReadingSections"" PRIMARY KEY (""Id""),
    CONSTRAINT ""FK_ReadingSections_ReadingParts_ReadingPartId"" FOREIGN KEY (""ReadingPartId"") REFERENCES ""ReadingParts"" (""Id"") ON DELETE CASCADE,
    CONSTRAINT ""FK_ReadingSections_ContentPaperAssets_ContentPaperAssetId"" FOREIGN KEY (""ContentPaperAssetId"") REFERENCES ""ContentPaperAssets"" (""Id"") ON DELETE SET NULL
);
");
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ""UX_ReadingSection_Part_SectionCode"" ON ""ReadingSections"" (""ReadingPartId"", ""SectionCode"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_ReadingSections_ReadingPartId"" ON ""ReadingSections"" (""ReadingPartId"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_ReadingSections_ContentPaperAssetId"" ON ""ReadingSections"" (""ContentPaperAssetId"");");

            migrationBuilder.Sql(@"ALTER TABLE ""ReadingQuestions"" ADD COLUMN IF NOT EXISTS ""ReadingSectionId"" character varying(64) NULL;");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_ReadingQuestions_ReadingSectionId"" ON ""ReadingQuestions"" (""ReadingSectionId"");");
            migrationBuilder.Sql(@"ALTER TABLE ""ReadingQuestions"" ADD CONSTRAINT ""FK_ReadingQuestions_ReadingSections_ReadingSectionId"" FOREIGN KEY (""ReadingSectionId"") REFERENCES ""ReadingSections"" (""Id"") ON DELETE SET NULL;");

            migrationBuilder.Sql(@"
INSERT INTO ""ReadingSections"" (""Id"", ""ReadingPartId"", ""SectionCode"", ""DisplayOrder"", ""MaxRawScore"", ""ContentPaperAssetId"", ""CreatedAt"", ""UpdatedAt"")
SELECT md5(rp.""Id"" || ':B1'), rp.""Id"", 1, 1, 1, NULL, NOW(), NOW()
FROM ""ReadingParts"" rp
WHERE rp.""PartCode"" = 2
  AND NOT EXISTS (SELECT 1 FROM ""ReadingSections"" rs WHERE rs.""ReadingPartId"" = rp.""Id"" AND rs.""SectionCode"" = 1);
INSERT INTO ""ReadingSections"" (""Id"", ""ReadingPartId"", ""SectionCode"", ""DisplayOrder"", ""MaxRawScore"", ""ContentPaperAssetId"", ""CreatedAt"", ""UpdatedAt"")
SELECT md5(rp.""Id"" || ':B2'), rp.""Id"", 2, 2, 1, NULL, NOW(), NOW()
FROM ""ReadingParts"" rp
WHERE rp.""PartCode"" = 2
  AND NOT EXISTS (SELECT 1 FROM ""ReadingSections"" rs WHERE rs.""ReadingPartId"" = rp.""Id"" AND rs.""SectionCode"" = 2);
INSERT INTO ""ReadingSections"" (""Id"", ""ReadingPartId"", ""SectionCode"", ""DisplayOrder"", ""MaxRawScore"", ""ContentPaperAssetId"", ""CreatedAt"", ""UpdatedAt"")
SELECT md5(rp.""Id"" || ':B3'), rp.""Id"", 3, 3, 1, NULL, NOW(), NOW()
FROM ""ReadingParts"" rp
WHERE rp.""PartCode"" = 2
  AND NOT EXISTS (SELECT 1 FROM ""ReadingSections"" rs WHERE rs.""ReadingPartId"" = rp.""Id"" AND rs.""SectionCode"" = 3);
INSERT INTO ""ReadingSections"" (""Id"", ""ReadingPartId"", ""SectionCode"", ""DisplayOrder"", ""MaxRawScore"", ""ContentPaperAssetId"", ""CreatedAt"", ""UpdatedAt"")
SELECT md5(rp.""Id"" || ':B4'), rp.""Id"", 4, 4, 1, NULL, NOW(), NOW()
FROM ""ReadingParts"" rp
WHERE rp.""PartCode"" = 2
  AND NOT EXISTS (SELECT 1 FROM ""ReadingSections"" rs WHERE rs.""ReadingPartId"" = rp.""Id"" AND rs.""SectionCode"" = 4);
INSERT INTO ""ReadingSections"" (""Id"", ""ReadingPartId"", ""SectionCode"", ""DisplayOrder"", ""MaxRawScore"", ""ContentPaperAssetId"", ""CreatedAt"", ""UpdatedAt"")
SELECT md5(rp.""Id"" || ':B5'), rp.""Id"", 5, 5, 1, NULL, NOW(), NOW()
FROM ""ReadingParts"" rp
WHERE rp.""PartCode"" = 2
  AND NOT EXISTS (SELECT 1 FROM ""ReadingSections"" rs WHERE rs.""ReadingPartId"" = rp.""Id"" AND rs.""SectionCode"" = 5);
INSERT INTO ""ReadingSections"" (""Id"", ""ReadingPartId"", ""SectionCode"", ""DisplayOrder"", ""MaxRawScore"", ""ContentPaperAssetId"", ""CreatedAt"", ""UpdatedAt"")
SELECT md5(rp.""Id"" || ':B6'), rp.""Id"", 6, 6, 1, NULL, NOW(), NOW()
FROM ""ReadingParts"" rp
WHERE rp.""PartCode"" = 2
  AND NOT EXISTS (SELECT 1 FROM ""ReadingSections"" rs WHERE rs.""ReadingPartId"" = rp.""Id"" AND rs.""SectionCode"" = 6);
INSERT INTO ""ReadingSections"" (""Id"", ""ReadingPartId"", ""SectionCode"", ""DisplayOrder"", ""MaxRawScore"", ""ContentPaperAssetId"", ""CreatedAt"", ""UpdatedAt"")
SELECT md5(rp.""Id"" || ':C1'), rp.""Id"", 7, 1, 8, NULL, NOW(), NOW()
FROM ""ReadingParts"" rp
WHERE rp.""PartCode"" = 3
  AND NOT EXISTS (SELECT 1 FROM ""ReadingSections"" rs WHERE rs.""ReadingPartId"" = rp.""Id"" AND rs.""SectionCode"" = 7);
INSERT INTO ""ReadingSections"" (""Id"", ""ReadingPartId"", ""SectionCode"", ""DisplayOrder"", ""MaxRawScore"", ""ContentPaperAssetId"", ""CreatedAt"", ""UpdatedAt"")
SELECT md5(rp.""Id"" || ':C2'), rp.""Id"", 8, 2, 8, NULL, NOW(), NOW()
FROM ""ReadingParts"" rp
WHERE rp.""PartCode"" = 3
  AND NOT EXISTS (SELECT 1 FROM ""ReadingSections"" rs WHERE rs.""ReadingPartId"" = rp.""Id"" AND rs.""SectionCode"" = 8);
");

            migrationBuilder.Sql(@"
UPDATE ""ReadingQuestions"" rq
SET ""ReadingSectionId"" = rs.""Id""
FROM ""ReadingSections"" rs
JOIN ""ReadingParts"" rp ON rp.""Id"" = rs.""ReadingPartId""
WHERE rq.""ReadingPartId"" = rp.""Id""
  AND rq.""ReadingSectionId"" IS NULL
  AND ((rp.""PartCode"" = 2 AND rs.""SectionCode"" = rq.""DisplayOrder"")
    OR (rp.""PartCode"" = 3 AND rs.""SectionCode"" = CASE WHEN rq.""DisplayOrder"" <= 8 THEN 7 ELSE 8 END));
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE IF EXISTS ""ReadingQuestions"" DROP CONSTRAINT IF EXISTS ""FK_ReadingQuestions_ReadingSections_ReadingSectionId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_ReadingQuestions_ReadingSectionId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""ReadingQuestions"" DROP COLUMN IF EXISTS ""ReadingSectionId"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""ReadingSections"";");
        }
    }
}