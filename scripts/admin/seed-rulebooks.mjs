/**
 * seed-rulebooks.mjs
 *
 * Walks rulebooks/<kind>/<profession>/rulebook.v*.json and imports each into
 * the backend's DB-managed rulebook table via:
 *
 *   POST /v1/admin/rulebooks/import   { json, mode: "create" | "replace" }
 *   POST /v1/admin/rulebooks/{id}/publish
 *
 * The on-disk JSON files remain the canonical source (embedded fallback used
 * by DbBackedRulebookLoader). Seeding to DB lets admins edit them in /admin/rulebooks
 * and is a no-op for the AI gateway when DB rows are absent.
 *
 * Usage:
 *   node scripts/admin/seed-rulebooks.mjs [--replace] [--publish] [--dry-run] [--only kind:profession]
 *
 *   --replace        Use mode="replace" instead of "create" (overwrites existing draft).
 *   --publish        Promote each imported rulebook to Published.
 *   --dry-run        Print what would be done, do not call admin API.
 *   --only k:p       Only seed one kind+profession pair (e.g. --only writing:medicine).
 *   --healthcheck    Run lib healthcheck and exit.
 */

import { readFileSync, readdirSync, statSync } from 'node:fs';
import { resolve, dirname, basename } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  CONFIG, parseFlags, startRun, endRun, adminFetch, logFailure,
  healthcheck, progress, sleep,
} from './_lib.mjs';

const flags = parseFlags();

const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const RULEBOOKS_DIR = resolve(REPO_ROOT, 'rulebooks');

function listRulebooks() {
  const out = [];
  if (!safeStat(RULEBOOKS_DIR)?.isDirectory()) {
    console.error(`Rulebooks dir not found: ${RULEBOOKS_DIR}`);
    return out;
  }
  for (const kind of readdirSync(RULEBOOKS_DIR)) {
    const kindDir = resolve(RULEBOOKS_DIR, kind);
    if (!safeStat(kindDir)?.isDirectory()) continue;
    if (kind === 'schema') continue;
    if (kind === 'remediation') continue; // remediation has different shape; admin-edited separately
    for (const profession of readdirSync(kindDir)) {
      const profDir = resolve(kindDir, profession);
      if (!safeStat(profDir)?.isDirectory()) continue;
      for (const file of readdirSync(profDir)) {
        if (!/^rulebook\.v\d+\.json$/i.test(file)) continue;
        out.push({ kind, profession, file: resolve(profDir, file) });
      }
    }
  }
  return out;
}

function safeStat(p) {
  try { return statSync(p); } catch { return null; }
}

async function main() {
  if (flags.healthcheck) {
    startRun('seed-rulebooks-healthcheck');
    const ok = await healthcheck();
    endRun({ ok });
    process.exit(ok ? 0 : 1);
  }

  const runId = startRun('seed-rulebooks');
  const all = listRulebooks();
  const filter = flags.only ? String(flags.only).toLowerCase() : null;
  const targets = filter
    ? all.filter(r => `${r.kind}:${r.profession}` === filter)
    : all;

  console.log(`Found ${all.length} rulebook files; processing ${targets.length}.`);

  const mode = flags.replace ? 'replace' : 'create';
  const publish = !!flags.publish;
  const dryRun = !!flags['dry-run'];

  let ok = 0, fail = 0, skipped = 0, published = 0;

  for (let i = 0; i < targets.length; i++) {
    const t = targets[i];
    const rel = `${t.kind}/${t.profession}/${basename(t.file)}`;
    console.log(progress(i + 1, targets.length, `${rel}`));

    let raw;
    try {
      raw = readFileSync(t.file, 'utf8');
      // Validate JSON parses + has required envelope.
      const parsed = JSON.parse(raw);
      if (!parsed.kind || !parsed.profession || !parsed.version) {
        throw new Error(`missing kind/profession/version in JSON`);
      }
    } catch (e) {
      logFailure('rulebook-read', rel, e);
      fail++;
      continue;
    }

    if (dryRun) {
      console.log(`  (dry-run) would POST /v1/admin/rulebooks/import  mode=${mode}`);
      if (publish) console.log(`  (dry-run) would POST /v1/admin/rulebooks/{id}/publish`);
      skipped++;
      continue;
    }

    // Import.
    const imp = await adminFetch('/v1/admin/rulebooks/import', {
      method: 'POST',
      body: { json: raw, mode },
    });
    if (!imp.ok) {
      // 409 / "already exists" is acceptable under mode=create — try replace if --replace not already set.
      const errMsg = JSON.stringify(imp.data).slice(0, 400);
      if (!flags.replace && (imp.status === 409 || /exists|duplicate/i.test(errMsg))) {
        console.log(`  ⓘ already exists; skipping import (use --replace to overwrite)`);
        skipped++;
        // Still attempt to publish if requested — need to find existing id.
        if (publish) {
          const list = await adminFetch('/v1/admin/rulebooks', { query: { kind: t.kind, profession: t.profession } });
          const existing = Array.isArray(list.data) ? list.data : (list.data?.items || []);
          // Prefer matching version, else most recent.
          const parsedVersion = JSON.parse(raw).version;
          const match = existing.find(r => r.version === parsedVersion) || existing[0];
          if (match?.id) {
            const pub = await adminFetch(`/v1/admin/rulebooks/${match.id}/publish`, { method: 'POST', body: {} });
            if (pub.ok) {
              console.log(`  ✓ published existing ${match.id}`);
              published++;
            } else if (pub.status !== 409 && pub.status !== 400) {
              logFailure('rulebook-publish-existing', { rel, id: match.id }, new Error(`${pub.status}: ${JSON.stringify(pub.data).slice(0, 200)}`));
            }
          }
        }
        continue;
      }
      logFailure('rulebook-import', rel, new Error(`${imp.status}: ${errMsg}`));
      fail++;
      continue;
    }
    const id = imp.data?.id || imp.data?.rulebookId;
    if (!id) {
      logFailure('rulebook-import', rel, new Error(`no id in response: ${JSON.stringify(imp.data).slice(0, 200)}`));
      fail++;
      continue;
    }
    console.log(`  ✓ imported ${id}  status=${imp.data?.status || '?'}`);
    ok++;

    if (publish) {
      // Backend rejects publishing a rulebook with zero rules; our JSON files all have rules so this is fine.
      const pub = await adminFetch(`/v1/admin/rulebooks/${id}/publish`, { method: 'POST', body: {} });
      if (!pub.ok) {
        logFailure('rulebook-publish', { rel, id }, new Error(`${pub.status}: ${JSON.stringify(pub.data).slice(0, 200)}`));
      } else {
        published++;
        console.log(`  ✓ published ${id}`);
      }
    }

    // Tiny pause to be polite even though the bucket throttles us.
    await sleep(75);
  }

  endRun({ targets: targets.length, imported: ok, published, skipped, failed: fail });
  process.exit(fail > 0 && ok === 0 ? 1 : 0);
}

main().catch(e => {
  console.error(e);
  process.exit(2);
});
