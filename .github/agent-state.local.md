# Agent State (local)

## Current task — Mobile OTP auto-rotation on resume (FIXED)
- Shipped `c512a4e4` on `main`: /verify-email challenge now persisted in localStorage (survives WebView process death), module-scoped auto-send guard (`app/verify-email/auto-send-guard.ts`), resume-time refreshSession skipped on auth/OTP screens in MobileRuntimeBridge. Validation: vitest app/verify-email 4/4; tsc errors all pre-existing/unrelated.
- Next: owner verifies on production Android/iOS build — request OTP, background app to check mail, return: screen must NOT reload or request a new OTP.

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
