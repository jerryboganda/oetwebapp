# CI/CD security

## Visibility

The source repository may be flipped public only for GitHub Actions compute.
That is an operator convenience. End-user downloads and auto-updates must never
require GitHub, even while the repo is public.

## What clients may contact

- `https://app.oetwithdrhesham.co.uk/desktop/updates/latest.json`
- `https://app.oetwithdrhesham.co.uk/releases/**`
- `https://app.oetwithdrhesham.co.uk/api/download/{windows,mac,android,ios}`
- `https://app.oetwithdrhesham.co.uk/api/releases/native`

Never:

- `github.com/jerryboganda/oetwebapp`
- `api.github.com`
- `releases/download`

## Secrets

| Secret | Purpose |
| --- | --- |
| `PROD_SSH_KEY` | Dedicated deploy key used by Actions to publish images and native artifacts |
| `TAURI_SIGNING_PRIVATE_KEY` | Minisign private key for desktop updater artifacts |
| `TAURI_SIGNING_PRIVATE_KEY_PASSWORD` | Password for the updater key |
| `WINDOWS_CERTIFICATE` | Optional Authenticode PFX (base64) |
| `WINDOWS_CERTIFICATE_PASSWORD` | Optional Authenticode password |
| `ANDROID_KEYSTORE_*` | Android release signing |
| `OET_MOBILE_ATTEST_SECRET` / `OET_DESKTOP_ATTEST_SECRET` | Playback attestation, baked at build time |
| `APPLE_*` / `IOS_*` | Optional iOS signing |

Do not embed GitHub tokens, deploy keys, or signing private keys in client apps.

## Permissions

Release publish jobs only need `contents: read`. They upload to the VPS over
SSH. They do not publish production installers as GitHub Release assets.
