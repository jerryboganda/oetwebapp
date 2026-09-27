-- Owner Agent Console database role (owner directive 2026-09-27;
-- agent-console/CONTRACT.md §2). Creates or updates the LOGIN role
-- `oet_owner_agent` that the console reaches through oet-agent-dbproxy
-- (OET_AGENT_DATABASE_URL=postgres://oet_owner_agent:…@oet-agent-dbproxy:5432/<db>).
-- Idempotent: safe to re-run; re-running also rotates the password.
--
-- Privilege model
--   * LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS,
--     CONNECTION LIMIT 10 (the app's own pool always keeps headroom).
--   * Member of the application role (psql variable :app_role = POSTGRES_USER)
--     WITH INHERIT TRUE, SET FALSE, ADMIN FALSE: it uses the app role's object
--     privileges and ownership (full app DDL + DML) but can never
--     `SET ROLE` to it. Role ATTRIBUTES (SUPERUSER, CREATEROLE, …) are never
--     inherited through membership.
--   * Per-role defaults: log_statement = 'mod' (every data-modifying statement
--     lands in the oet-postgres log), idle_in_transaction_session_timeout and
--     lock_timeout so an agent session cannot wedge the app behind a lock queue
--     (a session may still SET its own values).
--   * CONNECT on the current database.
--   Requires PostgreSQL 16+ (GRANT … WITH INHERIT/SET). Production runs pg17.
--
-- RESIDUAL RISK (flagged to the owner): with the official postgres image,
-- POSTGRES_USER is the bootstrap SUPERUSER. Inheriting its privileges includes
-- ownership of superuser-owned objects (e.g. extension functions), which a
-- member can redefine (CREATE OR REPLACE … SECURITY DEFINER) to escalate. The
-- script emits a WARNING in that case; the structural fix is a dedicated
-- non-superuser owner role for the app schema.
--
-- Apply on the VPS as root. Values come from .env.production, nothing is
-- echoed, and the password never appears on a command line (docker exec -e
-- VAR copies it from the caller's environment):
--
--   ENV=/opt/oetwebapp/.env.production
--   envval() { grep -E "^$1=" "$ENV" | tail -n 1 | cut -d= -f2- | sed -e 's/^"//' -e 's/"$//' -e "s/^'//" -e "s/'\$//"; }
--   PGU="$(envval POSTGRES_USER)"; PGD="$(envval POSTGRES_DB)"
--   OWNER_AGENT_DBPASSWORD="$(envval OWNER_AGENT__DBPASSWORD)" \
--     docker exec -i -e OWNER_AGENT_DBPASSWORD oet-postgres \
--     psql -X -v ON_ERROR_STOP=1 -U "$PGU" -d "$PGD" -v app_role="$PGU" -f - \
--     < /path/to/create-owner-agent-db-role.sql
--
-- `-v agent_password=…` is also accepted (it takes precedence over the
-- environment) but puts the password into the process list; prefer the form above.

\set ON_ERROR_STOP on

-- Keep the password out of every log/statistics sink for THIS session only
-- (requires the superuser session this script needs anyway).
SET pg_stat_statements.track = 'none';
SET log_statement = 'none';
SET log_min_duration_statement = -1;
SET log_min_error_statement = panic;
SET log_error_verbosity = terse;

\if :{?app_role}
\else
  DO $$ BEGIN RAISE EXCEPTION 'psql variable app_role is required: -v app_role=<POSTGRES_USER>'; END $$;
\endif

\if :{?agent_password}
\else
  \getenv agent_password OWNER_AGENT_DBPASSWORD
\endif
\if :{?agent_password}
\else
  DO $$ BEGIN RAISE EXCEPTION 'agent password missing: docker exec -e OWNER_AGENT_DBPASSWORD (preferred) or -v agent_password=…'; END $$;
\endif

-- psql never interpolates :variables inside dollar-quoted DO bodies, so hand
-- them over as session-local custom settings. \gset keeps the result off stdout.
SELECT set_config('oet_owner_agent.app_role', :'app_role', false) AS oet_owner_agent_app_role_set \gset
SELECT set_config('oet_owner_agent.password', :'agent_password', false) AS oet_owner_agent_password_set \gset
\unset oet_owner_agent_password_set
SELECT current_database() AS oet_owner_agent_dbname \gset

BEGIN;

DO $$
DECLARE
  v_app_role text := current_setting('oet_owner_agent.app_role');
  v_password text := current_setting('oet_owner_agent.password');
  v_app_role_is_super boolean;
BEGIN
  IF v_app_role IS NULL OR v_app_role = '' THEN
    RAISE EXCEPTION 'app_role is empty';
  END IF;
  IF v_app_role = 'oet_owner_agent' THEN
    RAISE EXCEPTION 'app_role must be the application role (POSTGRES_USER), not oet_owner_agent';
  END IF;
  SELECT rolsuper INTO v_app_role_is_super FROM pg_roles WHERE rolname = v_app_role;
  IF NOT FOUND THEN
    RAISE EXCEPTION 'app role % does not exist', v_app_role;
  END IF;
  IF v_password IS NULL OR length(v_password) < 24 THEN
    RAISE EXCEPTION 'the oet_owner_agent password must be at least 24 characters';
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'oet_owner_agent') THEN
    CREATE ROLE oet_owner_agent LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS CONNECTION LIMIT 10;
    RAISE NOTICE 'created role oet_owner_agent';
  ELSE
    ALTER ROLE oet_owner_agent LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS CONNECTION LIMIT 10;
    RAISE NOTICE 'role oet_owner_agent already exists; attributes re-asserted';
  END IF;

  EXECUTE format('ALTER ROLE oet_owner_agent PASSWORD %L', v_password);

  IF v_app_role_is_super THEN
    RAISE WARNING 'app role % is a SUPERUSER: oet_owner_agent inherits ownership of superuser-owned objects (residual escalation risk, see header)', v_app_role;
  END IF;
END
$$;

GRANT :"app_role" TO oet_owner_agent WITH ADMIN FALSE, INHERIT TRUE, SET FALSE;

ALTER ROLE oet_owner_agent SET log_statement = 'mod';
ALTER ROLE oet_owner_agent SET idle_in_transaction_session_timeout = '10min';
ALTER ROLE oet_owner_agent SET lock_timeout = '15s';

GRANT CONNECT ON DATABASE :"oet_owner_agent_dbname" TO oet_owner_agent;

-- Objects the agent creates (ad-hoc DDL) are owned by oet_owner_agent; make them
-- usable by the application role too, so the app never hits "permission denied"
-- on a table the console created. Idempotent.
ALTER DEFAULT PRIVILEGES FOR ROLE oet_owner_agent GRANT ALL ON TABLES TO :"app_role";
ALTER DEFAULT PRIVILEGES FOR ROLE oet_owner_agent GRANT ALL ON SEQUENCES TO :"app_role";
ALTER DEFAULT PRIVILEGES FOR ROLE oet_owner_agent GRANT EXECUTE ON FUNCTIONS TO :"app_role";

COMMENT ON ROLE oet_owner_agent IS 'Owner Agent Console (agent-console/CONTRACT.md). Managed by scripts/ops/create-owner-agent-db-role.sql; never grant SET on the app role.';

COMMIT;

-- Clear the session-local copy of the password before printing the summary.
SELECT set_config('oet_owner_agent.password', '', false) AS oet_owner_agent_password_cleared \gset

-- Summary (no secrets).
SELECT r.rolname,
       r.rolsuper,
       r.rolcreatedb,
       r.rolcreaterole,
       r.rolconnlimit,
       r.rolconfig
FROM pg_roles r
WHERE r.rolname = 'oet_owner_agent';

SELECT m.roleid::regrole AS member_of,
       m.admin_option,
       m.inherit_option,
       m.set_option
FROM pg_auth_members m
WHERE m.member = 'oet_owner_agent'::regrole;
