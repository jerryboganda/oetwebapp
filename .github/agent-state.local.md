# Agent State (local)

## Current task — Antigravity SDK gateway (SHIPPED, commit 8250ddb8b, pushed)
- `agent-gateway/` Python 3.12 service hosting google-antigravity v0.1.14: OpenAI-compatible `/v1/chat/completions` + native SSE sessions; 8 OET personas with rulebook skills; auth modes A gemini-key (hardened: budgets/cache/backoff) · B local-oauth (opt-in personal AI Pro quota, risk notice docs/antigravity/auth.md) · C sdk-oauth flip-day stub. 20 pytest tests green; live boot verified on host (healthz + /v1/agents).
- Backend: `AntigravityGatewaySeeder` (additive provider row `antigravity-gateway` + 18 route rows, `agent:<name>` model convention) + Program.cs hosted service. `dotnet build` green.
- Ops: agent-gateway service in dev/desktop/vps/production compose (Mode B hard-disabled in prod); deploy.yml GHCR build job + rollout script updated (gateway optional image, health-gated).
- Next: owner pastes GEMINI_API_KEY into VPS `.env.production` + internal token into `/admin/ai-providers` row, then flip a low-risk route (e.g. `card.draft.v1`) via admin route editor. Golden-set parity harness + Redis cache are Phase 3b/6 (docs/antigravity/roadmap.md).

## Previous — Auth/OTP mail isolated from marketing unsubscribe
- Root cause of missing password-reset OTP: Brevo accepted `/smtp/email` then blocked as unsubscribed. Marketing unsubscribe must never gate OTP.
- Code: four From lanes (`auth@`, `updates@`, `no-reply@`, `support@`). Webhook `unsubscribed` writes `__marketing__` only (never `EventKey=null`). Reputation events write `__non_auth__`. Admin inspect/unblock is one email; keeps marketing opt-out; never mass-unblock.
- Validation: `dotnet test --filter FullyQualifiedName~EmailLaneAndMailboxTests` 5/5.
- Next after this ship: owner unblocks only `drhagermurad2026@gmail.com` and `mindreader420123@gmail.com` in Admin → Notifications → Transactional Mailbox. In Brevo console confirm marketing unsubscribe does not add Transactional Blocklist, and OTP templates stay transactional From `auth@`.

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
