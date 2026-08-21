# Rollback

## Web / API

Blue/green deploy keeps the previous slot. A failed health gate does not flip
the router. To roll back a promoted web/API release, redeploy the last known
good SHA with the existing image-only workflow.

## Native desktop / mobile

The VPS keeps **only the latest installer per channel** (`desktop`, `android`,
`ios`) so disk use stays small. There is no previous binary left on the box.

Rollback is:

1. Re-run **Tauri Desktop Release** or **Mobile Release** for the last good version.
2. The workflow republishes that version as the single current catalog entry.
3. Confirm `/desktop/updates/latest.json` or `/api/releases/native` shows the
   restored version and a `/releases/...` HTTPS URL.

Do not point clients back at GitHub Releases.
