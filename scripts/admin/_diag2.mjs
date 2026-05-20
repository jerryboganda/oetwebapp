import { getAccessToken, CONFIG } from '/opt/oetwebapp/scripts/admin/_lib.mjs';
const token = await getAccessToken({ force: true });
async function call(body, label) {
  const url = CONFIG.apiBase + '/v1/admin/papers/ace9585ac0974f52b27d453502352dc4/assets';
  const r = await fetch(url, {
    method: 'POST',
    headers: { 'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  const text = await r.text();
  console.log('[' + label + '] ' + r.status + ' body: ' + (text || '(empty)') + ' cid: ' + r.headers.get('x-correlation-id'));
}
await call({ role: 0, mediaAssetId: 'a4e78354fe12472193f086f43fef3968' }, 'numeric role 0');
await call({ role: 'Audio', mediaAssetId: 'a4e78354fe12472193f086f43fef3968' }, 'string role Audio');
await call({ Role: 'Audio', MediaAssetId: 'a4e78354fe12472193f086f43fef3968' }, 'PascalCase');
await call({ role: 'Audio' }, 'role only');
await call({ mediaAssetId: 'a4e78354fe12472193f086f43fef3968' }, 'mediaAssetId only');
await call({ role: 0 }, 'numeric role only');
await call({ role: 0, mediaAssetId: 'a4e78354fe12472193f086f43fef3968', displayOrder: 2, makePrimary: true }, 'numeric + extras');
await call({ role: 0, mediaAssetId: 'a4e78354fe12472193f086f43fef3968', part: 'A2', title: 'X', displayOrder: 2, makePrimary: true }, 'numeric + all');
