# Hand-authored EF migrations only

Raw `dotnet ef migrations add` output is banned: it re-creates live tables and breaks deploy. Migrations are future-dated `YYYYMMDD090000_Name.cs` files with an inline `[Migration]` attribute; the ModelSnapshot is left alone.
