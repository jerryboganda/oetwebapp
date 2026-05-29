# Deployment runbook — Phases 2-7

**Target**: `vps` (~~185.252.233.186 — DECOMMISSIONED 20 May 2026~~ → now `68.183.32.122` `oet-dev`) — `/root/oetwebsite`, `/opt/oetwebapp`
**Containers**: `oet-api` + `oet-api-green` (blue/green), `oet-web` + `oet-web-green`, `oet-postgres` (pg17), `oet-db-backup`, `oet-clamav`

---

## Pre-flight

```bash
# Confirm prod health
ssh oet-dev "docker ps --filter 'name=oet-' --format 'table {{.Names}}\t{{.Status}}'"

# Take a DB snapshot before applying migrations
ssh oet-dev "docker exec oet-postgres pg_dump -U postgres oet_learner | gzip > /opt/oetwebapp-backups/oet-learner-pre-phase-2-7-$(date -u +%Y%m%dT%H%M%SZ).sql.gz"
```

## Step 1 — Push to remote

```bash
# Local
git add backend/src/OetLearner.Api app/admin/content app/recalls/documents \
        lib/api.ts lib/admin-permissions.ts \
  _audit/deployment-runbook.md _audit/phase-2-7-build-summary.md \
  backend/tests/OetLearner.Api.Tests/ReadingAuthoringTests.cs
git status   # eyeball — make sure NO .secrets/, backups/, or _audit/*.zip slipped in
git commit -m "feat(content): Phase 2-7 admin UIs (recalls, scoring, rulebook PDFs, result templates, speaking shared, folder importer)"
git push origin cleanup/remove-demo-dummy-seed-placeholder-data
```

## Step 2 + 3 — Canonical blue/green deploy (use the project's deploy-direct.sh)

The new VPS ships with the project's blue/green deploy script that handles
fetch → checkout → build → health-check → router swap → rollback-on-failure.
Do NOT run individual `docker compose` commands; use the script.

```bash
# DEPLOY_REF must be an immutable 40-character commit SHA with digest-pinned image refs.
DEPLOY_SHA=<40-character-sha>
ssh oet-dev "cd /opt/oetwebapp && nohup bash -lc 'DEPLOY_REF=$DEPLOY_SHA bash scripts/deploy/deploy-direct.sh' > /tmp/deploy-$DEPLOY_SHA.log 2>&1 < /dev/null &"
ssh oet-dev "tail -n 120 /tmp/deploy-$DEPLOY_SHA.log"
```

Behavior:
- Determines the idle slot (`.deploy/active-slot.env` → toggles blue/green).
- Builds `learner-api-<slot>` + `web-<slot>` from source.
- Starts the new slot with `--force-recreate` and waits for health checks on
  `:8080/health/ready` (API) + `:3000/api/health` (web).
- Switches the stable `learner-api` + `web` routers to the new slot.
- Verifies public endpoints at `https://api.oetwithdrhesham.co.uk/health/ready`
  + `https://app.oetwithdrhesham.co.uk/api/health`.
- On health-check failure: the previous slot stays active (zero-impact).

EF migrations are applied automatically inside the new image at startup via
the `DatabaseBootstrapper` service — no separate `dotnet ef database update`
needed. (See `backend/src/OetLearner.Api/Services/DatabaseBootstrapper.cs`.)

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

## Step 5 — Review deferred Phase 1 publish candidates

Two complete Listening drafts were identified in the earlier audit:
- `1322a10d2e4644378ffdb131c3c2cb71` — OET Listening Practice — speech-pathology Standard Set 011
- `dabaf1c3067542168080c04587086a16` — OET Listening Practice — nursing Standard Set 026

Before publishing anything, re-run the production audit against the current VPS,
confirm the paper IDs still match, review each draft in the admin UI, and record
explicit owner approval. Do not run direct publish commands from this runbook.

## Step 6 — User uploads the Project Real Content folder

1. Locally, zip the `Project Real Content` folder.
2. Log in to `https://app.oetwithdrhesham.co.uk/admin/content/imports/real-content-folder`.
3. Upload the ZIP.
4. Review proposals (every Listening sample, Reading sample, Writing letter, Speaking card, Recalls PDFs, Result templates, Rulebook PDFs, Scoring System).
5. Tick all → Commit. Drafts appear in their respective admin pages.
6. Publish each from its admin page when reviewed.

## Step 7 — AI/TTS backfill for 4 incomplete listening drafts

ElevenLabs credits exhausted May 2026 — route through DigitalOcean Qwen3-TTS
Voice Design with British English male voice. The script supports a
`TTS__ForceProvider=digitalocean` env var that bypasses ElevenLabs entirely.

```bash
# Required env vars on the VPS shell (or .env.production):
#   AI__ApiKey            – DigitalOcean Serverless Inference API key
#   AI__BaseUrl           – default https://inference.do-ai.run/v1 (no need to set)
#   AI__TtsMaleVoice      – default 'british-male' (override if DO uses different seed)
#   TTS__ForceProvider    – 'digitalocean' to bypass ElevenLabs

ssh oet-dev "cd /opt/oetwebapp && TTS__ForceProvider=digitalocean node scripts/admin/retry-listening-tts.mjs --paper-id 06ed32dd4bce4800bbe84c16ec8507ca"
ssh oet-dev "cd /opt/oetwebapp && TTS__ForceProvider=digitalocean node scripts/admin/retry-listening-tts.mjs --paper-id 51900b7211b84a8dbeb1d336b6e7c14a"
ssh oet-dev "cd /opt/oetwebapp && TTS__ForceProvider=digitalocean node scripts/admin/retry-listening-tts.mjs --paper-id b8e0e9def00a4dd192beb08a5121deb9"
ssh oet-dev "cd /opt/oetwebapp && TTS__ForceProvider=digitalocean node scripts/admin/retry-listening-tts.mjs --paper-id 16203e2a53344e598c532d67bb8d4cb8"
```

The 3 with no AudioScript at all will need first an initial
`generate-listening.mjs` pass (Claude Opus 4.7 via DO Serverless Inference)
before retry-tts can fill in audio.

## Rollback

```bash
# If something breaks, swap blue back live and restore DB snapshot
ssh oet-dev "cd /root/oetwebsite && docker compose -f docker-compose.production.yml up -d oet-api oet-web"
ssh oet-dev "docker compose -f docker-compose.production.yml stop oet-api-green oet-web-green"
# Roll back migrations (only if data corruption — usually unnecessary):
ssh oet-dev "docker exec oet-api dotnet ef database update PreviousMigrationName --context LearnerDbContext"
```

---

## Estimated downtime

Blue/green swap = ~0s user-visible downtime. EF migration application = <10s (all additive, no large data backfill).
