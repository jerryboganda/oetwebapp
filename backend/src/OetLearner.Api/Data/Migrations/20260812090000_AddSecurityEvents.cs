using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OetLearner.Api.Data;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Creates <c>SecurityEvents</c> — machine-generated security telemetry
    /// (Course Platform Security Requirements §4.4). Unlike the
    /// AnalyticsEvents/AuditEvents/AiUsageRecords conversion in migration
    /// 20260424190000, this is a brand-new table with no existing rows, so it
    /// is created range-partitioned by <c>OccurredAt</c> directly — no
    /// rename/copy/opt-in-GUC dance is needed. Composite PK
    /// (OccurredAt, Id) matches LearnerDbContext's fluent HasKey config
    /// (Postgres requires the partition column in every unique constraint).
    ///
    /// Pre-creates partitions for the current month plus the next 3 so writes
    /// immediately after deploy never hit a missing-partition error, even
    /// before <see cref="OetLearner.Api.Services.PartitionMaintenanceWorker"/>'s
    /// first sweep (+1 minute after startup). That worker's <c>Candidates</c>
    /// list should also be extended to include <c>SecurityEvents</c> so future
    /// months keep rolling automatically (done in Program.cs / the worker
    /// itself as part of the same change).
    /// </summary>
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260812090000_AddSecurityEvents")]
    public partial class AddSecurityEvents : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_class WHERE relname = 'SecurityEvents' AND relnamespace = 'public'::regnamespace
                    ) THEN
                        CREATE TABLE public.""SecurityEvents"" (
                            ""Id"" uuid NOT NULL,
                            ""OccurredAt"" timestamp with time zone NOT NULL,
                            ""AuthAccountId"" character varying(64) NULL,
                            ""Kind"" character varying(64) NOT NULL,
                            ""Severity"" character varying(16) NOT NULL,
                            ""IpAddress"" character varying(64) NULL,
                            ""CountryCode"" character varying(8) NULL,
                            ""UserAgent"" character varying(256) NULL,
                            ""Platform"" character varying(16) NULL,
                            ""SessionFamilyId"" uuid NULL,
                            ""DeviceId"" character varying(128) NULL,
                            ""DetailsJson"" text NULL,
                            CONSTRAINT ""PK_SecurityEvents"" PRIMARY KEY (""OccurredAt"", ""Id"")
                        ) PARTITION BY RANGE (""OccurredAt"");

                        CREATE INDEX ""IX_SecurityEvents_AuthAccountId_OccurredAt""
                            ON public.""SecurityEvents"" (""AuthAccountId"", ""OccurredAt"");
                        CREATE INDEX ""IX_SecurityEvents_Kind_OccurredAt""
                            ON public.""SecurityEvents"" (""Kind"", ""OccurredAt"");
                        CREATE INDEX ""IX_SecurityEvents_SessionFamilyId""
                            ON public.""SecurityEvents"" (""SessionFamilyId"");
                    END IF;
                END $$;

                DO $$
                DECLARE
                    v_cur date := date_trunc('month', now())::date;
                    v_month date;
                    v_part text;
                    i int;
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM pg_class
                        WHERE relname = 'SecurityEvents' AND relnamespace = 'public'::regnamespace AND relkind = 'p'
                    ) THEN
                        FOR i IN 0..3 LOOP
                            v_month := (v_cur + (i || ' months')::interval)::date;
                            v_part := format('SecurityEvents_p%s', to_char(v_month, 'YYYY_MM'));
                            EXECUTE format(
                                'CREATE TABLE IF NOT EXISTS public.%I PARTITION OF public.""SecurityEvents"" FOR VALUES FROM (%L) TO (%L)',
                                v_part, v_month, (v_month + interval '1 month')::date);
                        END LOOP;
                    END IF;
                END $$;
            ");

            migrationBuilder.AddColumn<int>(
                name: "DataRetentionSecurityEventsDays",
                table: "RuntimeSettings",
                type: "integer",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DataRetentionSecurityEventsDays",
                table: "RuntimeSettings");

            migrationBuilder.Sql(@"DROP TABLE IF EXISTS public.""SecurityEvents"" CASCADE;");
        }
    }
}
