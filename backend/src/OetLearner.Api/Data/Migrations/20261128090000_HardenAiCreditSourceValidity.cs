using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations;

[DbContext(typeof(LearnerDbContext))]
[Migration("20261128090000_HardenAiCreditSourceValidity")]
public partial class HardenAiCreditSourceValidity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SourceReferenceId",
            table: "AiPackageCreditTransactions",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ValidFrom",
            table: "AiPackageCreditLots",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ValidFrom",
            table: "AiPackageCreditTransactions",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql(
            "UPDATE \"AiPackageCreditLots\" SET \"ValidFrom\" = \"CreatedAt\" WHERE \"ValidFrom\" IS NULL;");
        migrationBuilder.Sql(
            "UPDATE \"AiPackageCreditTransactions\" SET \"ValidFrom\" = \"CreatedAt\" WHERE \"ValidFrom\" IS NULL;");

        migrationBuilder.CreateIndex(
            name: "IX_AiPackageCreditTransactions_UserId_SourceReferenceId",
            table: "AiPackageCreditTransactions",
            columns: new[] { "UserId", "SourceReferenceId" },
            filter: "\"SourceReferenceId\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_AiPackageCreditTransactions_UserId_SourceReferenceId",
            table: "AiPackageCreditTransactions");

        migrationBuilder.DropColumn(
            name: "ValidFrom",
            table: "AiPackageCreditLots");

        migrationBuilder.DropColumn(
            name: "SourceReferenceId",
            table: "AiPackageCreditTransactions");

        migrationBuilder.DropColumn(
            name: "ValidFrom",
            table: "AiPackageCreditTransactions");
    }
}
