using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
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
    /// <see cref="OetWithDrHesham.Api.Services.PartitionMaintenanceWorker"/> will
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
                parent_schema text;
                parent_name text;
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

                SELECT n.nspname, c.relname
                  INTO parent_schema, parent_name
                  FROM pg_class c
                  JOIN pg_namespace n ON n.oid = c.relnamespace
                 WHERE c.oid = parent;

                part_name := format('%s_p%s', parent_name, to_char(range_start, 'YYYY_MM'));

                IF NOT EXISTS (
                    SELECT 1
                      FROM pg_class c
                      JOIN pg_namespace n ON n.oid = c.relnamespace
                     WHERE n.nspname = parent_schema
                       AND c.relname = part_name
                ) THEN
                    EXECUTE format(
                        'CREATE TABLE IF NOT EXISTS %I.%I PARTITION OF %I.%I FOR VALUES FROM (%L) TO (%L)',
                        parent_schema, part_name, parent_schema, parent_name, range_start, range_end);
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
