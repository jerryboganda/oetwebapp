using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Multiple Stripe account support (spec 2026-08 §8). Creates
    /// <c>StripeAccountProfiles</c> — admin-managed Stripe credentials with
    /// encrypted secret/webhook keys, a single default flag, and test-connection
    /// telemetry. The default active profile's keys take precedence over the
    /// legacy single RuntimeSettings Stripe key (see RuntimeSettingsProvider.Merge).
    ///
    /// HAND-AUTHORED (repo convention, see 20260729090000): `dotnet ef migrations
    /// add` diffs against the intentionally-stale snapshot and would re-emit
    /// unrelated shipped schema. The snapshot is left as-is.
    ///
    /// SAFETY: additive only — one brand-new table, idempotent CREATE IF NOT
    /// EXISTS. Old containers during blue/green rollover never read it.
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260901090000_AddStripeAccountProfiles")]
    public partial class AddStripeAccountProfiles : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS public.""StripeAccountProfiles"" (
    ""Id"" character varying(64) NOT NULL,
    ""Label"" character varying(128) NOT NULL,
    ""Mode"" character varying(8) NOT NULL DEFAULT 'test',
    ""PublishableKey"" character varying(256) NULL,
    ""SecretKeyEncrypted"" text NOT NULL,
    ""WebhookSecretEncrypted"" text NULL,
    ""StripeAccountId"" character varying(64) NULL,
    ""RoutingCountriesCsv"" character varying(256) NULL,
    ""IsActive"" boolean NOT NULL DEFAULT TRUE,
    ""IsDefault"" boolean NOT NULL DEFAULT FALSE,
    ""LastTestResult"" character varying(512) NULL,
    ""LastTestedAt"" timestamp with time zone NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL,
    ""UpdatedAt"" timestamp with time zone NOT NULL,
    CONSTRAINT ""PK_StripeAccountProfiles"" PRIMARY KEY (""Id"")
);

CREATE INDEX IF NOT EXISTS ""IX_StripeAccountProfiles_IsDefault""
    ON public.""StripeAccountProfiles"" (""IsDefault"");
CREATE INDEX IF NOT EXISTS ""IX_StripeAccountProfiles_IsActive""
    ON public.""StripeAccountProfiles"" (""IsActive"");
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS public.""StripeAccountProfiles"";");
        }
    }
}
