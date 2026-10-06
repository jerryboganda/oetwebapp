# Hand-authored EF migrations only

Raw `dotnet ef migrations add` output is banned: it re-creates live tables and breaks deploy. Migrations are future-dated `YYYYMMDD090000_Name.cs` files with an inline `[Migration]` attribute and no Designer file.

The ModelSnapshot is never regenerated, but a migration that adds or changes mapped entities must add the matching entries to it by hand, property for property as the model configures them (column type, `MaxLength`, required, key, index names). `dotnet ef migrations has-pending-model-changes` (`speaking-ci.yml` / `migrations-check`, which runs for any change under `Data/Migrations/**` or `LearnerDbContext.cs`) compares the model with the snapshot and fails on any difference. DDL that is not part of the model (CHECK constraints, foreign keys, partial indexes) stays out of the snapshot. Migrations that only touch tables or columns no entity maps leave it alone.

EF applies migrations in ID string order, so a new ID must sort after the current maximum in `backend/src/OetLearner.Api/Data/Migrations` (check `ls Data/Migrations | sort | tail`), or a fresh database (CI, new environment) applies it out of order. Use a unique 14-digit timestamp: bump the `HHMMSS` part (e.g. `090100`) instead of reusing one. Never rename or renumber an existing migration.
