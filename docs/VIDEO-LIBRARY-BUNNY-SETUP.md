# Video Library — Bunny Stream setup & activation runbook

Everything below was **verified end-to-end against the live Bunny Stream API on 2026-07-03**
using a throwaway library. The feature is dormant until these steps are done.

## 1. Create the Bunny Stream library

In the Bunny dashboard → **Stream** → create a video library. Then, in the library's
**Security → General** page, use the **maximum-security** configuration below (owner directive
2026-07-14). The one non-obvious requirement: with **Block direct url file access ON** you
MUST allow-list the app domain, or the CDN returns `403 Forbidden` on every HLS request
*before the playback token is even evaluated* (the app's own requests match no referrer).

| Setting | Value | Why |
|---|---|---|
| **CDN token authentication** | **ON** | THE control. The app signs every `playlist.m3u8` + segment URL (directory token, see §"Verified signing"), per-video and time-limited, and only after native-app attestation. Cryptographic and unforgeable — this alone is maximum security. Verified 2026-07-14: signed → 200 valid HLS, unsigned → 403. |
| **Embed view token authentication** | **ON** (optional) | Governs Bunny's *iframe* embed player, which we do **not** use — harmless to leave on. |
| **Block direct url file access** (`BlockNoneReferrer`) | **OFF** | ⚠️ **Must be OFF for native-app playback.** It 403s any request without an accepted `Referer`, and the Tauri/Capacitor WebViews do **not** reliably send one — so with it ON, *no* video plays (proven 2026-07-14: signed URL → 200 *with* a referer, 403 *without*; the desktop app sent none). It is also pure referrer-checking = trivially spoofable (a scripted fake `Referer` passes it), so it adds **no** real security over CDN token auth. Leaving it OFF loses nothing and is required for playback. |
| **Allowed domains** | leave empty | Only relevant if the referrer block is on (which it must not be). |
| **Allow Direct Play** (`AllowDirectPlay`) | **ON** | We stream `playlist.m3u8` into hls.js / native HLS, not Bunny's iframe player. Off ⇒ 403. |

Optional: MP4 fallback can stay on; it doesn't affect HLS.

> **Why not keep the referrer block on for "extra" security?** Because it is referrer-based
> (spoofable) and native WebViews don't send an accepted Referer, so it only ever blocks your
> *own* app while a determined hotlinker can forge the header anyway. CDN **token**
> authentication — signed, per-video, expiring, attestation-gated — is the real, unforgeable
> control and stays ON. That is the maximum *effective* security.

> **Token key must match.** The library's **Token authentication key** (Security → General)
> is what the app signs with. If it is ever regenerated (↻), paste the new value into
> Admin → Settings → Bunny Stream → **CDN token-auth key** or every URL 403s.

> **Diagnose fast.** `scripts/videos/diagnose-playback.mjs` signs a real playback URL the
> same way the backend does and probes it (with/without referer + encode status) so you can
> tell a referrer block (403), an un-encoded video (404) and a working stream (200) apart.

## 2. Enable pull-zone token authentication

The library has an attached **pull zone** (CDN). In the pull zone → **Security**:

- Turn **Token Authentication ON** (`ZoneSecurityEnabled = true`).
- Copy the pull zone **Token Authentication Key** (`ZoneSecurityKey`, a 36-char string).
  This is the value you paste into the admin panel as the **CDN token-auth key**.

> Note: after toggling token auth there is a short (~seconds to a minute) propagation
> window before it is enforced at the edge.

## 3. Configure the app (Admin → Settings → Bunny Stream)

| Admin field | Bunny source |
|---|---|
| Library ID | the numeric library `Id` |
| API key | the library's **API Key** (per-library, used for uploads/metadata) |
| CDN hostname | the pull zone hostname, e.g. `vz-xxxxxxxx-xxx.b-cdn.net` |
| CDN token-auth key | the pull zone **ZoneSecurityKey** from step 2 |
| Webhook secret | any strong random string you choose (see step 4) |
| Attestation keys | the two app-attestation secrets (see step 5) |

`MEDIA_CDN_ORIGINS` env: the CSP default already allows `*.b-cdn.net`. Only set this env
(and redeploy) if you put a **custom CNAME** in front of the pull zone.

## 4. Webhook

In the Bunny library settings set the **Webhook URL** to:

```
https://api.oetwithdrhesham.co.uk/v1/webhooks/bunny-stream?secret=<the webhook secret from step 3>
```

Verified payload Bunny actually sends (field names matter — the parser reads these):

```json
{ "IsLiveStreamWebhook": false, "VideoLibraryId": 696391, "VideoGuid": "…", "Status": 1 }
```

Status mapping (verified): `0 Queued, 1 Processing, 2 Encoding, 3 Finished→Ready,
4 ResolutionFinished→Ready, 5 Failed`. The webhook is a convenience; the leader-locked
`BunnyEncodeStatusWorker` reconciles status every 5 min regardless.

## 5. App attestation secrets (playback is native-app-only)

Playback sessions are issued only to attested Tauri/Capacitor clients. Generate two random
64-char secrets and set them in **both** places (they must match byte-for-byte):

- GitHub repo secrets `OET_DESKTOP_ATTEST_SECRET`, `OET_MOBILE_ATTEST_SECRET` (baked into the
  app binaries at build time).
- The admin **attestation key map**, e.g.
  `{"tauri:v1":"<desktop-secret>","capacitor-android:v1":"<mobile-secret>","capacitor-ios:v1":"<mobile-secret>"}`.

Then ship the app releases: tag `desktop-v0.4.0` and dispatch mobile release `1.2.0`. Older
app builds show a friendly "update your app" screen; browsers always show the locked player.

## Verified signing algorithms (pinned by `BunnyStreamClientTests`)

- **TUS presigned upload**: `sha256_hex(libraryId + apiKey + expires + videoId)` — confirmed
  (valid → 201, wrong → 401).
- **CDN playback token** (directory auth): `base64url_nopad( sha256( tokenAuthKey + tokenPath
  + expires + "token_path=" + tokenPath ) )`, where `tokenPath = /{videoId}/`. The trailing
  `token_path=` parameter-data suffix is **required** — omitting it returns 403. URL:
  `https://{host}/{videoId}/playlist.m3u8?token=…&expires=…&token_path=%2F{videoId}%2F`.
  The **same** token authorizes all child media playlists/segments under `/{videoId}/`; the
  player (hls.js) re-appends the query to every child request, and Apple native HLS propagates
  it automatically — worth a one-time live check on iOS/macOS.
