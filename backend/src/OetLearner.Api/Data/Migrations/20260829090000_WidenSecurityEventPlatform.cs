using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>Preserves the full canonical Capacitor platform names in the
    /// partitioned SecurityEvents table (`capacitor-android` is 17 chars).</summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260829090000_WidenSecurityEventPlatform")]
    public partial class WidenSecurityEventPlatform : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_class
        WHERE relname = 'SecurityEvents'
          AND relnamespace = 'public'::regnamespace
    ) THEN
        ALTER TABLE public.""SecurityEvents""
            ALTER COLUMN ""Platform"" TYPE character varying(32);
    END IF;
END $$;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_class
        WHERE relname = 'SecurityEvents'
          AND relnamespace = 'public'::regnamespace
    ) THEN
        ALTER TABLE public.""SecurityEvents""
            ALTER COLUMN ""Platform"" TYPE character varying(16);
    END IF;
END $$;
");
        }
    }
}
