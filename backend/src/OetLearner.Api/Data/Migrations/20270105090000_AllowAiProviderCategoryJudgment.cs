using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// TypeSafe Jev provider row: <see cref="OetLearner.Api.Domain.AiProviderCategory.Judgment"/>
    /// (= 6) is a new <c>AiProviders.Category</c> value. The column is a plain
    /// <c>integer</c> and the model snapshot does not enumerate enum members,
    /// so there is NO model change; but
    /// <c>20260510194500_WidenAiProviderCategoryConstraint</c> pinned a Postgres
    /// CHECK to (0..5), which would reject the <c>typesafe-jev</c> seed row and
    /// any admin save of a Judgment provider. This widens the CHECK to include 6.
    /// Data-neutral: no row is read, written or deleted.
    ///
    /// HAND-AUTHORED (repo convention): inline <c>[Migration]</c>/<c>[DbContext]</c>,
    /// no Designer file, snapshot untouched. Down re-narrows the CHECK and will
    /// fail while any Judgment row exists (deactivate/delete it first).
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20270105090000_AllowAiProviderCategoryJudgment")]
    public partial class AllowAiProviderCategoryJudgment : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"AiProviders\" DROP CONSTRAINT IF EXISTS \"CK_AiProviders_Category\";");

            migrationBuilder.Sql(
                "ALTER TABLE \"AiProviders\" " +
                "ADD CONSTRAINT \"CK_AiProviders_Category\" " +
                "CHECK (\"Category\" IN (0, 1, 2, 3, 4, 5, 6));");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"AiProviders\" DROP CONSTRAINT IF EXISTS \"CK_AiProviders_Category\";");

            migrationBuilder.Sql(
                "ALTER TABLE \"AiProviders\" " +
                "ADD CONSTRAINT \"CK_AiProviders_Category\" " +
                "CHECK (\"Category\" IN (0, 1, 2, 3, 4, 5));");
        }
    }
}
