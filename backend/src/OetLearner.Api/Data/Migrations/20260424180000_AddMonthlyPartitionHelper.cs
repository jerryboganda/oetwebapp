using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Installs the pl/pgsql helper <c>public.ensure_monthly_partition(parent regclass, part_col text, target_month date)</c>.
    ///
    /// <para>Given a <em>range-partitioned</em> parent table and a target month,
    /// the function idempotently creates the month's partition if one does
    /// not already exist. If the parent is <em>not</em> partitioned (the
    /// common case until ops run a dedicated conversion during a maintenance
    /// window), the function returns a warning and does nothing, making it
    /// safe to call from a scheduler.</para>
    ///
    /// <para>This migration does NOT convert any existing table to a partitioned
    /// layout — that requires a table rewrite and a maintenance window. Once
    /// ops convert a table (e.g. AnalyticsEvents, AuditEvents, AiUsageRecords),
    /// <see cref="OetLearner.Api.Services.PartitionMaintenanceWorker"/> will
    /// automatically keep next-month partitions rolling.</para>
    /// </summary>
    public partial class AddMonthlyPartitionHelper : Migration
    {
        private const string HelperFunctionSql = @"
            CREATE OR REPLACE FUNCTION public.ensure_monthly_partition(
                parent regclass,
                part_col text,
                target_month date)
            RETURNS void
            LANGUAGE plpgsql
            AS $$
            DECLARE
                part_name text;
                range_start date := date_trunc('month', target_month)::date;
                range_end   date := (date_trunc('month', target_month) + interval '1 month')::date;
                is_partitioned boolean;
            BEGIN
                -- Parent partitioning check (only range-partitioned parents are supported).
                SELECT (c.relkind = 'p')
                  INTO is_partitioned
                  FROM pg_class c
                 WHERE c.oid = parent;

                IF NOT COALESCE(is_partitioned, false) THEN
                    RAISE NOTICE 'ensure_monthly_partition: % is not partitioned; skipping', parent;
                    RETURN;
                END IF;

                part_name := format('%I_p%s',
                    split_part(parent::text, '.', coalesce(nullif(position('.' in parent::text), 0) + 1, 1) - 1 + 1),
                    to_char(range_start, 'YYYY_MM'));

                -- A simpler name for a schema-qualified parent: strip schema, prefix _p.
                part_name := format('%s_p%s',
                    regexp_replace(parent::text, '^.*\.', ''),
                    to_char(range_start, 'YYYY_MM'));

                IF NOT EXISTS (
                    SELECT 1 FROM pg_class WHERE relname = part_name
                ) THEN
                    EXECUTE format(
                        'CREATE TABLE IF NOT EXISTS public.%I PARTITION OF %s FOR VALUES FROM (%L) TO (%L)',
                        part_name, parent::text, range_start, range_end);
                    RAISE NOTICE 'ensure_monthly_partition: created %', part_name;
                END IF;
            END;
            $$;";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(HelperFunctionSql);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.ensure_monthly_partition(regclass, text, date);");
        }
    }
}
