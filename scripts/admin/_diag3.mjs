import { adminFetch } from './_lib.mjs';
const r = await adminFetch('/v1/admin/papers/ace9585ac0974f52b27d453502352dc4');
const assets = r.data?.assets ?? [];
console.log('count:', assets.length);
for (const a of assets) {
  console.log(JSON.stringify({ id: a.id, role: a.role, roleType: typeof a.role, part: a.part, displayOrder: a.displayOrder, mediaAssetId: a.mediaAssetId }));
}
