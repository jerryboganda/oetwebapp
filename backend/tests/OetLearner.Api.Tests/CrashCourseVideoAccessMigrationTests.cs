using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using OetLearner.Api.Data.Migrations;

namespace OetLearner.Api.Tests;

public sealed class CrashCourseVideoAccessMigrationTests
{
    [Fact]
    public void Up_ProtectsEveryFullCoursePlanTableWithoutChangingCrashCourseGrants()
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        new InspectableMigration().ApplyUp(builder);

        var sql = builder.Operations
            .OfType<SqlOperation>()
            .Select(operation => operation.Sql)
            .ToArray();
        var combinedSql = string.Join(Environment.NewLine, sql);
        var blockedIds = Regex.Matches(combinedSql, @"vid_[a-f0-9]{32}")
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, sql.Length);
        Assert.Contains(sql, statement => statement.Contains("UPDATE \"BillingPlans\"", StringComparison.Ordinal));
        Assert.Contains(sql, statement => statement.Contains("UPDATE \"BillingPlanVersions\"", StringComparison.Ordinal));
        Assert.All(sql, statement =>
        {
            Assert.Contains("\"Code\" LIKE 'full-%'", statement, StringComparison.Ordinal);
            Assert.Contains("{videos,include}", statement, StringComparison.Ordinal);
            Assert.Contains("{videos,exclude}", statement, StringComparison.Ordinal);
            Assert.Contains("jsonb_array_elements", statement, StringComparison.Ordinal);
            Assert.Contains("jsonb_set", statement, StringComparison.Ordinal);
        });
        Assert.Equal(18, blockedIds.Length);
    }

    private sealed class InspectableMigration : ApplyCrashCourseVideoExclusionsToFullCourses
    {
        public void ApplyUp(MigrationBuilder builder) => Up(builder);
    }
}
