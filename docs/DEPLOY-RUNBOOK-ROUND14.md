# Deploy Runbook — Round 14 (commit `c426423`)

> GitHub is up to date: `main @ c426423` (<https://github.com/jerryboganda/oetwebapp>).
> This runbook is what **you** run on the VPS. The agent does NOT execute these
> commands for you.

---

## 1. SSH into the VPS

```powershell
ssh root@185.252.233.186
```

---

## 2. Pull latest and rebuild

```bash
cd /root/oetwebsite
git fetch origin
git log --oneline -1               # sanity: confirm current HEAD
git reset --hard origin/main       # hard-sync to c426423
git log --oneline -1               # confirm: c426423 feat(perf): ship rounds 11-13 perf sweep

# Build + rollout (standalone Next.js + .NET API)
docker compose --env-file .env.production -f docker-compose.production.yml up -d --build

# Watch until healthy
docker compose -f docker-compose.production.yml ps
docker compose -f docker-compose.production.yml logs -f web api --tail=200
# Ctrl-C once you see: "Next.js ... ready in ..." and ".NET ... Now listening on: http://[::]:8080"
```

Expected healthy state:

```text
NAME              STATUS                   PORTS
oetwebsite-web    Up (healthy)             0.0.0.0:3000->3000/tcp
oetwebsite-api    Up (healthy)             0.0.0.0:8080->8080/tcp
oetwebsite-db     Up (healthy)             5432/tcp
```

---

## 3. Smoke check the public endpoints

```bash
# Frontend health (proxied through NPM)
curl -sS -o /dev/null -w "%{http_code}\n" https://app.oetwithdrhesham.co.uk/api/health
# -> 200

# API health (canonical endpoints — no `/v1` prefix)
curl -sS -o /dev/null -w "%{http_code}\n" https://api.oetwithdrhesham.co.uk/health
# -> 200
curl -sS -o /dev/null -w "%{http_code}\n" https://api.oetwithdrhesham.co.uk/health/ready
# -> 200

# Homepage
curl -sS -o /dev/null -w "%{http_code}\n" https://app.oetwithdrhesham.co.uk/
# -> 200 (or 307 to /sign-in — both OK)
```

If any of those are not `2xx`/`3xx`, jump to Rollback.

---

## 4. Rollback (only if needed)

Previous known-good commit: **`ca9b0a8`**.

```bash
cd /root/oetwebsite
git reset --hard ca9b0a8
docker compose --env-file .env.production -f docker-compose.production.yml up -d --build
```

**DO NOT** touch the postgres volume. The DB schema was not changed in this
release (perf / client-side only).

---

## 5. Verify in the browser (you, manually)

Do the manual QA pass in [MANUAL-QA-CHECKLIST-ROUND14.md](MANUAL-QA-CHECKLIST-ROUND14.md).

Then run the Playwright prod smoke (see
[PROD-SMOKE-RUNBOOK.md](PROD-SMOKE-RUNBOOK.md)).
