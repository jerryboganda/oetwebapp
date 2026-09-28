# Hand-authored EF migrations only

Raw `dotnet ef migrations add` output is banned: it re-creates live tables and breaks deploy. Migrations are future-dated `YYYYMMDD090000_Name.cs` files with an inline `[Migration]` attribute; the ModelSnapshot is left alone.

EF applies migrations in ID string order, so a new ID must sort after the current maximum in `backend/src/OetLearner.Api/Data/Migrations` (check `ls Data/Migrations | sort | tail`), or a fresh database (CI, new environment) applies it out of order. Use a unique 14-digit timestamp: bump the `HHMMSS` part (e.g. `090100`) instead of reusing one. Never rename or renumber an existing migration.
