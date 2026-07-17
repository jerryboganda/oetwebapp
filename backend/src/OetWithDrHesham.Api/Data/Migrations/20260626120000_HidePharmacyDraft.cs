using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260626120000_HidePharmacyDraft")]
    public partial class HidePharmacyDraft : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data migration — hides the provisional `full-pharmacy` SKU from the
            // public catalogue until it is activated (spec rule #9).
            //
            // The boot/admin reseeder is DISABLED by default in production, so the
            // JSON manifest flip (isDraft:true) alone will not update an existing
            // production database. This migration applies the same draft state that
            // the seeder would have written, scoped strictly to `full-pharmacy`.
            //
            // The public catalogue endpoint and AddonEligibilityService filter on
            // `Status == Active && IsVisible && !IsDraft`, so flipping IsDraft to
            // true removes the plan from both surfaces.
            //
            // Each UPDATE is idempotent and safe when the row is absent (it simply
            // affects 0 rows). Identifiers use Postgres double-quoted casing to
            // match the EF model mapping.
            //
            // Verified against LearnerDbContextModelSnapshot.cs + Oet2026CatalogSeeder.cs:
            //  - "BillingPlans"."IsDraft"        boolean
            //  - "BillingPlanVersions"."IsDraft" boolean   (keyed by "Code" = plan code)
            //  - "ContentPackages"."Status"      integer enum ContentStatus
            //                                    (Draft = 0, Published = 4)

            migrationBuilder.Sql(@"
UPDATE ""BillingPlans""
SET ""IsDraft"" = true
WHERE ""Code"" = 'full-pharmacy'
  AND ""IsDraft"" IS DISTINCT FROM true;
");

            migrationBuilder.Sql(@"
UPDATE ""BillingPlanVersions""
SET ""IsDraft"" = true
WHERE ""Code"" = 'full-pharmacy'
  AND ""IsDraft"" IS DISTINCT FROM true;
");

            // ContentStatus.Draft = 0 — matches what the seeder writes for a draft plan.
            migrationBuilder.Sql(@"
UPDATE ""ContentPackages""
SET ""Status"" = 0
WHERE ""Code"" = 'full-pharmacy'
  AND ""Status"" IS DISTINCT FROM 0;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse — re-publish the `full-pharmacy` SKU. Restores the previous
            // public-catalogue visibility (IsDraft = false) and the content package
            // to Published (ContentStatus.Published = 4), scoped to full-pharmacy.
            migrationBuilder.Sql(@"
UPDATE ""BillingPlans""
SET ""IsDraft"" = false
WHERE ""Code"" = 'full-pharmacy'
  AND ""IsDraft"" IS DISTINCT FROM false;
");

            migrationBuilder.Sql(@"
UPDATE ""BillingPlanVersions""
SET ""IsDraft"" = false
WHERE ""Code"" = 'full-pharmacy'
  AND ""IsDraft"" IS DISTINCT FROM false;
");

            // ContentStatus.Published = 4 — matches the seeder's published state.
            migrationBuilder.Sql(@"
UPDATE ""ContentPackages""
SET ""Status"" = 4
WHERE ""Code"" = 'full-pharmacy'
  AND ""Status"" IS DISTINCT FROM 4;
");
        }
    }
}
