using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class HardenWalletTopUpTiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_WalletTopUpTierConfigs_Amount_Currency"
                ON "WalletTopUpTierConfigs" ("Amount", "Currency");
                """);

            migrationBuilder.Sql("""
                INSERT INTO "WalletTopUpTierConfigs"
                    ("Id", "Amount", "Credits", "Bonus", "Label", "IsPopular", "DisplayOrder", "IsActive", "Currency", "CreatedAt", "UpdatedAt", "CreatedBy", "UpdatedBy")
                SELECT * FROM (VALUES
                    ('11111111-1111-4111-8111-111111111110'::uuid, 10, 10, 0, 'Starter', false, 0, true, 'AUD', NOW(), NOW(), 'migration:20260504120000', 'migration:20260504120000'),
                    ('11111111-1111-4111-8111-111111111125'::uuid, 25, 28, 3, 'Standard', false, 1, true, 'AUD', NOW(), NOW(), 'migration:20260504120000', 'migration:20260504120000'),
                    ('11111111-1111-4111-8111-111111111150'::uuid, 50, 60, 10, 'Best value', true, 2, true, 'AUD', NOW(), NOW(), 'migration:20260504120000', 'migration:20260504120000'),
                    ('11111111-1111-4111-8111-111111111200'::uuid, 100, 130, 30, 'Power', false, 3, true, 'AUD', NOW(), NOW(), 'migration:20260504120000', 'migration:20260504120000')
                ) AS seed("Id", "Amount", "Credits", "Bonus", "Label", "IsPopular", "DisplayOrder", "IsActive", "Currency", "CreatedAt", "UpdatedAt", "CreatedBy", "UpdatedBy")
                WHERE NOT EXISTS (SELECT 1 FROM "WalletTopUpTierConfigs");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "WalletTopUpTierConfigs"
                WHERE "CreatedBy" = 'migration:20260504120000'
                  AND "UpdatedBy" = 'migration:20260504120000';
                """);
        }
    }
}
