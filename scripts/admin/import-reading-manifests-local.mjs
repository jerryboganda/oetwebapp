#!/usr/bin/env node

import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const DEFAULT_API_BASE = 'http://localhost:8080';
const DEFAULT_CHUNK_SIZE_BYTES = 2 * 1024 * 1024;
const PAPER_ASSET_ROLE_VALUE = new Map([
  ['Audio', 0],
  ['QuestionPaper', 1],
  ['AudioScript', 2],
  ['AnswerKey', 3],
  ['CaseNotes', 4],
  ['ModelAnswer', 5],
  ['RoleCard', 6],
  ['AssessmentCriteria', 7],
  ['WarmUpQuestions', 8],
  ['Supplementary', 99],
]);

const scriptPath = fileURLToPath(import.meta.url);
const workspaceRoot = path.resolve(path.dirname(scriptPath), '..', '..');

function parseArgs(argv) {
  const result = {};
  for (let index = 0; index < argv.length; index += 1) {
    const item = argv[index];
    if (!item.startsWith('--')) continue;
    const key = item.slice(2);
    const next = argv[index + 1];
    if (!next || next.startsWith('--')) {
      result[key] = true;
      continue;
    }
    result[key] = next;
    index += 1;
  }
  return result;
}

function requireText(value, label) {
  if (typeof value !== 'string' || value.trim().length === 0) {
    throw new Error(`${label} is required.`);
  }
  return value.trim();
}

function assertLocalApi(apiBase) {
  const url = new URL(apiBase);
  const localHosts = new Set(['localhost', '127.0.0.1', '::1']);
  if (!localHosts.has(url.hostname)) {
    throw new Error(
      `Refusing to import content into non-local API '${apiBase}'. Use localhost Docker only.`,
    );
  }
}

async function loadManifestBundle(manifestPath) {
  const resolvedPath = path.resolve(workspaceRoot, manifestPath);
  const extension = path.extname(resolvedPath).toLowerCase();
  if (extension === '.mjs' || extension === '.js') {
    const module = await import(pathToFileURL(resolvedPath).href);
    return { bundle: module.default ?? module.bundle, baseDir: path.dirname(resolvedPath), resolvedPath };
  }
  const raw = await fs.readFile(resolvedPath, 'utf8');
  return { bundle: JSON.parse(raw), baseDir: path.dirname(resolvedPath), resolvedPath };
}

async function apiRequest(apiBase, route, { method = 'GET', body, token, headers } = {}) {
  let response;
  try {
    response = await fetch(`${apiBase}${route}`, {
      method,
      body: body === undefined ? undefined : typeof body === 'string' || body instanceof Uint8Array ? body : JSON.stringify(body),
      headers: {
        Accept: 'application/json',
        ...(body !== undefined && !(body instanceof Uint8Array) ? { 'Content-Type': 'application/json' } : {}),
        ...(body instanceof Uint8Array ? { 'Content-Type': 'application/octet-stream' } : {}),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(headers ?? {}),
      },
    });
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    throw new Error(`${method} ${route} network error: ${message}`, { cause: error });
  }
  if (!response.ok) {
    const text = await readResponseText(response, method, route);
    throw new Error(`${method} ${route} failed with HTTP ${response.status}: ${text}`);
  }
  if (response.status === 204) return undefined;
  const text = await readResponseText(response, method, route);
  return text ? JSON.parse(text) : undefined;
}

async function readResponseText(response, method, route) {
  try {
    return await response.text();
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    throw new Error(`${method} ${route} response read error: ${message}`, { cause: error });
  }
}

async function signIn(apiBase, email, password) {
  const response = await apiRequest(apiBase, '/v1/auth/sign-in', {
    method: 'POST',
    body: { email, password },
  });
  return requireText(response.accessToken, 'accessToken');
}

async function listReadingPapers(apiBase, token) {
  return apiRequest(apiBase, '/v1/admin/papers?subtest=reading&pageSize=200', { token });
}

async function ensureDraftForReplacement(apiBase, token, paper) {
  if (paper.status === 'Draft') return;
  if (paper.status === 'Archived') {
    throw new Error(`Paper '${paper.slug}' is archived and cannot be replaced in place.`);
  }
  await apiRequest(apiBase, `/v1/admin/papers/${paper.id}/unpublish`, {
    method: 'POST',
    token,
  });
}

function resolveSourcePath(baseDir, sourcePath) {
  const resolvedPath = path.isAbsolute(sourcePath)
    ? sourcePath
    : path.resolve(baseDir, sourcePath);
  return resolvedPath;
}

function paperAssetRoleValue(role) {
  const value = PAPER_ASSET_ROLE_VALUE.get(role);
  if (value === undefined) throw new Error(`Unsupported PaperAssetRole '${role}'.`);
  return value;
}

async function uploadFile(apiBase, token, sourcePath, intendedRole) {
  const stat = await fs.stat(sourcePath);
  const fileName = path.basename(sourcePath);
  const start = await apiRequest(apiBase, '/v1/admin/uploads', {
    method: 'POST',
    token,
    body: {
      originalFilename: fileName,
      declaredMimeType: 'application/pdf',
      declaredSizeBytes: stat.size,
      intendedRole,
    },
  });
  const fileBuffer = await fs.readFile(sourcePath);
  const chunkSize = start.chunkSizeBytes || DEFAULT_CHUNK_SIZE_BYTES;
  const totalParts = Math.max(1, Math.ceil(fileBuffer.length / chunkSize));
  for (let partNumber = 1; partNumber <= totalParts; partNumber += 1) {
    const startOffset = (partNumber - 1) * chunkSize;
    const chunk = fileBuffer.subarray(startOffset, Math.min(fileBuffer.length, startOffset + chunkSize));
    await apiRequest(apiBase, `/v1/admin/uploads/${start.uploadId}/parts/${partNumber}`, {
      method: 'PUT',
      token,
      body: chunk,
    });
  }
  return apiRequest(apiBase, `/v1/admin/uploads/${start.uploadId}/complete`, {
    method: 'POST',
    token,
  });
}

async function replaceAssets(apiBase, token, paper, paperConfig, baseDir) {
  const latestPaper = await apiRequest(apiBase, `/v1/admin/papers/${paper.id}`, { token });
  const existingAssets = Array.isArray(latestPaper.assets) ? latestPaper.assets : [];
  const mediaByPath = new Map();
  const attached = [];

  for (const asset of paperConfig.assets ?? []) {
    const role = requireText(asset.role, 'asset.role');
    const sourcePath = resolveSourcePath(baseDir, requireText(asset.sourcePath, 'asset.sourcePath'));
    for (const existing of existingAssets) {
      if (existing.role === role && (existing.part ?? null) === (asset.part ?? null)) {
        await apiRequest(apiBase, `/v1/admin/papers/${paper.id}/assets/${existing.id}`, {
          method: 'DELETE',
          token,
        });
      }
    }
    let mediaAssetId = mediaByPath.get(sourcePath);
    if (!mediaAssetId) {
      const upload = await uploadFile(apiBase, token, sourcePath, role);
      mediaAssetId = upload.mediaAssetId ?? upload.MediaAssetId;
      if (!mediaAssetId) {
        throw new Error(`Upload completed without a media asset id for ${sourcePath}.`);
      }
      mediaByPath.set(sourcePath, mediaAssetId);
    }
    const attachedAsset = await attachPaperAsset(apiBase, token, paper.id, {
      role,
      mediaAssetId,
      part: asset.part ?? null,
      title: asset.title ?? path.basename(sourcePath),
      displayOrder: asset.displayOrder ?? 1,
      makePrimary: asset.makePrimary !== false,
    });
    attached.push(attachedAsset);
  }
  return attached;
}

async function attachPaperAsset(apiBase, token, paperId, asset) {
  const body = {
    role: paperAssetRoleValue(asset.role),
    mediaAssetId: asset.mediaAssetId,
    part: asset.part,
    title: asset.title,
    displayOrder: asset.displayOrder,
    makePrimary: asset.makePrimary,
  };

  try {
    return await apiRequest(apiBase, `/v1/admin/papers/${paperId}/assets`, {
      method: 'POST',
      token,
      body,
    });
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    if (!message.includes('terminated')) throw error;

    const latestPaper = await apiRequest(apiBase, `/v1/admin/papers/${paperId}`, { token });
    const persistedAsset = (latestPaper.assets ?? []).find((candidate) => (
      candidate.role === asset.role
      && (candidate.part ?? null) === (asset.part ?? null)
      && candidate.mediaAssetId === asset.mediaAssetId
    ));
    if (!persistedAsset) throw error;
    return persistedAsset;
  }
}

async function upsertPaper(apiBase, token, paperConfig, replaceExisting) {
  const slug = requireText(paperConfig.slug, 'paper.slug');
  const allPapers = await listReadingPapers(apiBase, token);
  const existing = allPapers.find((paper) => paper.slug === slug);
  const body = {
    subtestCode: 'reading',
    title: requireText(paperConfig.title, 'paper.title'),
    slug,
    professionId: null,
    appliesToAllProfessions: true,
    difficulty: paperConfig.difficulty ?? 'standard',
    estimatedDurationMinutes: paperConfig.estimatedDurationMinutes ?? 60,
    cardType: null,
    letterType: null,
    priority: paperConfig.priority ?? 0,
    tagsCsv: paperConfig.tagsCsv ?? 'reading,local-import',
    sourceProvenance: requireText(paperConfig.sourceProvenance, 'paper.sourceProvenance'),
  };

  if (!existing) {
    const created = await apiRequest(apiBase, '/v1/admin/papers', {
      method: 'POST',
      token,
      body,
    });
    return { paper: created, created: true };
  }

  if (!replaceExisting) {
    throw new Error(`Paper '${slug}' already exists. Re-run with --replace-existing to update it.`);
  }

  await ensureDraftForReplacement(apiBase, token, existing);
  const updated = await apiRequest(apiBase, `/v1/admin/papers/${existing.id}`, {
    method: 'PUT',
    token,
    body: {
      title: body.title,
      professionId: null,
      appliesToAllProfessions: true,
      difficulty: body.difficulty,
      estimatedDurationMinutes: body.estimatedDurationMinutes,
      cardType: null,
      letterType: null,
      priority: body.priority,
      tagsCsv: body.tagsCsv,
      sourceProvenance: body.sourceProvenance,
    },
  });
  return { paper: updated, created: false };
}

async function importPaper(apiBase, token, paperConfig, baseDir, options) {
  const { paper, created } = await upsertPaper(apiBase, token, paperConfig, options.replaceExisting);
  await replaceAssets(apiBase, token, paper, paperConfig, baseDir);
  await apiRequest(apiBase, `/v1/admin/papers/${paper.id}/reading/ensure-canonical`, {
    method: 'POST',
    token,
  });
  let importResult;
  try {
    importResult = await apiRequest(apiBase, `/v1/admin/papers/${paper.id}/reading/manifest`, {
      method: 'POST',
      token,
      body: {
        replaceExisting: true,
        manifest: paperConfig.manifest,
      },
    });
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    if (!message.includes(`/v1/admin/papers/${paper.id}/reading/manifest response read error: terminated`)) {
      throw error;
    }
    importResult = { structure: { parts: paperConfig.manifest.parts } };
  }
  const validation = await apiRequest(apiBase, `/v1/admin/papers/${paper.id}/reading/validate`, { token });
  if (!validation.isPublishReady) {
    throw new Error(
      `Paper '${paper.slug}' is not publish-ready: ${JSON.stringify(validation.issues, null, 2)}`,
    );
  }
  if (options.publish) {
    await apiRequest(apiBase, `/v1/admin/papers/${paper.id}/publish`, {
      method: 'POST',
      token,
    });
  }
  const publishedPaper = await apiRequest(apiBase, `/v1/admin/papers/${paper.id}`, { token });
  return {
    id: paper.id,
    slug: paper.slug,
    title: paper.title,
    created,
    status: publishedPaper.status,
    counts: validation.counts,
    importedParts: importResult.structure?.parts?.length ?? null,
  };
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const apiBase = args.api ?? DEFAULT_API_BASE;
  assertLocalApi(apiBase);

  const manifestPath = requireText(args.manifest, '--manifest');
  const email = requireText(args.email ?? process.env.OET_ADMIN_EMAIL, '--email or OET_ADMIN_EMAIL');
  const password = requireText(args.password ?? process.env.OET_ADMIN_PASSWORD, '--password or OET_ADMIN_PASSWORD');
  const { bundle, baseDir, resolvedPath } = await loadManifestBundle(manifestPath);
  const papers = Array.isArray(bundle?.papers) ? bundle.papers : [];
  if (papers.length === 0) throw new Error(`No papers found in ${resolvedPath}.`);

  const token = await signIn(apiBase, email, password);
  const options = {
    replaceExisting: Boolean(args['replace-existing']),
    publish: !args['no-publish'],
  };

  const results = [];
  for (const paperConfig of papers) {
    console.log(`Importing ${paperConfig.slug}...`);
    results.push(await importPaper(apiBase, token, paperConfig, baseDir, options));
  }
  console.log(JSON.stringify({ apiBase, papers: results }, null, 2));
}

main().catch((error) => {
  console.error(error instanceof Error ? error.stack ?? error.message : error);
  process.exitCode = 1;
});