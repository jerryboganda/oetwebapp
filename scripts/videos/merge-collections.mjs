#!/usr/bin/env node
// Consolidate duplicate Bunny Stream collections that name the SAME category
// (module + language + content-type) but were split across differently-ordered
// or over-nested folder names. Reads the LIVE library (source of truth), moves
// every video from the sibling folders into one canonical folder, then deletes
// the emptied siblings. No video is ever deleted — only re-homed.
//
// Scope is intentionally narrow — only the families listed in TARGETS below are
// touched (Reading/Arabic/Workshops and Listening/Arabic/Workshops). It never
// merges across a different module, language, content-type, or a non-empty
// profession segment, so lookalike-but-distinct folders stay separate.
//
// Secrets are read from env only (never hard-coded / committed):
//   BUNNY_STREAM_KEY   the OET Video Library key (preferred), OR
//   BUNNY_ACCOUNT_KEY  Bunny account (master) key -> used to fetch the library key
//   BUNNY_LIBRARY_ID   defaults to 696416
//
// Usage:
//   node scripts/videos/merge-collections.mjs            # DRY RUN — prints the plan, writes nothing
//   node scripts/videos/merge-collections.mjs --apply    # execute moves + delete emptied folders

const LIBRARY_ID = process.env.BUNNY_LIBRARY_ID || '696416';
const ACCOUNT_KEY = process.env.BUNNY_ACCOUNT_KEY || '';
let STREAM_KEY = process.env.BUNNY_STREAM_KEY || '';

const API = 'https://video.bunnycdn.com';
const ACCOUNT_API = 'https://api.bunny.net';

const APPLY = process.argv.includes('--apply');

// Same classification vocab as upload-videos-to-bunny.mjs (case-insensitive).
const PROFESSIONS = ['Medicine', 'Nursing', 'Pharmacy', 'Radiography', 'Dentistry'];
const LANGUAGES = ['Arabic', 'English'];
const CONTENT_TYPES = ['Sessions', 'Workshops'];

// The ONLY families this script will consolidate. `canonical` is the folder name
// everything lands under (created or reused).
const TARGETS = [
  { module: 'Reading', language: 'Arabic', contentType: 'Workshops', canonical: 'Reading / Workshops / Arabic' },
  { module: 'Listening', language: 'Arabic', contentType: 'Workshops', canonical: 'Listening / Workshops / Arabic' },
];

const cleanSeg = (s) => s.replace(/[\s_]+$/g, '').replace(/\s{2,}/g, ' ').trim();

/** Classify a collection name into {module, profession, language, contentType}. */
function classify(name) {
  const segs = name.split('/').map(cleanSeg).filter(Boolean);
  const findIn = (vocab) => {
    for (const seg of segs) {
      const hit = vocab.find((v) => v.toLowerCase() === seg.toLowerCase());
      if (hit) return hit;
    }
    return null;
  };
  return {
    module: segs[0] || null,
    profession: findIn(PROFESSIONS),
    language: findIn(LANGUAGES),
    contentType: findIn(CONTENT_TYPES),
  };
}

async function resolveStreamKey() {
  if (STREAM_KEY) return STREAM_KEY;
  if (!ACCOUNT_KEY) throw new Error('Set BUNNY_STREAM_KEY (or BUNNY_ACCOUNT_KEY) in the environment.');
  const res = await fetch(`${ACCOUNT_API}/videolibrary/${LIBRARY_ID}`, {
    headers: { AccessKey: ACCOUNT_KEY, Accept: 'application/json' },
  });
  if (!res.ok) throw new Error(`Failed to fetch library ${LIBRARY_ID}: HTTP ${res.status}`);
  const j = await res.json();
  STREAM_KEY = j.ApiKey;
  if (!STREAM_KEY) throw new Error('Library response had no ApiKey.');
  return STREAM_KEY;
}

async function bunny(method, urlPath, body) {
  const res = await fetch(`${API}${urlPath}`, {
    method,
    headers: {
      AccessKey: STREAM_KEY,
      Accept: 'application/json',
      ...(body ? { 'Content-Type': 'application/json' } : {}),
    },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
  const text = await res.text();
  if (!res.ok) throw new Error(`${method} ${urlPath} -> HTTP ${res.status}: ${text.slice(0, 300)}`);
  return text ? JSON.parse(text) : {};
}

async function listCollections() {
  const out = [];
  let page = 1;
  for (;;) {
    const j = await bunny('GET', `/library/${LIBRARY_ID}/collections?page=${page}&itemsPerPage=100`);
    for (const it of j.items || []) out.push({ guid: it.guid, name: it.name, videoCount: it.videoCount ?? 0 });
    const total = j.totalItems ?? (j.items ? j.items.length : 0);
    if (page * 100 >= total || !(j.items || []).length) break;
    page++;
  }
  return out;
}

async function listCollectionVideos(guid) {
  const out = [];
  let page = 1;
  for (;;) {
    const j = await bunny('GET', `/library/${LIBRARY_ID}/videos?collection=${encodeURIComponent(guid)}&page=${page}&itemsPerPage=100`);
    for (const it of j.items || []) out.push({ guid: it.guid, title: it.title });
    const total = j.totalItems ?? (j.items ? j.items.length : 0);
    if (page * 100 >= total || !(j.items || []).length) break;
    page++;
  }
  return out;
}

const moveVideo = (videoGuid, collectionId) =>
  bunny('POST', `/library/${LIBRARY_ID}/videos/${encodeURIComponent(videoGuid)}`, { collectionId });
const createCollection = (name) =>
  bunny('POST', `/library/${LIBRARY_ID}/collections`, { name });
const renameCollection = (guid, name) =>
  bunny('POST', `/library/${LIBRARY_ID}/collections/${encodeURIComponent(guid)}`, { name });
const deleteCollection = (guid) =>
  bunny('DELETE', `/library/${LIBRARY_ID}/collections/${encodeURIComponent(guid)}`);

async function main() {
  console.log(`\n${APPLY ? '⚠  APPLY MODE — writing to Bunny' : '🔎 DRY RUN — no changes will be written'}`);
  console.log(`Library ${LIBRARY_ID}\n`);
  await resolveStreamKey();

  const collections = await listCollections();
  console.log(`Live library has ${collections.length} collections.\n`);

  let plannedMoves = 0;
  let plannedDeletes = 0;

  for (const target of TARGETS) {
    // Match by category, and only when there's no profession segment (these
    // families have none). A profession would mark genuinely distinct content.
    const matched = collections.filter((c) => {
      const k = classify(c.name);
      return (
        k.module?.toLowerCase() === target.module.toLowerCase() &&
        k.language?.toLowerCase() === target.language.toLowerCase() &&
        k.contentType?.toLowerCase() === target.contentType.toLowerCase() &&
        k.profession == null
      );
    });

    console.log('─'.repeat(72));
    console.log(`Family: ${target.canonical}`);
    if (matched.length === 0) {
      console.log('  No matching collections found — skipping.\n');
      continue;
    }

    // Load each matched collection's videos (live membership).
    const withVideos = [];
    for (const c of matched) {
      const vids = await listCollectionVideos(c.guid);
      withVideos.push({ ...c, videos: vids });
    }

    // Pick the canonical target: an exact-name match if present, else the
    // matched folder with the most videos (renamed to the canonical name).
    let targetCol = withVideos.find((c) => c.name === target.canonical) || null;
    let willRename = null;
    let willCreate = false;
    if (!targetCol) {
      const biggest = [...withVideos].sort((a, b) => b.videos.length - a.videos.length)[0];
      if (biggest && biggest.videos.length > 0) {
        targetCol = biggest;
        willRename = { from: biggest.name, to: target.canonical };
      } else {
        willCreate = true; // no usable existing folder — create a fresh canonical
      }
    }

    const sources = withVideos.filter((c) => c !== targetCol);
    const totalIncoming = sources.reduce((n, c) => n + c.videos.length, 0);

    console.log(`  Matched ${withVideos.length} folders:`);
    for (const c of withVideos) {
      const role = c === targetCol ? 'KEEP' : 'merge→';
      console.log(`    [${role}] "${c.name}"  (${c.videos.length} videos)`);
      for (const v of c.videos) console.log(`             · ${v.title}`);
    }
    if (willCreate) console.log(`  → will CREATE canonical folder "${target.canonical}"`);
    if (willRename) console.log(`  → will RENAME "${willRename.from}" → "${willRename.to}"`);
    console.log(`  → will MOVE ${totalIncoming} videos into "${target.canonical}"`);
    console.log(`  → will DELETE ${sources.length} emptied folder(s)\n`);

    plannedMoves += totalIncoming;
    plannedDeletes += sources.length;

    if (!APPLY) continue;

    // ── Execute ──
    let targetGuid;
    if (targetCol) {
      targetGuid = targetCol.guid;
      if (willRename) {
        await renameCollection(targetGuid, target.canonical);
        console.log(`    renamed → "${target.canonical}"`);
      }
    } else {
      const created = await createCollection(target.canonical);
      targetGuid = created.guid;
      if (!targetGuid) throw new Error(`Create returned no guid for "${target.canonical}"`);
      console.log(`    created "${target.canonical}" (${targetGuid})`);
    }

    for (const src of sources) {
      for (const v of src.videos) {
        await moveVideo(v.guid, targetGuid);
        console.log(`    moved "${v.title}" → ${target.canonical}`);
      }
    }

    // Only delete folders we just emptied (re-verify live count == 0).
    for (const src of sources) {
      const remaining = await listCollectionVideos(src.guid);
      if (remaining.length === 0) {
        await deleteCollection(src.guid);
        console.log(`    deleted empty folder "${src.name}"`);
      } else {
        console.log(`    ⚠ kept "${src.name}" — still has ${remaining.length} video(s), NOT deleted`);
      }
    }
    console.log('');
  }

  console.log('─'.repeat(72));
  console.log(
    APPLY
      ? `Done. Moved ${plannedMoves} videos, deleted ${plannedDeletes} folders.`
      : `Plan: move ${plannedMoves} videos, delete ${plannedDeletes} folders. Re-run with --apply to execute.`,
  );
}

main().catch((err) => {
  console.error('\n✖ Merge failed:', err.message);
  process.exit(1);
});
