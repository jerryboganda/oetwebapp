using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Widens <c>AiProviders.AllowedModelsCsv</c> from <c>varchar(1024)</c> to
    /// <c>varchar(4096)</c> so providers exposing many models — e.g. DigitalOcean
    /// Serverless Inference (~64 model IDs ≈ 1.3 KB) — can store a full
    /// allow-list in a single field without overflowing.
    ///
    /// Triggered by a production 500 (<c>22001 value too long for type character
    /// varying(1024)</c>) on PUT <c>/v1/admin/ai/providers/{id}</c> after the
    /// admin used the new "Discover models" button on DigitalOcean Serverless.
    ///
    /// Hand-written (no Designer.cs) per the repo's migration-drift policy in
    /// <c>memories/repo/migration-drift-note.md</c>.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260518200000_WidenAiProviderAllowedModelsCsv")]
    public partial class WidenAiProviderAllowedModelsCsv : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "AllowedModelsCsv",
                table: "AiProviders",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down would lose data if any row exceeds 1024 chars. Truncate first
            // then shrink. Idempotent.
            migrationBuilder.Sql("""
                UPDATE "AiProviders"
                SET "AllowedModelsCsv" = LEFT("AllowedModelsCsv", 1024)
                WHERE LENGTH("AllowedModelsCsv") > 1024;
                """);
            migrationBuilder.AlterColumn<string>(
                name: "AllowedModelsCsv",
                table: "AiProviders",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4096)",
                oldMaxLength: 4096);
        }
    }
}
