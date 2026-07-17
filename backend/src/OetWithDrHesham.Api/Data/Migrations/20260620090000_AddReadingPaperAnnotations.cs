using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260620090000_AddReadingPaperAnnotations")]
    public partial class AddReadingPaperAnnotations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""ReadingPaperAnnotations"" (
    ""Id"" character varying(64) NOT NULL,
    ""UserId"" character varying(64) NOT NULL,
    ""PaperId"" character varying(64) NOT NULL,
    ""ContentPaperAssetId"" character varying(64) NOT NULL,
    ""PageNumber"" integer NOT NULL,
    ""Kind"" integer NOT NULL,
    ""GeometryJson"" character varying(8192) NOT NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_ReadingPaperAnnotations"" PRIMARY KEY (""Id""),
    CONSTRAINT ""FK_ReadingPaperAnnotations_ContentPapers_PaperId"" FOREIGN KEY (""PaperId"") REFERENCES ""ContentPapers"" (""Id"") ON DELETE CASCADE,
    CONSTRAINT ""FK_ReadingPaperAnnotations_ContentPaperAssets_ContentPaperAssetId"" FOREIGN KEY (""ContentPaperAssetId"") REFERENCES ""ContentPaperAssets"" (""Id"") ON DELETE CASCADE
);
");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_ReadingPaperAnnotations_UserId_PaperId"" ON ""ReadingPaperAnnotations"" (""UserId"", ""PaperId"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_ReadingPaperAnnotations_UserId_PaperId_ContentPaperAssetId"" ON ""ReadingPaperAnnotations"" (""UserId"", ""PaperId"", ""ContentPaperAssetId"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_ReadingPaperAnnotations_ContentPaperAssetId"" ON ""ReadingPaperAnnotations"" (""ContentPaperAssetId"");");
            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_ReadingPaperAnnotations_PaperId"" ON ""ReadingPaperAnnotations"" (""PaperId"");");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ReadingPaperAnnotations");
        }
    }
}
