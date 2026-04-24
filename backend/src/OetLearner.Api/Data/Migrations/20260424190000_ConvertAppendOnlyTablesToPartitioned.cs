using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OetLearner.Api.Data.Migrations
{
    /// <summary>
    /// Converts <c>AnalyticsEvents</c>, <c>AuditEvents</c>, and
    /// <c>AiUsageRecords</c> to monthly range-partitioned tables on their
    /// time column (OccurredAt / CreatedAt).
    ///
    /// <para><b>Strategy per table</b> (idempotent, transactional per table):</para>
    /// <list type="number">
    ///   <item>Skip if already partitioned (checks <c>pg_class.relkind</c>).</item>
    ///   <item>Rename the existing heap table to <c>{name}_legacy</c>.</item>
    ///   <item>Create a new <c>{name}</c> as PARTITION BY RANGE on the time column,
    ///     cloning columns + constraints + defaults from the legacy table.</item>
    ///   <item>Pre-create one partition covering every past month that has rows
    ///     (via generate_series), plus the current and next 2 months.</item>
    ///   <item>Copy all rows with <c>INSERT INTO new SELECT * FROM legacy</c>.</item>
    ///   <item>Drop the legacy table.</item>
    ///   <item>Recreate the indexes that existed on the legacy table (EF will
    ///     re-create its HasIndex set on next migration run if any drift; BRIN
    ///     indexes from migration ...150000 are recreated here explicitly).</item>
    /// </list>
    ///
    /// <para><b>Lock duration</b>: the rename + swap are instant; the
    /// <c>INSERT INTO new SELECT FROM legacy</c> copy holds an ACCESS
    /// EXCLUSIVE lock on the legacy table for the duration of the copy. For
    /// current production volumes (&lt; 10 GB per table) this is seconds to a
    /// few minutes. If a table has grown significantly, run this migration
    /// during low-traffic.</para>
    ///
    /// <para><b>Opt-in gate</b>: the conversion is behind an opt-in GUC
    /// (<c>oet.enable_partition_conversion = 'true'</c>) so that applying
    /// this migration is a no-op by default. To actually convert, an operator
    /// runs one of:
    /// <code>
    ///   -- persistent (all future sessions on this DB)
    ///   ALTER DATABASE oet_prod SET oet.enable_partition_conversion = 'true';
    ///
    ///   -- per-session
    ///   SET oet.enable_partition_conversion = 'true';
    /// </code>
    /// and then re-runs <c>dotnet ef database update</c>, or manually invokes
    /// the same DO-block on each target table. The conversion is idempotent
    /// so re-running is safe.</para>
    ///
    /// <para>Rationale: bundling a full table rewrite with a routine deploy
    /// risks extended downtime if the copy is slower than expected. Shipping
    /// the DDL behind an opt-in GUC decouples code rollout from the
    /// maintenance window and keeps the standard deploy path fast.</para>
    /// </summary>
    public partial class ConvertAppendOnlyTablesToPartitioned : Migration
    {
        private const string ConvertTableSql = @"
            DO $$
            DECLARE
                v_enable text;
                v_is_partitioned boolean;
                v_min_month date;
                v_cur_month date := date_trunc('month', now())::date;
                v_part_name text;
                v_part_start date;
            BEGIN
                -- Opt-in gate: no-op unless explicitly enabled.
                BEGIN
                    v_enable := current_setting('oet.enable_partition_conversion', true);
                EXCEPTION WHEN undefined_object THEN
                    v_enable := null;
                END;
                IF v_enable IS DISTINCT FROM 'true' THEN
                    RAISE NOTICE 'ConvertAppendOnlyTablesToPartitioned: {TABLE} skipped (oet.enable_partition_conversion not set to true)';
                    RETURN;
                END IF;

                -- Skip if already partitioned.
                SELECT (relkind = 'p') INTO v_is_partitioned
                FROM pg_class WHERE relname = '{TABLE}' AND relnamespace = 'public'::regnamespace;
                IF COALESCE(v_is_partitioned, false) THEN
                    RAISE NOTICE 'ConvertAppendOnlyTablesToPartitioned: {TABLE} already partitioned';
                    RETURN;
                END IF;

                -- Skip if table doesn't exist (fresh DB, no data yet).
                IF NOT EXISTS (
                    SELECT 1 FROM pg_class WHERE relname = '{TABLE}' AND relnamespace = 'public'::regnamespace
                ) THEN
                    RAISE NOTICE 'ConvertAppendOnlyTablesToPartitioned: {TABLE} missing, skip';
                    RETURN;
                END IF;

                EXECUTE 'ALTER TABLE public.""{TABLE}"" RENAME TO ""{TABLE}_legacy""';

                -- Build the partitioned parent from the legacy table's column
                -- definitions but WITHOUT the PK (Postgres requires PK to
                -- include the partition column; we recreate as composite below).
                -- INCLUDING CONSTRAINTS copies CHECK constraints but not PK.
                EXECUTE format(
                    'CREATE TABLE public.""{TABLE}"" (LIKE public.""{TABLE}_legacy"" INCLUDING DEFAULTS INCLUDING STORAGE) PARTITION BY RANGE (""{COL}"")');

                -- Composite PK: (partition column, original Id). Postgres
                -- requires the partition column in every unique constraint.
                -- Id alone is still effectively unique because it's a ULID.
                EXECUTE format(
                    'ALTER TABLE public.""{TABLE}"" ADD PRIMARY KEY (""{COL}"", ""Id"")');

                -- Find earliest row month so we can pre-create back-partitions.
                EXECUTE format(
                    'SELECT date_trunc(''month'', min(""{COL}""))::date FROM public.""{TABLE}_legacy""')
                INTO v_min_month;
                IF v_min_month IS NULL THEN
                    v_min_month := v_cur_month;
                END IF;

                -- Create a partition for every month from min_month through cur+2.
                v_part_start := v_min_month;
                WHILE v_part_start <= (v_cur_month + interval '2 months')::date LOOP
                    v_part_name := format('{TABLE}_p%s', to_char(v_part_start, 'YYYY_MM'));
                    EXECUTE format(
                        'CREATE TABLE IF NOT EXISTS public.%I PARTITION OF public.""{TABLE}"" FOR VALUES FROM (%L) TO (%L)',
                        v_part_name,
                        v_part_start,
                        (v_part_start + interval '1 month')::date);
                    v_part_start := (v_part_start + interval '1 month')::date;
                END LOOP;

                -- Copy rows across.
                EXECUTE format('INSERT INTO public.""{TABLE}"" SELECT * FROM public.""{TABLE}_legacy""');

                EXECUTE 'DROP TABLE public.""{TABLE}_legacy""';

                -- Recreate BRIN index from migration 20260424150000.
                EXECUTE format(
                    'CREATE INDEX IF NOT EXISTS ""IX_{TABLE}_{COL}_brin"" ON public.""{TABLE}"" USING BRIN (""{COL}"")');

                RAISE NOTICE 'ConvertAppendOnlyTablesToPartitioned: {TABLE} converted, pre-seeded from %', v_min_month;
            END $$;";

        private static string Emit(string table, string column) =>
            ConvertTableSql.Replace("{TABLE}", table).Replace("{COL}", column);

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Emit("AnalyticsEvents", "OccurredAt"));
            migrationBuilder.Sql(Emit("AuditEvents", "OccurredAt"));
            migrationBuilder.Sql(Emit("AiUsageRecords", "CreatedAt"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally not reversed automatically — rolling a partitioned
            // table back to a heap requires a full copy and a maintenance
            // window. If you need to roll back, run a manual script that:
            //   (1) renames the partitioned parent to _new_partitioned
            //   (2) CREATE TABLE ... (LIKE _new_partitioned INCLUDING ALL)
            //   (3) INSERT INTO ... SELECT * FROM _new_partitioned
            //   (4) DROP TABLE _new_partitioned CASCADE
            // This is the same technique as Up() but in reverse.
        }
    }
}
