#!/usr/bin/env node
/**
 * Video Library playback diagnostic.
 *
 * Reproduces the backend's Bunny CDN directory-token signing (VideoPlaybackSessionService
 * -> BunnyStreamClient.ComputeCdnToken) EXACTLY, then probes the signed playlist.m3u8 to
 * pinpoint why "any of the available videos are not playing":
 *
 *   403  -> CDN token or referrer block ("Block direct url file access")
 *   404  -> the video is not finished encoding on Bunny yet (no playlist)
 *   200  -> playback is authorised; the problem is purely client-side (CSP / hls.js)
 *
 * It probes WITH and WITHOUT a Referer header so you can tell a referrer block apart from
 * a token problem, and (optionally, with the library API key) reports the encode status.
 *
 * Nothing is written anywhere and no secret is printed. Run:
 *
 *   BUNNY_CDN_HOST=vz-xxxxxxxx-xxx.b-cdn.net \
 *   BUNNY_TOKEN_KEY=65c44867-715e-4f2b-ad8a-034229bd1524 \
 *   BUNNY_VIDEO_GUID=71874a09-b1a5-41c7-9522-166f1169394a \
 *   APP_ORIGIN=https://app.oetwithdrhesham.co.uk \
 *   [BUNNY_LIBRARY_ID=696416 BUNNY_STREAM_KEY=<library api key>] \
 *   node scripts/videos/diagnose-playback.mjs
 *
 * On Windows PowerShell set each with `$env:NAME = "value"` first, then run node.
 */
import { createHash } from 'node:crypto';

const CDN_HOST = process.env.BUNNY_CDN_HOST?.trim().replace(/^https?:\/\//, '').replace(/\/+$/, '');
const TOKEN_KEY = process.env.BUNNY_TOKEN_KEY?.trim();
const VIDEO_GUID = (process.env.BUNNY_VIDEO_GUID || '71874a09-b1a5-41c7-9522-166f1169394a').trim();
const APP_ORIGIN = (process.env.APP_ORIGIN || 'https://app.oetwithdrhesham.co.uk').trim().replace(/\/+$/, '');
const LIBRARY_ID = process.env.BUNNY_LIBRARY_ID?.trim() || '696416';
const STREAM_KEY = process.env.BUNNY_STREAM_KEY?.trim();

if (!CDN_HOST || !TOKEN_KEY) {
  console.error('Missing required env. Need at least BUNNY_CDN_HOST and BUNNY_TOKEN_KEY.');
  console.error('Find CDN host: Bunny -> Stream -> library -> API (or any thumbnail URL host).');
  console.error('Find token key: Bunny -> Stream -> library -> Security -> General -> Token authentication key.');
  process.exit(2);
}

const base64url = (buf) =>
  buf.toString('base64').replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, '');

/** Byte-for-byte mirror of BunnyStreamClient.ComputeCdnToken (directory auth). */
function computeCdnToken(tokenAuthKey, tokenPath, expiresUnix) {
  const payload = `${tokenAuthKey}${tokenPath}${expiresUnix}token_path=${tokenPath}`;
  return base64url(createHash('sha256').update(payload, 'utf8').digest());
}

/** Byte-for-byte mirror of BunnyStreamClient.BuildSignedPlaybackUrl. */
function buildSignedPlaybackUrl(host, videoId, token, expiresUnix, tokenPath) {
  return `https://${host}/${videoId}/playlist.m3u8`
    + `?token=${token}&expires=${expiresUnix}&token_path=${encodeURIComponent(tokenPath)}`;
}

async function probe(url, label, withReferer) {
  const headers = { Accept: 'application/vnd.apple.mpegurl,*/*' };
  if (withReferer) {
    headers.Referer = `${APP_ORIGIN}/`;
    headers.Origin = APP_ORIGIN;
  }
  try {
    const res = await fetch(url, { headers, redirect: 'manual' });
    const bodyStart = (await res.text()).slice(0, 120).replace(/\s+/g, ' ');
    const cdnStatus = res.headers.get('cdn-requestid') ? '' : '';
    console.log(`  ${label.padEnd(22)} -> HTTP ${res.status} ${res.statusText}`);
    if (res.status !== 200) console.log(`     body: ${bodyStart}`);
    else console.log(`     ${bodyStart.startsWith('#EXTM3U') ? 'valid HLS manifest ✔' : 'body: ' + bodyStart}`);
    return res.status;
  } catch (e) {
    console.log(`  ${label.padEnd(22)} -> fetch error: ${e.message}`);
    return -1;
  }
}

const now = Math.floor(Date.now() / 1000);
const expires = now + 3600;
const tokenPath = `/${VIDEO_GUID}/`;
const token = computeCdnToken(TOKEN_KEY, tokenPath, expires);
const signedUrl = buildSignedPlaybackUrl(CDN_HOST, VIDEO_GUID, token, expires, tokenPath);
const unsignedUrl = `https://${CDN_HOST}/${VIDEO_GUID}/playlist.m3u8`;

console.log('=== Video Library playback diagnostic ===');
console.log('CDN host   :', CDN_HOST);
console.log('Video GUID :', VIDEO_GUID);
console.log('App origin :', APP_ORIGIN);
console.log('Token      : (signed, key hidden)');
console.log('');

// Optional: encode status straight from the Stream API (rules out "not encoded yet").
if (STREAM_KEY) {
  try {
    const r = await fetch(`https://video.bunnycdn.com/library/${LIBRARY_ID}/videos/${VIDEO_GUID}`,
      { headers: { AccessKey: STREAM_KEY, Accept: 'application/json' } });
    if (r.ok) {
      const v = await r.json();
      // Mirrors VideoLibraryAdminService.MapBunnyStatus: 3 AND 4 are both Ready/playable.
      const map = { 0: 'Uploading', 1: 'Processing', 2: 'Encoding', 3: 'Ready', 4: 'Ready', 5: 'Failed', 6: 'UploadFailed' };
      const ready = v.status === 3 || v.status === 4;
      console.log(`Encode status: ${v.status} = ${map[v.status] ?? '?'} (progress ${v.encodeProgress ?? '?'}%), resolutions: ${v.availableResolutions || 'none'}`);
      if (!ready) console.log('  ⚠ Not Ready — a non-ready video has no playlist.m3u8 (expect 404 below).');
      console.log('');
    } else {
      console.log(`Encode status lookup -> HTTP ${r.status} (check BUNNY_STREAM_KEY / BUNNY_LIBRARY_ID)\n`);
    }
  } catch (e) { console.log('Encode status lookup failed:', e.message, '\n'); }
}

console.log('Probing signed playlist.m3u8:');
const sWith = await probe(signedUrl, 'signed + referer', true);
const sNo = await probe(signedUrl, 'signed, NO referer', false);
console.log('Probing UNSIGNED (control — should 403 when token auth is on):');
await probe(unsignedUrl, 'unsigned + referer', true);

console.log('\n=== Verdict ===');
if (sWith === 200) {
  console.log('✔ Signed URL authorises WITH a referer. Token+signing are correct.');
  if (sNo !== 200) console.log('  → "Block direct url file access" needs your app allow-listed: Bunny → Security → General → Allowed domains → add *.oetwithdrhesham.co.uk. The WebView sends this referer, so playback will work with the block kept ON.');
  else console.log('  → CDN auth is fine even without a referer. If the app still shows black, it is client-side (CSP/hls.js).');
} else if (sWith === 404) {
  console.log('✖ 404 — the video is not finished encoding on Bunny yet (or the GUID is wrong). No token/referrer change will help until encoding completes. Check Bunny → Stream → Manage library for "Ready".');
} else if (sWith === 403) {
  console.log('✖ 403 even WITH a referer and a valid signature. Most likely causes, in order:');
  console.log('   1. The app is signing with a DIFFERENT token key than the library currently has (regenerated key). Set Admin → Settings → Bunny Stream → CDN token-auth key to the exact library key.');
  console.log('   2. "Block direct url file access" is on AND your app origin is not in Allowed domains (add *.oetwithdrhesham.co.uk).');
  console.log('   3. Token-auth propagation lag (wait ~60s and re-run).');
} else {
  console.log('? Unexpected result — see statuses above.');
}
