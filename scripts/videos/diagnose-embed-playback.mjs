#!/usr/bin/env node
/**
 * Video Library EMBED playback diagnostic (current scheme, shipped 2026-07-29).
 *
 * Reproduces the backend's Bunny embed-view-token signing EXACTLY
 * (BunnyStreamClient.ComputeEmbedToken / BuildSignedEmbedUrl), then probes the
 * real signed embed URL so you can see Bunny's actual response without ever
 * sharing the API key with anyone else. Nothing is written anywhere; the key
 * never leaves this machine.
 *
 * Run (PowerShell):
 *
 *   $env:BUNNY_LIBRARY_ID = "696416"
 *   $env:BUNNY_API_KEY = "<Stream -> library 696416 -> Security -> General -> Token authentication key (or Library -> API)>"
 *   $env:BUNNY_VIDEO_GUID = "<paste a real video GUID from Bunny -> Manage library -> click any video>"
 *   node scripts/videos/diagnose-embed-playback.mjs
 *
 * Run (bash):
 *
 *   BUNNY_LIBRARY_ID=696416 BUNNY_API_KEY=... BUNNY_VIDEO_GUID=... \
 *     node scripts/videos/diagnose-embed-playback.mjs
 */
import { createHash } from 'node:crypto';

const LIBRARY_ID = (process.env.BUNNY_LIBRARY_ID || '').trim();
const API_KEY = (process.env.BUNNY_API_KEY || '').trim();
const VIDEO_GUID = (process.env.BUNNY_VIDEO_GUID || '').trim();

if (!LIBRARY_ID || !API_KEY || !VIDEO_GUID) {
  console.error('Missing required env. Need BUNNY_LIBRARY_ID, BUNNY_API_KEY, BUNNY_VIDEO_GUID.');
  console.error('Get the video GUID from Bunny -> Stream -> your library -> Manage library -> click any video (the GUID is in its URL / Video ID field).');
  process.exit(2);
}

/** Byte-for-byte mirror of BunnyStreamClient.ComputeEmbedToken. */
function computeEmbedToken(apiKey, videoId, expiresUnix) {
  const payload = `${apiKey}${videoId}${expiresUnix}`;
  return createHash('sha256').update(payload, 'utf8').digest('hex');
}

/** Byte-for-byte mirror of BunnyStreamClient.BuildSignedEmbedUrl. */
function buildSignedEmbedUrl(libraryId, videoId, token, expiresUnix) {
  return `https://iframe.mediadelivery.net/embed/${encodeURIComponent(libraryId)}/${encodeURIComponent(videoId)}`
    + `?token=${token}&expires=${expiresUnix}&autoplay=false&preload=true`;
}

const now = Math.floor(Date.now() / 1000);
const expires = now + 300; // matches production's clamped-to-300s minimum TTL
const token = computeEmbedToken(API_KEY, VIDEO_GUID, expires);
const signedUrl = buildSignedEmbedUrl(LIBRARY_ID, VIDEO_GUID, token, expires);
const badTokenUrl = buildSignedEmbedUrl(LIBRARY_ID, VIDEO_GUID, '0'.repeat(64), expires);
const noTokenUrl = `https://iframe.mediadelivery.net/embed/${encodeURIComponent(LIBRARY_ID)}/${encodeURIComponent(VIDEO_GUID)}`;

console.log('=== Video Library EMBED playback diagnostic ===');
console.log('Library ID :', LIBRARY_ID);
console.log('Video GUID :', VIDEO_GUID);
console.log('Expires    :', expires, `(${new Date(expires * 1000).toISOString()})`);
console.log('Token      : (signed, key hidden)');
console.log('');

async function probe(url, label) {
  try {
    const res = await fetch(url, { redirect: 'manual' });
    const body = (await res.text()).slice(0, 300).replace(/\s+/g, ' ');
    console.log(`  ${label.padEnd(28)} -> HTTP ${res.status} ${res.statusText}`);
    console.log(`     body: ${body}`);
    return res.status;
  } catch (e) {
    console.log(`  ${label.padEnd(28)} -> fetch error: ${e.message}`);
    return -1;
  }
}

console.log('Probing:');
const withCorrectToken = await probe(signedUrl, 'correctly signed');
await probe(badTokenUrl, 'deliberately WRONG token');
await probe(noTokenUrl, 'no token at all');

console.log('\n=== Verdict ===');
if (withCorrectToken === 200) {
  console.log('✔ The correctly signed embed URL returns 200. Our signing scheme and stored key are correct.');
  console.log('  → The 403 learners see must be coming from somewhere else (a different key/library actually');
  console.log('    live in production than what you just tested, expiry/clock skew on the server, or the');
  console.log('    request never reaching this URL at all). Compare this key/library ID against exactly what');
  console.log('    Admin -> Settings -> Bunny Stream has saved right now.');
} else if (withCorrectToken === 403) {
  console.log('✖ 403 even with a correctly-signed token. Since "wrong token" and "correct token" likely both');
  console.log('  403 the same way, this points at something beyond signature validity — e.g. the account/library');
  console.log('  security policy still bouncing embed requests for another reason (e.g. MediaCage DRM licensing,');
  console.log('  or an account-level restriction not visible on the Security -> General page). Worth a support');
  console.log('  ticket to Bunny with this exact URL and asking them what specifically is rejecting it.');
} else if (withCorrectToken === 404) {
  console.log('✖ 404 — this video GUID does not exist in this library (or is not finished encoding). Try another');
  console.log('  video GUID copied fresh from Bunny -> Manage library.');
} else {
  console.log('? Unexpected result — see statuses above.');
}
