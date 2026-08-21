# Private native distribution (2026-08-22)

## Problem

Desktop auto-update and native download CTAs still depend on GitHub Releases
(`api.github.com`, `github.com/.../releases/download`). That exposes the private
source repository whenever those URLs are used, and it breaks when the repo is
private.

Repo visibility may still flip for Actions compute. End users must never need
GitHub.

## Target

```text
Private GitHub + Actions
        ↓ build / sign / checksum
Secure SSH upload
        ↓
VPS /var/opt/oet-learner/releases/
        ↓ HTTPS
app.oetwithdrhesham.co.uk/desktop/updates/latest.json
app.oetwithdrhesham.co.uk/releases/...
app.oetwithdrhesham.co.uk/api/download/{windows,mac,android,ios}
app.oetwithdrhesham.co.uk/api/releases/native
        ↓
Desktop Tauri updater · marketing CTAs · mobile fallback
```

## Non-goals

- Changing the public/private Actions visibility workflow
- Moving heavy builds onto the VPS
- Play/App Store listing work
- CDN/object-storage migration

## Layout

```text
/var/opt/oet-learner/releases/
  desktop/current.json
  desktop/<version>/*
  mobile/android/current.json
  mobile/android/<version>/*
  mobile/ios/current.json
  mobile/ios/<version>/*
```

`current.json` is written last. Versioned binaries are immutable.

## Trust

Installer and updater URLs must be HTTPS under
`https://app.oetwithdrhesham.co.uk/releases/`. GitHub hosts are rejected.
Missing catalogs fail closed to `/get-app`, never to GitHub.
