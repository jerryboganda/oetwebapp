// Diagnostic: hit /v1/admin/papers/{id}/assets and dump full response.
import { getAccessToken, CONFIG } from '/opt/oetwebapp/scripts/admin/_lib.mjs';

const PAPER = process.argv[2] || 'ace9585ac0974f52b27d453502352dc4';
const MEDIA = process.argv[3]; // required: a recently-uploaded mediaAssetId

const token = await getAccessToken({ force: true });
console.log('apiBase:', CONFIG.apiBase);
console.log('token len:', token.length);

async function call(body, label) {
  const r = await fetch(`${CONFIG.apiBase}/v1/admin/papers/${PAPER}/assets`, {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json',
      'Accept': 'application/json',
    },
    body: JSON.stringify(body),
  });
  const text = await r.text();
  console.log(`\n=== ${label} ===`);
  console.log('status:', r.status, r.statusText);
  console.log('headers:', JSON.stringify(Object.fromEntries(r.headers.entries()), null, 2));
  console.log('body:', text || '(empty)');
}

if (!MEDIA) {
  console.log('Skipping body call; pass mediaAssetId as arg2');
} else {
  await call({
    role: 'Audio',
    mediaAssetId: MEDIA,
    part: 'A2',
    title: 'Listening Part A2',
    displayOrder: 2,
    makePrimary: true,
  }, 'normal attach');
}

await call({}, 'empty body');
await call({ role: 'Audio' }, 'missing mediaAssetId');
