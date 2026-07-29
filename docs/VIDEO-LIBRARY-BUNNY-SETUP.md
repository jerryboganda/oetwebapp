# Video Library — Bunny Stream setup & activation runbook

This runbook describes the current short-lived, token-authenticated Bunny embed
contract used by production. Provider-side values still have to be checked
against the real library after every security-setting change.

## 1. Create the Bunny Stream library

In the Bunny dashboard → **Stream** → create or open the production video
library. In its security settings, use this application-compatible profile:

| Setting | Value | Why |
|---|---|---|
| **Embed view token authentication** | **ON** | The API signs the iframe embed for one video and a maximum of 300 seconds. |
| **MediaCage Basic DRM** | **ON** at minimum | The supported encrypted/download-resistant playback surface is Bunny's embed player. Use Enterprise DRM when Widevine/FairPlay license enforcement is required. |
| **MP4 fallback** | **OFF** | Prevents a downloadable MP4 fallback from weakening the protected stream. |
| **Allow Direct Play** (`AllowDirectPlay`) | **OFF** | Learner playback never receives a raw HLS/MP4 URL. |
| **Expose/keep original files** | **OFF** | Prevents original uploads from being directly retrievable after encoding. |
| **Block direct URL file access** (`BlockNoneReferrer`) | **OFF** | Native WebViews do not reliably send a browser `Referer`; embed token auth and MediaCage are the enforceable controls. |
| **Allowed domains** | production web/app domains | Defense in depth for browser embedding; do not rely on this instead of signed embeds. |
| **CDN token authentication** | **ON** | Still protects exact-path signed thumbnails and other CDN assets. Video playback itself uses the signed embed. |

> **Keys must match their purpose.** The library **API key** signs learner
> embeds. The pull-zone **Token authentication key** signs exact-path CDN
> assets. If either is regenerated, update only its corresponding encrypted
> Admin → Settings field.

> **Diagnose fast.** `scripts/videos/diagnose-playback.mjs` signs a real playback URL the
> same way the backend does and probes it (with/without referer + encode status) so you can
> tell a referrer block (403), an un-encoded video (404) and a working stream (200) apart.

## 2. Enable pull-zone token authentication

The library has an attached **pull zone** (CDN). In the pull zone →
**Security**:

- Turn **Token Authentication ON** (`ZoneSecurityEnabled = true`).
- Copy the pull zone **Token Authentication Key** (`ZoneSecurityKey`). This is
  the value you paste into the admin panel as the **CDN token-auth key**.

> Note: after toggling token auth there is a short (~seconds to a minute) propagation
> window before it is enforced at the edge.

## 3. Configure the app (Admin → Settings → Bunny Stream)

| Admin field | Bunny source |
|---|---|
| Library ID | the numeric library `Id` |
| API key | the library's **API Key** (per-library, used for uploads/metadata) |
| CDN hostname | the pull zone hostname, e.g. `vz-xxxxxxxx-xxx.b-cdn.net` |
| CDN token-auth key | the pull zone **ZoneSecurityKey** from step 2 (exact-path CDN assets) |
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

## 5. Playback authorisation

Native playback sessions use attested Tauri/Capacitor clients. Generate two
random 64-char secrets and set them in **both** places (they must match
byte-for-byte):

- GitHub repo secrets `OET_DESKTOP_ATTEST_SECRET`, `OET_MOBILE_ATTEST_SECRET` (baked into the
  app binaries at build time).
- The admin **attestation key map**, e.g.
  `{"tauri:v1":"<desktop-secret>","capacitor-android:v1":"<mobile-secret>","capacitor-ios:v1":"<mobile-secret>"}`.

Web playback uses the authenticated access-token family plus a user-bound,
single-use 90-second nonce. It intentionally does not embed a browser secret.
Both native and web receive only a short-lived signed Bunny embed.

## Verified signing algorithms (pinned by `BunnyStreamClientTests`)

- **TUS presigned upload**: `sha256_hex(libraryId + apiKey + expires + videoId)` — confirmed
  (valid → 201, wrong → 401).
- **Embed view token** (all learner playback):
  `sha256_hex(libraryApiKey + videoId + expires)`. URL:
  `https://iframe.mediadelivery.net/embed/{libraryId}/{videoId}?token=…&expires=…`.
  Production uses a 300-second expiry. The raw HLS/MP4 URL is never returned.
- **CDN file token** remains only for exact-path signed thumbnails; it cannot
  authorise the video directory or playback manifest.
