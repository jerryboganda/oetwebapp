# Automatic update architecture

End users never contact GitHub. Source can stay private (or flip public only
for Actions compute). Downloads and updates come from production.

```text
GitHub Actions (build / sign / checksum)
        ↓ SSH
VPS /var/opt/oet-learner/releases/{desktop,mobile}/<latest>
        ↓ HTTPS
app.oetwithdrhesham.co.uk
        ├── /desktop/updates/latest.json
        ├── /releases/**
        ├── /api/download/{windows,mac,android,ios}
        └── /api/releases/native?platform=android|ios
        ↓
Desktop Tauri updater · website CTAs · mobile APK fallback
```

## Desktop

The Tauri shell checks `https://app.oetwithdrhesham.co.uk/desktop/updates/latest.json`,
downloads the installer from `/releases/desktop/<version>/`, verifies the
minisign signature, then installs. A failed check leaves the app usable.

## Mobile

Play/App Store remain the primary path. `/api/releases/native` advertises the
latest VPS-hosted APK/IPA as a same-origin fallback. iOS production installs
still prefer the App Store when configured.

## Retention

Publish keeps **only the latest release per channel** (`desktop`, `android`,
`ios`). The previous version directory is deleted automatically so the VPS
does not accumulate installers.
