\pset pager off
\echo === dev users ===
select "Email", "Role" from "Users" order by "Email";
\echo === dev migrations count ===
select count(*) from "__EFMigrationsHistory";
\echo === dev last 3 migrations ===
select "MigrationId" from "__EFMigrationsHistory" order by "MigrationId" desc limit 3;
\echo === dev top content tables ===
select relname, n_live_tup from pg_stat_user_tables where n_live_tup > 0 order by n_live_tup desc limit 50;
