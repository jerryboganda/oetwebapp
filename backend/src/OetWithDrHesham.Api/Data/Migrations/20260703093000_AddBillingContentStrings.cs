using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    /// <summary>
    /// Creates <c>BillingContentStrings</c>, the admin-editable key/value store for
    /// learner billing-page copy (titles, badges, button labels, section intros). Rows
    /// are OVERRIDES only — the canonical defaults live in <c>lib/billing-copy-defaults.ts</c>
    /// and the learner page falls back to them, so the table starts empty and nothing
    /// renders blank. Hand-written idempotent SQL (house style).
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260703093000_AddBillingContentStrings")]
    public partial class AddBillingContentStrings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""BillingContentStrings"" (
    ""Key"" character varying(128) NOT NULL,
    ""Section"" character varying(64) NOT NULL DEFAULT '',
    ""Value"" character varying(4000) NOT NULL DEFAULT '',
    ""Description"" character varying(256),
    ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
    ""UpdatedByAdminId"" character varying(64),
    ""UpdatedByAdminName"" character varying(128),
    CONSTRAINT ""PK_BillingContentStrings"" PRIMARY KEY (""Key"")
);
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""BillingContentStrings"";");
        }
    }
}
