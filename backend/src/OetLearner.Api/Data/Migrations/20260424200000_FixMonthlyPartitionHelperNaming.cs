using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Replaces public.ensure_monthly_partition with a version that derives
    /// partition names from pg_class.relname instead of regclass::text. This
    /// avoids quoted parent names such as "AnalyticsEvents"_p2026_04 and keeps
    /// the helper idempotent after the partition-conversion migration.
    /// </summary>
    public partial class FixMonthlyPartitionHelperNaming : Migration
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
                SELECT n.nspname, c.relname, (c.relkind = 'p')
                  INTO parent_schema, parent_name, is_partitioned
                  FROM pg_class c
                  JOIN pg_namespace n ON n.oid = c.relnamespace
                 WHERE c.oid = parent;

                IF parent_name IS NULL THEN
                    RAISE NOTICE 'ensure_monthly_partition: % does not exist; skipping', parent;
                    RETURN;
                END IF;

                IF NOT COALESCE(is_partitioned, false) THEN
                    RAISE NOTICE 'ensure_monthly_partition: % is not partitioned; skipping', parent;
                    RETURN;
                END IF;

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
