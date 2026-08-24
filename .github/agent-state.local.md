# Agent State (local)

## Current task — Antigravity integration COMPLETE; idle for next task
- All code phases shipped: v0.2 hardening (`d73c999a8`) + phases 3c–7 (`f6ad1d090`). Gateway pytest 38/38; cargo check OK.
- Live state: students on existing providers; gateway Mode A (Gemini API key) standby; AI Pro Antigravity quota unused in prod (Mode B = owner PC only; Mode C awaits Google — `pnpm ai:flipday-watch`).
- Owner-side ops pending (manual): `pnpm ai:parity` → 10% route flips in `/admin/ai-providers` (provider `antigravity-gateway`, model `agent:<name>`) → 100%; desktop clean-PC smoke; mobile device pass (`docs/antigravity/mobile-validation.md`); install `ops/prometheus/alerts-oet-gateway.yml` on VPS monitoring.
- Plain-language status + admin switch guide: `docs/antigravity/README.md` (Current status section). Lessons + env gotchas for future agents: `docs/dev/lessons-learned.md` (python via agent-gateway venv only, no docker locally, cargo check with space-free CARGO_TARGET_DIR, cargo test impossible on this host).

## Previous — Antigravity gateway hardening (v0.2)
- Circuit breaker per route (fast-fail 503 → resolver fallback), turn timeout → 504, constant-time token compare, request caps 413, SSE keepalive, idle-session reaper, lock-safe eviction, graceful drain, accurate usage accounting, opt-in structured outputs (fixed manifest `json.loads(dict)` crash), optional Redis cache, Prometheus `/v1/metrics`, JSON logs.
- Compose dev/desktop/vps/production carry new env knobs; prod/vps got `stop_grace_period: 45s`. Gateway version 0.2.0. Commit `d73c999a8`.

## Previous — production deploy of ff29552c+fix
- Deploy blocked on missing GEMINI_API_KEY: gateway crashed in lifespan so routers did not flip.
- Gateway now degrades (healthz HTTP 200, auth_ready=false) so web/API can promote without a Gemini key.

## Previous — Auth/OTP mail isolated from marketing unsubscribe
- Root cause of missing password-reset OTP: Brevo accepted `/smtp/email` then blocked as unsubscribed. Marketing unsubscribe must never gate OTP.
- Code: four From lanes (`auth@`, `updates@`, `no-reply@`, `support@`). Webhook `unsubscribed` writes `__marketing__` only (never `EventKey=null`). Reputation events write `__non_auth__`. Admin inspect/unblock is one email; keeps marketing opt-out; never mass-unblock.
- Validation: `dotnet test --filter FullyQualifiedName~EmailLaneAndMailboxTests` 5/5. Shipped `dc92bbcc` via Actions `32652938029` (success). Repo private again.
- Next: owner unblocks only `drhagermurad2026@gmail.com` and `mindreader420123@gmail.com` in Admin → Notifications → Transactional Mailbox. In Brevo console confirm marketing unsubscribe does not add Transactional Blocklist, and OTP templates stay transactional From `auth@`.

## Previous — Mobile OTP auto-rotation on resume (FIXED + DEPLOYED)
- Fix commit `c512a4e4` deployed to production 2026-08-23 via rerun of Actions `32640038373`. Prod probe: /verify-email returns 200.
- No fresh app release needed: Capacitor `server.url` is remote-only.

## Goal
Continue official OET Reading uploads on production for **oetwebapp**.
Publish live. Same five book folders for Full Exam and Part A/B/C.

## Read first
1. `docs/READING-UPLOAD-ZERO-DEVIATION-CONTRACT.md`
2. `docs/READING-UPLOAD-AGENT-HANDOFF.md`
3. `docs/READING-MODULE-SAVE-AND-UPLOAD.md`
4. `docs/PRODUCTION-DATA-PERSISTENCE.md`

## Already live — do not re-import
- 80 published Reading papers (Jayden 01–05, AH 01–03, Atlas 01–26+Kaplan, Nova 01–20, VD 01–05/07–25, `reading-sample-1`)
- Atlas 09 Head injuries is 20/6/8 (no C2). Full exam starts. Scoring /42.
- Candidate Part B/C visible (`fdcc4776`): one Part A/B/C booklet on the left; Part B questions separate on the right.

## Next step
Idle unless the owner sends Atlas 09 C2 or asks to import Desktop extra `Reading -1.pdf`. Never invent C2. Never publish VD6. Never `down -v`.

## Persistence
Named volumes `oetwebsite_oet_*` are independent of containers. Compose pins them `external: true`. Host wrapper `/usr/local/bin/docker` blocks volume rm/prune and `compose down -v`. Content is deleted only from the admin UI. Deploy only recreates web/API slots.

## Constraints
- Public API only. No local API/DB. No `--dev-auth` on Production.
- Do not retry `admin@oet-prep.dev` or bootstrap passwords.
- GitHub Actions for deploys. Make the repo public for the run, then private again. Never leave it public. No VPS compute. No green recreate.
- Not DMB.
