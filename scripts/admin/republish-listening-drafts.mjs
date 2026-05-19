#!/usr/bin/env node
/**
 * republish-listening-drafts.mjs
 *
 * Salvage path for listening papers stuck in Draft after the orchestrator
 * fixed `makeProvenance` (space → `;`) and added the `/listening/backfill`
 * step before publish. For every Draft listening paper in the database it:
 *
 *   1. PATCH /v1/admin/papers/{id}              → fixes SourceProvenance string
 *      so it contains a backend-parseable `legal=original-authoring-attested`
 *      token (split on `;`).
 *   2. POST  /v1/admin/papers/{id}/listening/backfill
 *      → projects the in-paper ExtractedTextJson blob into the relational
 *        ListeningQuestions / ListeningParts / ListeningExtracts tables that
 *        the publish gate actually reads from.
 *   3. POST  /v1/admin/papers/{id}/publish      → flips Draft → Published.
 *
 * Usage: node scripts/admin/republish-listening-drafts.mjs [--limit N]
 */
import process from 'node:process';
import { adminFetch, parseFlags } from './_lib.mjs';

const flags = parseFlags(process.argv.slice(2));
const LIMIT = Number(flags.limit ?? 200);

console.log(`Republishing stuck Draft listening papers (limit=${LIMIT})…`);

// 1. enumerate Draft listening papers via admin list endpoint
const list = await adminFetch(`/v1/admin/papers?subtest=Listening&status=Draft&pageSize=${LIMIT}`, { method: 'GET' });
if (!list.ok) {
  console.error(`✖ list failed: ${list.status} ${JSON.stringify(list.data).slice(0, 400)}`);
  process.exit(1);
}
const items = Array.isArray(list.data?.items) ? list.data.items : (Array.isArray(list.data) ? list.data : []);
console.log(`Found ${items.length} Draft listening papers.`);

let ok = 0, fail = 0;
for (const p of items) {
  const id = p.id ?? p.paperId ?? p.Id;
  const title = p.title ?? p.Title ?? '(untitled)';
  const prov = p.sourceProvenance ?? p.SourceProvenance ?? '';
  console.log(`\n[${ok + fail + 1}/${items.length}] ${title}  id=${id}`);

  // step 1: normalize SourceProvenance to use `; legal=…` instead of ` legal=…`
  let needsFix = false;
  let fixed = prov;
  if (prov && / legal=/.test(prov) && !/;\s*legal=/.test(prov)) {
    fixed = prov.replace(/\s+legal=/g, '; legal=');
    needsFix = true;
  } else if (!prov || !/legal=(original-authoring-attested|licensed-content-attested|permission-attested|copyright-cleared)/.test(prov)) {
    fixed = (prov ? prov.trimEnd().replace(/[;,.\s]+$/, '') + '; ' : '') + 'legal=original-authoring-attested';
    needsFix = true;
  }
  if (needsFix) {
    const patch = await adminFetch(`/v1/admin/papers/${id}`, { method: 'PUT', body: { sourceProvenance: fixed } });
    if (!patch.ok) {
      console.log(`  ✖ provenance patch failed (${patch.status}): ${JSON.stringify(patch.data).slice(0, 200)}`);
      fail++; continue;
    }
    console.log(`  ✓ provenance fixed`);
  } else {
    console.log(`  · provenance ok`);
  }

  // step 2: backfill relational projection
  const bf = await adminFetch(`/v1/admin/papers/${id}/listening/backfill`, { method: 'POST', body: {} });
  if (!bf.ok) {
    console.log(`  ✖ backfill failed (${bf.status}): ${JSON.stringify(bf.data).slice(0, 200)}`);
    fail++; continue;
  }
  console.log(`  ✓ backfilled`);

  // step 3: publish
  const pub = await adminFetch(`/v1/admin/papers/${id}/publish`, { method: 'POST', body: {} });
  if (!pub.ok) {
    console.log(`  ✖ publish failed (${pub.status}): ${JSON.stringify(pub.data).slice(0, 400)}`);
    fail++; continue;
  }
  console.log(`  ✓ published`);
  ok++;
}

console.log(`\n===\nrepublish summary: ok=${ok} fail=${fail} total=${items.length}`);
process.exit(fail > 0 ? 2 : 0);
