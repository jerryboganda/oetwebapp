# Native release process

## Prerequisites

1. Deploy the web stack so `/var/opt/oet-learner/releases` is mounted into
   `web`, `web-blue`, and `web-green`.
2. Repository secrets: `PROD_SSH_KEY`, plus the existing signing secrets.

## Desktop

1. Tag `vX.Y.Z-tauri-desktop` or run **Tauri Desktop Release**.
2. Actions builds Windows/macOS, signs updater artifacts, then uploads only
   that version to the VPS.
3. Previous desktop files on the VPS are deleted automatically.
4. Verify:
   - `https://app.oetwithdrhesham.co.uk/desktop/updates/latest.json`
   - `https://app.oetwithdrhesham.co.uk/api/download/windows`

## Mobile

1. Run **Mobile Release** with the version.
2. Actions publishes the latest APK/IPA to the VPS and deletes the previous
   mobile files for that platform.
3. Verify `https://app.oetwithdrhesham.co.uk/api/releases/native?platform=android`.

## Rollback

Re-run the workflow for the last known-good version. That republishes it as
the single latest catalog entry.
