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
Owner should click **Start full exam** again. The marking-policy block is cleared: effective Reading/Listening policies plus 0–42 conversion tables are on production. Then continue Atlas PDFs when uploaded.

## Constraints
- Public API only. No local API/DB. No `--dev-auth` on Production.
- Do not retry `admin@oet-prep.dev` or bootstrap passwords.
- GitHub Actions for deploys. Make the repo public for the run, then private again. Never leave it public. No VPS compute. No green recreate.
- Not DMB. Checkout is `E:\Projects\OET with Dr Hesham\Web App` on `main`.
