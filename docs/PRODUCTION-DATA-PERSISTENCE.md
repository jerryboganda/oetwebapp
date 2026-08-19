# Production data persistence

**Law:** papers, media, users, and backups live in **named Docker volumes**.
They are independent of web/API containers. Rebuilding or recreating
containers does **not** delete them.

Inspected live on `root@185.252.233.186` 2026-08-20 after deploy `fdcc4776`
(API blue recreated 20:41 UTC). Postgres container was **not** recreated.
80 published Reading papers still present.

## Live volumes (project `oetwebsite`)

| Volume | Mount | Holds |
| --- | --- | --- |
| `oetwebsite_oet_postgres_data` | `oet-postgres:/var/lib/postgresql/data` | PostgreSQL (papers, keys, users, attempts) |
| `oetwebsite_oet_learner_storage` | `oet-api-*:/var/opt/oet-learner/storage` | Reading/Listening PDFs and media |
| `oetwebsite_oet_db_backups` | `db-backup:/backups` | `pg_dump` backups |
| `oetwebsite_oet_clamav_data` | clamav signatures | not learner data |

Created 2026-06-03. Compose pins them `external: true` with these exact names
so a deploy cannot attach a new empty volume.

`Storage__LocalRootPath` is always `/var/opt/oet-learner/storage`.

## What a deploy does

`scripts/deploy/auto-deploy-ghcr.sh` only:

1. Pulls GHCR images
2. `compose up --force-recreate` of **web-$slot**, **learner-api-$slot**, **db-backup**
3. Health-gates, then flips the routers

It does **not** recreate `postgres`. It does **not** pass `-v`. It does **not**
`docker volume rm`, `volume prune`, or `system prune`.

`down -v` in `qa-smoke.yml` is **desktop CI only**, never production.

Host wrapper `/usr/local/bin/docker` (installed by
`scripts/deploy/protect-production-data.sh` on every deploy) **blocks**:

- `docker volume rm` of any `oetwebsite_oet_*` volume
- `docker volume prune`
- `docker system prune --volumes`
- `docker compose down -v` / `--volumes`

Live papers, media, users, and attempts are **not** removed by rebuilds.
The only intended way to remove application content is the **admin panel**.

## Forbidden

- `docker compose down -v`
- `docker volume rm` / `volume prune` of the four `oetwebsite_oet_*` volumes
- Recreating the postgres or storage volumes
- Changing `name: oetwebsite` (that would prefix new empty volumes)

If a volume is missing, **stop**. Create it by hand with the exact name above.
Do not let Compose invent a parallel `oetwebapp_*` volume.
