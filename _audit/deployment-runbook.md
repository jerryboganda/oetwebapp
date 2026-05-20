# Deployment runbook — Phases 2-7

**Target**: `vps` (185.252.233.186) — `/root/oetwebsite`, `/opt/oetwebapp`
**Containers**: `oet-api` + `oet-api-green` (blue/green), `oet-web` + `oet-web-green`, `oet-postgres` (pg17), `oet-db-backup`, `oet-clamav`

---

## Pre-flight

```bash
# Confirm prod health
ssh vps "docker ps --filter 'name=oet-' --format 'table {{.Names}}\t{{.Status}}'"

# Take a DB snapshot before applying migrations
ssh vps "docker exec oet-postgres pg_dump -U postgres oet_learner | gzip > /opt/oetwebapp-backups/oet-learner-pre-phase-2-7-$(date -u +%Y%m%dT%H%M%SZ).sql.gz"
```

## Step 1 — Push to remote

```bash
# Local
git add -A backend/src/OetLearner.Api app/admin/content app/recalls/documents \
        lib/api.ts lib/admin-permissions.ts \
        _audit/ backend/tests/OetLearner.Api.Tests/ReadingAuthoringTests.cs
git status   # eyeball — make sure NO .secrets/ or backups/ slipped in
git commit -m "feat(content): Phase 2-7 admin UIs (recalls, scoring, rulebook PDFs, result templates, speaking shared, folder importer)"
git push origin cleanup/remove-demo-dummy-seed-placeholder-data
```

## Step 2 — Pull on VPS

```bash
ssh vps "cd /root/oetwebsite && git fetch && git checkout cleanup/remove-demo-dummy-seed-placeholder-data && git pull --ff-only"
```

## Step 3 — Rebuild containers (blue/green swap)

```bash
# Rebuild API + Web for the green slot (idle one)
ssh vps "cd /root/oetwebsite && docker compose -f docker-compose.production.yml build oet-api-green oet-web-green"

# Migrations: apply on the live oet-postgres
ssh vps "docker exec oet-api dotnet ef database update --no-build --context LearnerDbContext --project /app/OetLearner.Api"
# (If --no-build fails because the image is the old build: instead run the new green container with a one-shot migration:
ssh vps "docker compose -f docker-compose.production.yml run --rm oet-api-green dotnet ef database update --context LearnerDbContext"

# Then start green, drain blue
ssh vps "cd /root/oetwebsite && docker compose -f docker-compose.production.yml up -d oet-api-green oet-web-green"
# Wait ~30s for health checks
ssh vps "docker ps --filter 'name=oet-' --format 'table {{.Names}}\t{{.Status}}'"
# Update NPM upstream / swap blue/green per existing procedure
```

## Step 4 — Sanity probe (immediate)

```bash
# Confirm new endpoints respond
TOK=$(curl -s -X POST -H 'Content-Type: application/json' \
  -d '{"email":"manwara575@gmail.com","password":"REDACT","rememberMe":false}' \
  https://api.oetwithdrhesham.co.uk/v1/auth/sign-in | jq -r .accessToken)

curl -s -H "Authorization: Bearer $TOK" \
  https://api.oetwithdrhesham.co.uk/v1/admin/recall-documents | jq '.total'
curl -s -H "Authorization: Bearer $TOK" \
  https://api.oetwithdrhesham.co.uk/v1/admin/scoring-policy
curl -s -H "Authorization: Bearer $TOK" \
  https://api.oetwithdrhesham.co.uk/v1/admin/result-templates
curl -s -H "Authorization: Bearer $TOK" \
  https://api.oetwithdrhesham.co.uk/v1/admin/speaking/shared-resources
```

Expected: all return `200 OK` with empty arrays/null (no rows yet).

## Step 5 — Activate deferred Phase 1 publishes

Two complete Listening drafts user approved earlier:
- `1322a10d2e4644378ffdb131c3c2cb71` — OET Listening Practice — speech-pathology Standard Set 011
- `dabaf1c3067542168080c04587086a16` — OET Listening Practice — nursing Standard Set 026

```bash
curl -s -X POST -H "Authorization: Bearer $TOK" -H 'Content-Type: application/json' -d '{}' \
  https://api.oetwithdrhesham.co.uk/v1/admin/papers/1322a10d2e4644378ffdb131c3c2cb71/publish
curl -s -X POST -H "Authorization: Bearer $TOK" -H 'Content-Type: application/json' -d '{}' \
  https://api.oetwithdrhesham.co.uk/v1/admin/papers/dabaf1c3067542168080c04587086a16/publish
```

## Step 6 — User uploads the Project Real Content folder

1. Locally, zip the `Project Real Content` folder.
2. Log in to `https://app.oetwithdrhesham.co.uk/admin/content/imports/real-content-folder`.
3. Upload the ZIP.
4. Review proposals (every Listening sample, Reading sample, Writing letter, Speaking card, Recalls PDFs, Result templates, Rulebook PDFs, Scoring System).
5. Tick all → Commit. Drafts appear in their respective admin pages.
6. Publish each from its admin page when reviewed.

## Step 7 — AI/TTS backfill for 4 incomplete listening drafts

```bash
ssh vps "cd /opt/oetwebapp && node scripts/admin/retry-listening-tts.mjs --paper-id 06ed32dd4bce4800bbe84c16ec8507ca"
ssh vps "cd /opt/oetwebapp && node scripts/admin/retry-listening-tts.mjs --paper-id 51900b7211b84a8dbeb1d336b6e7c14a"
ssh vps "cd /opt/oetwebapp && node scripts/admin/retry-listening-tts.mjs --paper-id b8e0e9def00a4dd192beb08a5121deb9"
ssh vps "cd /opt/oetwebapp && node scripts/admin/retry-listening-tts.mjs --paper-id 16203e2a53344e598c532d67bb8d4cb8"
```

The 3 with no assets at all will need first an initial `generate-listening.mjs` pass (which requires DO Claude config) before retry-tts can fill in audio.

## Rollback

```bash
# If something breaks, swap blue back live and restore DB snapshot
ssh vps "cd /root/oetwebsite && docker compose -f docker-compose.production.yml up -d oet-api oet-web"
ssh vps "docker compose -f docker-compose.production.yml stop oet-api-green oet-web-green"
# Roll back migrations (only if data corruption — usually unnecessary):
ssh vps "docker exec oet-api dotnet ef database update PreviousMigrationName --context LearnerDbContext"
```

---

## Estimated downtime

Blue/green swap = ~0s user-visible downtime. EF migration application = <10s (all additive, no large data backfill).
