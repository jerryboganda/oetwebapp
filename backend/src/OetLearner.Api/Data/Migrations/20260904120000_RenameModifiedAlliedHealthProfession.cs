using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// Candidate-facing brief 14 Aug 2026: the allied-health filter and signup
/// catalog must display "Modified Allied Health Profession" instead of the
/// earlier "Other Allied health profession" label. Ids stay canonical.
/// Data-only — snapshot is unaffected.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20260904120000_RenameModifiedAlliedHealthProfession")]
public partial class RenameModifiedAlliedHealthProfession : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
UPDATE ""Professions""
SET ""Label"" = 'Modified Allied Health Profession'
WHERE ""Id"" = 'other-allied-health';
");
        migrationBuilder.Sql(@"
UPDATE ""SignupProfessionCatalog""
SET ""Label"" = 'Modified Allied Health Profession'
WHERE ""Id"" = 'other-allied-health';
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
UPDATE ""Professions""
SET ""Label"" = 'Other Allied health profession'
WHERE ""Id"" = 'other-allied-health';
");
        migrationBuilder.Sql(@"
UPDATE ""SignupProfessionCatalog""
SET ""Label"" = 'Other Allied health profession'
WHERE ""Id"" = 'other-allied-health';
");
    }
}
