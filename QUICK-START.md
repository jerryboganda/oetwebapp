# OET Web App — Quick Start (Hybrid Dev)

**Architecture:** Database + .NET API run in **Podman**. The Next.js frontend runs **natively on Windows** for fast hot reload.

> Why hybrid? On this Windows host, Podman's VM disk I/O is slow — Next.js itself
> reports `Slow filesystem detected (~13s)` inside a container, so a containerized
> dev server can't hot-reload efficiently. Measured: containerized `/sign-in` =
> **240s+ timeout**; native = **31s cold, ~1s per edit**. DB + API are unaffected
> and run great in Podman. This matches the repo's intended dev model.

---

## 🚀 Start Everything (one command)

```powershell
.\start-dev.ps1
```

This starts the Podman containers (DB + API), waits for the API to be healthy, then launches the native frontend dev server.

### Or start manually:

```powershell
# 1. Database + API in Podman
podman compose -f docker-compose.hotreload.yml --env-file .env.docker-local up -d

# 2. Frontend natively (separate terminal)
pnpm run dev
```

## ✅ Access Points

| Service | URL | Runs in |
|---------|-----|---------|
| **Frontend** | http://localhost:3000 | Native (Windows) |
| **API** | http://localhost:8080 | Podman |
| **Swagger** | http://localhost:8080/swagger | Podman |
| **Database** | `localhost:5433` (user `oet_user`, db `oet_with_dr_hesham`) | Podman |

## 🔥 Hot Reload

- **Frontend** (native): edit anything under `app/`, `components/`, `lib/`, `contexts/`, `hooks/` → browser updates in **~1 second**. Measured: Next.js recompile **173ms**.
- **Backend** (Podman): edit `backend/src/**/*.cs` → `dotnet watch` rebuilds automatically (~5–10s).

## 📋 Common Commands

```powershell
# Container status / logs
podman compose -f docker-compose.hotreload.yml ps
podman compose -f docker-compose.hotreload.yml logs -f learner-api

# Stop containers (frontend: just Ctrl+C its terminal)
podman compose -f docker-compose.hotreload.yml down

# Restart API after env change in .env.docker-local
podman compose -f docker-compose.hotreload.yml --env-file .env.docker-local up -d --force-recreate learner-api

# Database shell
podman exec -it oet-hotreload-postgres psql -U oet_user -d oet_with_dr_hesham
```

## 🆘 Troubleshooting

**API returns nothing / unhealthy right after start:** it's compiling. First boot
takes a few minutes (Roslyn). Watch: `podman logs -f oet-hotreload-api` until you
see `Now listening on: http://[::]:8080`.

**Frontend can't reach API:** confirm `.env.local` has
`NEXT_PUBLIC_API_BASE_URL=http://localhost:8080` and the API is healthy
(`curl http://localhost:8080/health`).

**Port 5433 vs 5432:** OET Postgres is published on **5433** to avoid clashing with
another project already using 5432. Inside the compose network the API still uses
`postgres:5432`.

## 📦 What's where

- `docker-compose.hotreload.yml` — DB + API services (with the fixes that make them boot: minimal env, `--no-launch-profile` to bind `:8080`, bin/obj volume shadows)
- `.env.docker-local` — container env (DB creds, bootstrap admin)
- `.env.local` — native frontend env (`NEXT_PUBLIC_API_BASE_URL`)
- `start-dev.ps1` — one-command launcher
- `backend/Dockerfile.dev` — .NET dev image (`dotnet watch`)
- `Dockerfile.dev` — Next.js dev image (kept for reference; not used in hybrid)

---

**Default login (dev):** `expert@oet-with-dr-hesham.local` / `DevPassword123!` (from `.env.docker-local`).
