using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OetWithDrHesham.Api.Data;

#nullable disable

namespace OetWithDrHesham.Api.Data.Migrations
{
    [DbContext(typeof(LearnerDbContext))]
    [Migration("20260614100000_BackfillReadingOptionIds")]
    public partial class BackfillReadingOptionIds : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data migration — enriches existing OptionsJson rows with stable IDs.
            // Runs as raw SQL that invokes a PL/pgSQL DO block for backfill.
            // This is safe because:
            //  - It only modifies rows where OptionsJson is a non-empty array
            //  - It preserves existing option text/values
            //  - It generates deterministic IDs from (questionId, index)
            //
            // For Postgres, we use a DO block that iterates over ReadingQuestions
            // and injects `id` and `letter` keys into each option object.
            migrationBuilder.Sql(@"
DO $$
DECLARE
    rec RECORD;
    opts jsonb;
    new_opts jsonb;
    opt jsonb;
    idx int;
    letters text[] := ARRAY['A','B','C','D','E','F'];
    option_id text;
    hash_input text;
BEGIN
    FOR rec IN
        SELECT ""Id"", ""OptionsJson""
        FROM ""ReadingQuestions""
        WHERE ""OptionsJson"" IS NOT NULL
          AND ""OptionsJson"" != '[]'
          AND ""OptionsJson"" != ''
          AND jsonb_typeof(""OptionsJson""::jsonb) = 'array'
    LOOP
        BEGIN
            opts := rec.""OptionsJson""::jsonb;
            new_opts := '[]'::jsonb;
            idx := 0;

            FOR opt IN SELECT * FROM jsonb_array_elements(opts)
            LOOP
                hash_input := rec.""Id"" || ':' || idx::text;
                option_id := 'opt-' || left(encode(sha256(convert_to(hash_input, 'UTF8')), 'hex'), 12);

                IF jsonb_typeof(opt) = 'string' THEN
                    -- Plain string → convert to object
                    new_opts := new_opts || jsonb_build_array(
                        jsonb_build_object(
                            'id', option_id,
                            'text', opt #>> '{}',
                            'letter', letters[idx + 1]
                        )
                    );
                ELSIF jsonb_typeof(opt) = 'object' THEN
                    -- Object without id → inject id and letter
                    IF NOT (opt ? 'id') THEN
                        opt := opt || jsonb_build_object('id', option_id);
                    END IF;
                    IF NOT (opt ? 'letter') THEN
                        opt := opt || jsonb_build_object('letter', letters[idx + 1]);
                    END IF;
                    new_opts := new_opts || jsonb_build_array(opt);
                ELSE
                    -- Unknown shape, keep as-is
                    new_opts := new_opts || jsonb_build_array(opt);
                END IF;

                idx := idx + 1;
            END LOOP;

            UPDATE ""ReadingQuestions""
            SET ""OptionsJson"" = new_opts::text,
                ""UpdatedAt"" = now()
            WHERE ""Id"" = rec.""Id"";

        EXCEPTION WHEN OTHERS THEN
            -- Skip malformed rows gracefully
            RAISE NOTICE 'Skipped question % due to: %', rec.""Id"", SQLERRM;
        END;
    END LOOP;
END $$;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reversing the backfill: strip `id` and `letter` keys, convert objects
            // back to plain strings where only `text` remains.
            migrationBuilder.Sql(@"
DO $$
DECLARE
    rec RECORD;
    opts jsonb;
    new_opts jsonb;
    opt jsonb;
BEGIN
    FOR rec IN
        SELECT ""Id"", ""OptionsJson""
        FROM ""ReadingQuestions""
        WHERE ""OptionsJson"" IS NOT NULL
          AND ""OptionsJson"" != '[]'
          AND ""OptionsJson"" != ''
          AND jsonb_typeof(""OptionsJson""::jsonb) = 'array'
    LOOP
        BEGIN
            opts := rec.""OptionsJson""::jsonb;
            new_opts := '[]'::jsonb;

            FOR opt IN SELECT * FROM jsonb_array_elements(opts)
            LOOP
                IF jsonb_typeof(opt) = 'object' AND (opt ? 'text') THEN
                    -- Convert back to plain string
                    new_opts := new_opts || jsonb_build_array(to_jsonb(opt ->> 'text'));
                ELSE
                    new_opts := new_opts || jsonb_build_array(opt);
                END IF;
            END LOOP;

            UPDATE ""ReadingQuestions""
            SET ""OptionsJson"" = new_opts::text,
                ""UpdatedAt"" = now()
            WHERE ""Id"" = rec.""Id"";

        EXCEPTION WHEN OTHERS THEN
            RAISE NOTICE 'Skipped question % due to: %', rec.""Id"", SQLERRM;
        END;
    END LOOP;
END $$;
");
        }
    }
}
