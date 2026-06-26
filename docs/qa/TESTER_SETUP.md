# OET Prep Desktop — Tester Setup & Install Guide

This guide covers installing the **OET Prep** desktop app (Tauri 2 build) for **internal testing**,
trusting the self-signed publisher certificate, known issues, and how to report bugs.

> **Why a trust step is needed:** internal builds are signed with a **self-signed** certificate
> ("OET Prep Internal Testing"), not a commercially-trusted one. Windows SmartScreen will warn about an
> "unknown publisher" until you import the public certificate **once**. This is expected and safe for an
> internal tester pool. (The public-trust upgrade path — Azure Trusted Signing — is noted at the end.)

---

## 1. System requirements

| | Windows | macOS |
|---|---|---|
| OS | Windows 10 (1809+) or Windows 11, x64 | macOS 12 (Monterey) or later |
| Runtime | Microsoft **Edge WebView2** runtime (auto-installed if missing) | built-in WKWebView |
| Disk | ~400 MB (app + bundled .NET API + Next.js server + Node) | ~400 MB |
| Network | Online for sign-in/sync; offline study works after first run | same |

The installer bundles everything (the .NET API, the Next.js server, and a Node runtime) — no separate
installs are required beyond WebView2 (which the installer fetches if absent).

---

## 2. Windows — install & trust the certificate (one time)

### Step A — Import the publisher certificate (do this first)
You'll receive a file named **`oet-codesign.cer`** out-of-band (not from the repo).

1. Double-click `oet-codesign.cer` → **Install Certificate**.
2. Store Location: **Current User** → Next.
3. Choose **Place all certificates in the following store** → **Browse** →
   select **Trusted Root Certification Authorities** → OK → Next → Finish → **Yes** to the warning.
4. Repeat steps 1–3, but this time select the **Trusted Publishers** store.

> Domain admins can push the `.cer` to both stores for all machines via Group Policy
> (Computer Configuration → Windows Settings → Security Settings → Public Key Policies).

### Step B — Install the app
1. Run **`OET Prep_<version>_x64-setup.exe`** (NSIS).
2. It installs per-user (no admin elevation required by default).
3. After Step A, the installer's **publisher shows "OET Prep Internal Testing"** (no "unknown publisher").
4. Launch from the Start Menu. First launch shows a brief splash ("Starting local services…") while the
   bundled API + renderer boot, then the app loads.

### Verify the signature (optional)
Right-click the installer → **Properties → Digital Signatures** → you should see
"OET Prep Internal Testing" with a valid timestamp.

---

## 3. macOS — install (unsigned build)

The macOS `.dmg` is **not notarized** (internal build), so Gatekeeper will block it on first open.

1. Open the `.dmg`, drag **OET Prep** to Applications.
2. **First launch:** right-click (or Control-click) the app → **Open** → **Open** in the dialog.
   (Double-clicking will show "cannot be opened because the developer cannot be verified" — use
   right-click → Open instead.)
3. Grant **microphone** permission when prompted (needed for Speaking practice).

> ⚠️ The macOS build is **CI-built but not yet functionally validated** (the microphone-capture gate
> requires a manual run on real Mac hardware). Treat macOS as **experimental** for this round.

---

## 4. Auto-updates

The app checks for updates on launch from `https://app.oetwithdrhesham.co.uk/desktop/updates/latest.json`
and verifies each update's **minisign signature** before installing. If the update feed isn't reachable,
the check fails silently and the app runs normally. (Feed hosting is a maintainer task — see §7.)

---

## 5. Known issues / limitations (this build)

- **Deep-link `oet-prep://pair?code=…`** routes to `/pair`, which doesn't exist yet → 404 (BUG-002,
  frontend, out of scope for this build). Tray links (Dashboard, Study Plan) work.
- **macOS** is unvalidated (see §3).
- Self-signed trust is **per-machine** — each tester must import the `.cer` (§2 Step A).
- Logs for troubleshooting live at:
  - Windows: `%APPDATA%\com.oetprep.desktop\logs\` (`backend.log`, `renderer.log`, `desktop.log`)
  - macOS: `~/Library/Application Support/com.oetprep.desktop/logs/`

---

## 6. How to report bugs

Include: **what you did**, **what happened**, **what you expected**, your **OS + version**, the app
**version** (Help/About or the installer filename), and attach the three log files from §5. File issues
on the repo's issue tracker (or the channel the maintainer designates).

---

## 7. Maintainer handoff (not for testers)

### Code-signing certificate
- Self-signed cert **"OET Prep Internal Testing"**, thumbprint
  `9E1D24DAB316C568A107E7EFD058786541B9DAA8`, wired in `src-tauri/tauri.dist.conf.json`
  (`bundle.windows.certificateThumbprint`) with `sha256` + RFC-3161 timestamp.
- Local builds sign automatically (cert is in `Cert:\CurrentUser\My`). To **regenerate** (e.g., on
  expiry in 3 years):
  ```powershell
  New-SelfSignedCertificate -Type CodeSigningCert `
    -Subject "CN=OET Prep Internal Testing" `
    -CertStoreLocation Cert:\CurrentUser\My `
    -KeyExportPolicy Exportable -KeyUsage DigitalSignature `
    -NotAfter (Get-Date).AddYears(3)
  ```
  Then update the thumbprint in `tauri.dist.conf.json` and re-export the `.cer`/`.pfx`.
- The `.pfx`, `.cer`, and base64 are in `~/.oet-signing/` (gitignored; **never commit**).

### GitHub secrets to set (release workflow)
```bash
# Windows Authenticode (self-signed) — base64 of the .pfx + its password
gh secret set WINDOWS_CERTIFICATE        < ~/.oet-signing/oet-codesign.pfx.b64
gh secret set WINDOWS_CERTIFICATE_PASSWORD --body "oet-internal-signing"
# Updater minisign private key (generated; empty password)
gh secret set TAURI_SIGNING_PRIVATE_KEY  < ~/.tauri/oet-updater-prod.key
gh secret set TAURI_SIGNING_PRIVATE_KEY_PASSWORD --body ""
```

### Updater feed hosting (one-time, on the VPS behind Nginx Proxy Manager)
Serve the three release files (`latest.json`, the signed `*-setup.exe`, and its `*.sig`) at
`https://app.oetwithdrhesham.co.uk/desktop/updates/`. Example Nginx location (static dir):
```nginx
location /desktop/updates/ {
    alias /srv/oet-desktop-updates/;
    autoindex off;
    add_header Cache-Control "no-cache";
    types { application/json json; application/octet-stream exe sig; }
}
```
Then on each release, copy the workflow artifacts (`latest.json`, `.exe`, `.sig`) into
`/srv/oet-desktop-updates/`. `latest.json` format (Tauri v2):
```json
{
  "version": "0.1.1",
  "notes": "…",
  "pub_date": "2026-06-25T00:00:00Z",
  "platforms": {
    "windows-x86_64": {
      "signature": "<contents of the .sig file>",
      "url": "https://app.oetwithdrhesham.co.uk/desktop/updates/OET%20Prep_0.1.1_x64-setup.exe"
    }
  }
}
```

### Public-trust upgrade path (beyond internal testing)
For wider/public distribution without per-machine cert imports, switch Windows signing to
**Azure Trusted Signing** (~$10/mo, one-time identity validation) — publicly trusted, no SmartScreen
publisher warning. The release workflow already has hooks for Azure/`WIN_CSC_*` secrets.
