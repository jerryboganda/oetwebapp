using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260809090000_ReplaceTelegramDeliveryWithWhatsApp")]
    public partial class ReplaceTelegramDeliveryWithWhatsApp : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AlignTable(migrationBuilder, "BillingPlans", updateTimestamp: true);
            AlignTable(migrationBuilder, "BillingPlanVersions", updateTimestamp: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward-only candidate communication policy. Do not restore the previous channel.
        }

        private static void AlignTable(
            MigrationBuilder migrationBuilder,
            string table,
            bool updateTimestamp)
        {
            var timestamp = updateTimestamp ? @", ""UpdatedAt"" = now()" : string.Empty;
            migrationBuilder.Sql($@"
UPDATE ""{table}""
SET ""DeliveryMethod"" = 'whatsapp',
    ""TelegramInviteUrl"" = 'https://wa.me/447961725989'{timestamp}
WHERE ""DeliveryMethod"" = 'telegram';

UPDATE ""{table}""
SET ""TelegramInviteUrl"" = 'https://wa.me/447961725989'{timestamp}
WHERE ""TelegramInviteUrl"" IS NOT NULL
  AND ""TelegramInviteUrl"" ILIKE '%t.me/%';
");
        }
    }
}
