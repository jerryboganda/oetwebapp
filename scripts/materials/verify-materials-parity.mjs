#!/usr/bin/env node
// ─────────────────────────────────────────────────────────────────────────────
// Fail-proof Materials parity verifier — SHA256, byte-for-byte, no guessing.
//
// Proves that EVERY file in the on-disk Materials tree exists in production with
// IDENTICAL bytes, and reports anything in production that does not correspond to
// a disk file. Reconciliation is by content hash only — filenames and sizes are
// never trusted, so a rename, a re-encode, or a same-size-but-different file
// cannot hide a discrepancy.
//
// Guarantees enforced:
//   • Every disk file's SHA256 is present in production        → nothing missing
//   • No file has been split or transcoded on the way to prod  → derived copies
//     (e.g. a 197 MB WAV stored as a 35 MB MP3, or one PDF stored as N parts)
//     surface as MISSING(original) + FOREIGN(derived), never as a silent match
//   • Exit code 0 only when the disk is fully covered by prod  → CI/gate friendly
//
// This is a READ-ONLY audit. It never writes to the database or storage.
//
// ── Inputs ───────────────────────────────────────────────────────────────────
//   MATERIALS_SRC_ROOT   default "<repo>/OET Materials & Videos Data/Materials"
//   --manifest <file>    production manifest, one row per MaterialFile:
//                          sha256|sizeBytes|folderPath|title|status
//                        Default: scripts/materials/prod-manifest.txt (committed
//                        snapshot). Regenerate the live manifest with:
//
//     ssh vps "docker exec oet-postgres psql -U oet_learner -d oet_learner -t -A -F'|' -c \"
//       WITH RECURSIVE t AS (
//         SELECT \"Id\", \"Name\"::text AS path FROM \"MaterialFolders\" WHERE \"ParentFolderId\" IS NULL
//         UNION ALL
//         SELECT f.\"Id\", t.path||'/'||f.\"Name\" FROM \"MaterialFolders\" f JOIN t ON f.\"ParentFolderId\"=t.\"Id\"
//       )
//       SELECT LOWER(m.\"Sha256\"), m.\"SizeBytes\", COALESCE(t.path,'(root)'), mf.\"Title\", mf.\"Status\"
//       FROM \"MaterialFiles\" mf
//       LEFT JOIN t ON mf.\"FolderId\"=t.\"Id\"
//       LEFT JOIN \"MediaAssets\" m ON mf.\"MediaAssetId\"=m.\"Id\";\"" > scripts/materials/prod-manifest.txt
//
// ── Flags ────────────────────────────────────────────────────────────────────
//   --manifest <file>    override the production manifest path
//   --json               emit a machine-readable report to stdout
//   --include-archived   also count Archived (status 6) prod rows as coverage
//                        (default: archived rows are ignored — they are invisible
//                        to learners, so they cannot satisfy a disk file)
//
// ── Usage ────────────────────────────────────────────────────────────────────
//   node scripts/materials/verify-materials-parity.mjs
//   node scripts/materials/verify-materials-parity.mjs --manifest /tmp/live.txt --json
//
// Exit 0 = every disk file is present in prod. Exit 1 = at least one is missing.
// ─────────────────────────────────────────────────────────────────────────────

import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { readFile, readdir, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import os from 'node:os';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(HERE, '..', '..');

const argv = process.argv.slice(2);
const flag = (name) => argv.includes(name);
const opt = (name, fallback) => {
  const i = argv.indexOf(name);
  return i >= 0 && argv[i + 1] ? argv[i + 1] : fallback;
};

const SRC_ROOT = process.env.MATERIALS_SRC_ROOT
  || path.join(REPO_ROOT, 'OET Materials & Videos Data', 'Materials');
const MANIFEST = path.resolve(opt('--manifest', path.join(HERE, 'prod-manifest.txt')));
const AS_JSON = flag('--json');
const INCLUDE_ARCHIVED = flag('--include-archived');
const ARCHIVED_STATUS = '6';

// General-English root PDFs live loose on disk but are grouped under a
// "Basic English Course" folder in production. Mirror that so the reconciliation
// compares like-for-like (content hash is unaffected either way).
const ROOT_FILE_FOLDER = 'Basic English Course';

const CONCURRENCY = Math.max(2, Math.min(8, (os.cpus()?.length ?? 4) - 1));

function sha256File(file) {
  return new Promise((resolve, reject) => {
    const hash = createHash('sha256');
    createReadStream(file)
      .on('error', reject)
      .on('data', (d) => hash.update(d))
      .on('end', () => resolve(hash.digest('hex')));
  });
}

async function walk(dir) {
  const out = [];
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...(await walk(full)));
    else if (entry.isFile()) out.push(full);
  }
  return out;
}

// Map an absolute disk path to the folder path prod uses (POSIX separators,
// loose root files grouped under ROOT_FILE_FOLDER).
function diskFolderOf(absFile) {
  const rel = path.relative(SRC_ROOT, absFile).split(path.sep).join('/');
  const slash = rel.lastIndexOf('/');
  return slash === -1 ? ROOT_FILE_FOLDER : rel.slice(0, slash);
}

async function hashAll(files) {
  const results = new Array(files.length);
  let next = 0;
  async function worker() {
    while (next < files.length) {
      const i = next++;
      results[i] = { file: files[i], sha: await sha256File(files[i]) };
    }
  }
  await Promise.all(Array.from({ length: CONCURRENCY }, worker));
  return results;
}

function parseManifest(text) {
  const rows = [];
  for (const line of text.split('\n')) {
    if (!line.trim()) continue;
    const [sha, size, folder, title, status] = line.split('|');
    if (!sha) continue;
    rows.push({
      sha: sha.toLowerCase(),
      size: Number(size),
      folder,
      title,
      status: (status ?? '').trim(),
    });
  }
  return rows;
}

async function main() {
  await stat(SRC_ROOT).catch(() => {
    console.error(`✖ Materials source not found: ${SRC_ROOT}`);
    console.error('  Set MATERIALS_SRC_ROOT to the on-disk Materials tree.');
    process.exit(2);
  });

  const [files, manifestText] = await Promise.all([
    walk(SRC_ROOT),
    readFile(MANIFEST, 'utf8').catch(() => {
      console.error(`✖ Manifest not found: ${MANIFEST}`);
      process.exit(2);
    }),
  ]);

  const prodRows = parseManifest(manifestText);
  const prodActive = prodRows.filter((r) => INCLUDE_ARCHIVED || r.status !== ARCHIVED_STATUS);
  const prodBySha = new Set(prodActive.map((r) => r.sha));
  const diskHashes = await hashAll(files);
  const diskBySha = new Map();
  for (const { file, sha } of diskHashes) {
    if (!diskBySha.has(sha)) diskBySha.set(sha, []);
    diskBySha.get(sha).push(file);
  }

  // MISSING: disk content whose SHA is absent from (active) prod.
  const missing = [];
  for (const { file, sha } of diskHashes) {
    if (!prodBySha.has(sha)) {
      missing.push({ sha, disk: path.relative(SRC_ROOT, file).split(path.sep).join('/'), folder: diskFolderOf(file) });
    }
  }
  // FOREIGN: active prod content whose SHA is absent from disk.
  const foreign = [];
  for (const r of prodActive) {
    if (!diskBySha.has(r.sha)) {
      foreign.push({ sha: r.sha, prod: `${r.folder}/${r.title}`, size: r.size, status: r.status });
    }
  }

  const matchedDisk = diskHashes.length - missing.length;
  const archivedCount = prodRows.length - prodActive.length;

  const report = {
    ok: missing.length === 0,
    diskFiles: diskHashes.length,
    diskDistinct: diskBySha.size,
    prodRows: prodRows.length,
    prodActive: prodActive.length,
    prodArchivedIgnored: INCLUDE_ARCHIVED ? 0 : archivedCount,
    matchedDisk,
    missing,
    foreign,
  };

  if (AS_JSON) {
    console.log(JSON.stringify(report, null, 2));
    process.exit(report.ok ? 0 : 1);
  }

  const line = '─'.repeat(70);
  console.log(line);
  console.log('  MATERIALS PARITY — SHA256 byte-for-byte reconciliation');
  console.log(line);
  console.log(`  Disk files hashed        : ${report.diskFiles}  (${report.diskDistinct} distinct by content)`);
  console.log(`  Prod rows                : ${report.prodRows}  (${report.prodActive} active${report.prodArchivedIgnored ? `, ${report.prodArchivedIgnored} archived ignored` : ''})`);
  console.log(`  Disk files present in prod: ${report.matchedDisk}/${report.diskFiles}`);
  console.log(line);

  if (missing.length === 0) {
    console.log('  ✓ PASS — every disk file is present in production, byte-identical.');
  } else {
    console.log(`  ✖ FAIL — ${missing.length} disk file(s) NOT present in production:`);
    for (const m of missing) console.log(`      MISSING  ${m.disk}`);
  }

  if (foreign.length > 0) {
    console.log(line);
    console.log(`  ⚠ ${foreign.length} active prod file(s) with no matching disk content`);
    console.log('    (a re-encode or a split turns 1 disk file into these — re-upload the');
    console.log('     original whole and archive these to restore exact parity):');
    for (const f of foreign) console.log(`      FOREIGN  ${f.prod}  (${f.size} bytes)`);
  }
  console.log(line);
  process.exit(report.ok ? 0 : 1);
}

main().catch((err) => {
  console.error('✖ verifier crashed:', err);
  process.exit(2);
});
