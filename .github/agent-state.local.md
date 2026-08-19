# Agent State (local)

## Goal
Continue official OET Reading uploads on production for **oetwebapp**.
Publish live. Same five book folders for Full Exam and Part A/B/C.

## Read first
1. `docs/READING-UPLOAD-ZERO-DEVIATION-CONTRACT.md`
2. `docs/READING-UPLOAD-AGENT-HANDOFF.md`
3. `docs/READING-MODULE-SAVE-AND-UPLOAD.md`

## Already live — do not re-import
- Jayden JB1–JB5 — part-only QuestionPaper PDFs attached
- Anna Hartford AH1–AH3 — part-only QuestionPaper PDFs attached
- `reading-sample-1` already had separate part files

## Next step
Owner 2026-08-19: Atlas 09 Full Exam starts without C2 (same 60-min timer).
Deploy `CanStartFullExam` on production. Do not invent C2. Do not publish VD6.

## Constraints
- Public API only. No local API/DB. No `--dev-auth` on Production.
- Do not retry `admin@oet-prep.dev` or bootstrap passwords.
- GitHub Actions for deploys. Make the repo public for the run, then private again. Never leave it public. No VPS compute. No green recreate.
- Not DMB. Checkout is `E:\Projects\OET with Dr Hesham\Web App` on `main`.
