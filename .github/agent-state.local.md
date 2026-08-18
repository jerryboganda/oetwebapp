# Agent State (local)

## Goal
Continue official OET Reading uploads on production for **oetwebapp**.
Publish live. Same five book folders for Full Exam and Part A/B/C.

## Read first
1. `docs/READING-UPLOAD-AGENT-HANDOFF.md`
2. `docs/READING-MODULE-SAVE-AND-UPLOAD.md`

## Already live — do not touch
- Jayden JB1–JB5
- Anna Hartford AH1–AH3
- `reading-sample-1`

## Next step
Wait for the owner to upload **Atlas Practice Series** PDFs (full 20/6/16 + printed keys). Then extract, build manifests, offline-validate, import via the production public API with device headers, and publish status 4. If Atlas is not in the session, ask for the PDFs. Do not invent papers.

## Constraints
- Public API only. No local API/DB. No `--dev-auth` on Production.
- Do not retry `admin@oet-prep.dev` or bootstrap passwords.
- GitHub Actions for deploys. No VPS compute. No green recreate.
- Not DMB. Checkout is `E:\Projects\OET with Dr Hesham\Web App` on `main`.
