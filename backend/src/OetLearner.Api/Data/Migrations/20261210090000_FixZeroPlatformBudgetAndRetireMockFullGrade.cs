using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

/// <summary>
/// W3/W8 production repair: legacy <c>MonthlyBudgetUsd = 0</c> meant unlimited
/// and W3 fail-closed it into "deny every platform AI call". Apply the
/// owner-approved $50 UTC month cap. Deactivate retired <c>mock.full_grade</c>.
/// Snapshot untouched.
/// </summary>
[DbContext(typeof(LearnerDbContext))]
[Migration("20261210090000_FixZeroPlatformBudgetAndRetireMockFullGrade")]
public partial class FixZeroPlatformBudgetAndRetireMockFullGrade : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE "AiGlobalPolicies"
            SET "MonthlyBudgetUsd" = 50,
                "UpdatedAt" = NOW()
            WHERE "Id" = 'global'
              AND "MonthlyBudgetUsd" <= 0;

            UPDATE "AiFeaturePolicies"
            SET "IsActive" = FALSE
            WHERE "FeatureCode" = 'mock.full_grade';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE "AiFeaturePolicies"
            SET "IsActive" = TRUE
            WHERE "FeatureCode" = 'mock.full_grade';
            """);
    }
}
