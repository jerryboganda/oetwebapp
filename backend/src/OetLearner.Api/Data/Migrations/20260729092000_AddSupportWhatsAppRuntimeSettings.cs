using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Access &amp; payment spec 2026-07-15 §1/§6.4 — adds the runtime-settings "support"
    /// section so the WhatsApp proof number and its message template are admin-editable
    /// instead of hard-coded in <c>lib/billing/whatsapp.ts</c>. Both columns are nullable;
    /// null falls back to the existing PLATFORM_WHATSAPP constant, so behaviour is
    /// unchanged until an admin writes a value. Hand-written idempotent SQL (house style,
    /// see 20260723090000); mirrors the boot-time self-heal DDL.
    ///
    /// The number is NOT a secret — it is printed next to every package — so it is stored
    /// plain rather than through the RuntimeSettings.Secret.v1 protector, and is seeded
    /// here with the current live value.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260729092000_AddSupportWhatsAppRuntimeSettings")]
    public partial class AddSupportWhatsAppRuntimeSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""SupportWhatsAppNumber"" character varying(32);
ALTER TABLE ""RuntimeSettings"" ADD COLUMN IF NOT EXISTS ""SupportWhatsAppProofTemplate"" character varying(1000);
");

            // The singleton is created lazily by the admin PUT endpoint, so it may not exist
            // yet. A row of all-null overrides is equivalent to no row at all (the provider
            // substitutes `new RuntimeSettingsRow { Id = "default" }` when it finds none),
            // so materialising it here is safe and lets the seed land.
            migrationBuilder.Sql(@"
INSERT INTO ""RuntimeSettings"" (""Id"", ""UpdatedAt"") VALUES ('default', now())
ON CONFLICT (""Id"") DO NOTHING;
");

            // Seed only when unset — never clobber a number an admin has already entered.
            migrationBuilder.Sql(@"
UPDATE ""RuntimeSettings""
SET ""SupportWhatsAppNumber"" = '447961725989'
WHERE ""Id"" = 'default' AND ""SupportWhatsAppNumber"" IS NULL;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""SupportWhatsAppProofTemplate"";
ALTER TABLE ""RuntimeSettings"" DROP COLUMN IF EXISTS ""SupportWhatsAppNumber"";
");
        }
    }
}
