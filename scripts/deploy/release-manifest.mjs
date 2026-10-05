#!/usr/bin/env node
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { appendFileSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, posix, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { onBlock } from './verify-pipeline-contract.mjs';

export const COMPONENTS = ['web', 'api', 'db-backup', 'agent-gateway'];
const SHA = /^[a-f0-9]{40}$/;
const DIGEST = /^sha256:[a-f0-9]{64}$/;
const CHECKSUM = /^[a-f0-9]{64}$/;
const root = resolve(fileURLToPath(new URL('../..', import.meta.url)));

/**
 * Which images an edit can change (owner directive 2026-10-06: SMART and FAIL-PROOF deploys: a frontend-only change
 * rebuilds and rolls out only the web image, a backend-only change only the API, a test/docs/ledger-only change nothing).
 *
 * Each pattern mirrors what that image's REAL build context reads: web = Dockerfile.dockerignore allow-list minus the
 * test files it strips; api = backend/src/OetLearner.Api + the csproj's out-of-tree inputs; db-backup / agent-gateway = the
 * Dockerfiles' COPY sources. buildInputParityFailures() (run by the ship gate, the syntax gate and the guards job) fails the
 * run when this classifier, the push `paths:` filter in build-images.yml and the real contexts drift apart, so a new input
 * can never be silently reused from a stale image.
 */
export const TEST_FILE = /(?:^|\/)__tests__\/|\.(?:test|spec)\.[^/]+$/;
const WEB_DIR = /^(?:app|pages|components|contexts|hooks|lib|public|messages|types)\//;
const WEB_SHARED = /^(?:data|rulebooks)\//;
const WEB_ROOT = /^(?:package\.json|pnpm-lock\.yaml|\.npmrc|Dockerfile|Dockerfile\.dockerignore|tsconfig\.json|next-env\.d\.ts|next\.config\.ts|postcss\.config\.mjs|i18n\.ts|proxy\.ts|middleware\.ts|instrumentation(?:-client)?\.ts|sentry\.(?:client|server|edge)\.config\.ts)$/;
const API_INPUT = /^(?:backend\/(?!tests\/)|rulebooks\/|data\/|global\.json$|Directory\.[^/]+\.(?:props|targets|rsp)$|(?:NuGet|nuget)\.[Cc]onfig$)/;
const API_NOT_INPUT = /(?:\.md$|^backend\/(?:Dockerfile|Dockerfile\.dev)$)/i;
const BACKUP_INPUT = /^scripts\/backup\/(?:Dockerfile(?:\.dockerignore)?|[^/]+\.sh)$/;
const GATEWAY_INPUT = /^agent-gateway\/(?:Dockerfile|\.dockerignore|pyproject\.toml|src\/)/;

export function classifyInputs(files) {
  if (files === null) return { web: true, api: true, 'db-backup': true, 'agent-gateway': true, writing: true };
  const result = { web: false, api: false, 'db-backup': false, 'agent-gateway': false, writing: false };
  for (const file of files) {
    if (/^(\.github\/workflows\/build-images\.yml|scripts\/deploy\/release-manifest\.mjs)$/.test(file)) {
      COMPONENTS.forEach((key) => { result[key] = true; });
    }
    if (((WEB_DIR.test(file) || WEB_SHARED.test(file)) && !TEST_FILE.test(file)) || WEB_ROOT.test(file)) result.web = true;
    if (API_INPUT.test(file) && !API_NOT_INPUT.test(file)) result.api = true;
    if (BACKUP_INPUT.test(file)) result['db-backup'] = true;
    if (GATEWAY_INPUT.test(file)) result['agent-gateway'] = true;
    if (/^(backend\/src\/OetLearner\.Api\/(?:Services\/(?:Writing\/|Rulebook\/|Ai\/AiFeatureRouteResolver\.cs)|Configuration\/Writing)|rulebooks\/writing\/|tests\/writing-regression\/)/.test(file)
        && !/\.md$/i.test(file)) result.writing = true;
  }
  return result;
}

function globToRegExp(glob) {
  let out = '';
  for (let i = 0; i < glob.length; i += 1) {
    const c = glob[i];
    if (c === '*' && glob[i + 1] === '*') {
      if (glob[i + 2] === '/') { out += '(?:.*/)?'; i += 2; } else { out += '.*'; i += 1; }
    } else if (c === '*') out += '[^/]*';
    else out += c.replace(/[.+?^${}()|[\]\\]/g, '\\$&');
  }
  return new RegExp(`^${out}$`);
}

function copySources(dockerfile, base) {
  return dockerfile.split(/\r?\n/).flatMap((line) => {
    const match = /^\s*(?:COPY|ADD)\s+(?:--\S+\s+)*(.+)$/i.exec(line);
    if (!match || /--from=/i.test(line)) return [];
    const parts = match[1].trim().split(/\s+/);
    return parts.slice(0, -1).map((part) => posix.normalize(posix.join(base, part)));
  });
}

/**
 * Fail-proof drift guard: returns the reasons the classifier, the push-path filter and the real Docker build contexts
 * disagree. Empty means every file an image reads both flags that image and starts a build, and test files never do.
 * `files` is the tracked file list (git ls-files); `readFile(relative)` reads a tracked file.
 */
export function buildInputParityFailures({ files, readFile }) {
  const failures = [];
  const workflow = readFile('.github/workflows/build-images.yml');
  const need = (component, file, why) => {
    if (!classifyInputs([file])[component]) {
      failures.push(`${component} reads ${file} (${why}) but classifyInputs does not flag it: a later change would reuse a stale ${component} image`);
    }
    if (!buildInputsChanged([file], workflow)) {
      failures.push(`${component} reads ${file} (${why}) but the build-images.yml push paths would start no build for it`);
    }
  };

  // web: the real context is Dockerfile.dockerignore's allow-list minus its deny patterns.
  const lines = readFile('Dockerfile.dockerignore').split(/\r?\n/).map((l) => l.trim()).filter((l) => l && !l.startsWith('#'));
  const allow = lines.filter((l) => l.startsWith('!')).map((l) => l.slice(1));
  const deny = lines.filter((l) => !l.startsWith('!') && l !== '**');
  const allowed = (f) => allow.some((a) => (a.endsWith('/**') ? f.startsWith(a.slice(0, -2)) : a.endsWith('/') ? f.startsWith(a) : f === a));
  const denied = (f) => deny.some((d) => posix.matchesGlob(f, d) || posix.matchesGlob(f, `${d}/**`));
  for (const f of files) if (allowed(f) && !denied(f)) need('web', f, 'in the web build context');

  // api: the csproj, its out-of-tree includes, and the runtime image inputs.
  const API_FILE = /^(?:backend\/src\/OetLearner\.Api\/|backend\/(?:Directory\.[^/]+|\.editorconfig|Dockerfile\.runtime(?:\.dockerignore)?)$|backend\/scripts\/StripeProductSeeder\/catalog\.json$|(?:global\.json|NuGet\.config)$|Directory\.[^/]+\.(?:props|targets)$)/;
  for (const f of files) {
    if (API_FILE.test(f) && !/\.md$/i.test(f) && !/(?:^|\/)(?:bin|obj)\//.test(f)) need('api', f, 'published into the API image');
  }
  const csproj = readFile('backend/src/OetLearner.Api/OetLearner.Api.csproj');
  for (const m of csproj.matchAll(/(?:Include|Update)="((?:\.\.[\\/])+[^"$]*)"/g)) {
    const relative = posix.normalize(posix.join('backend/src/OetLearner.Api', m[1].replaceAll('\\', '/')));
    if (relative.startsWith('backend/src/OetLearner.Api/')) continue;
    const pattern = globToRegExp(relative);
    for (const f of files) if (pattern.test(f)) need('api', f, `referenced by OetLearner.Api.csproj (${m[1]})`);
  }

  // db-backup and agent-gateway: exactly what their Dockerfiles COPY.
  for (const [component, dockerfile, base] of [['db-backup', 'scripts/backup/Dockerfile', ''], ['agent-gateway', 'agent-gateway/Dockerfile', 'agent-gateway']]) {
    const sources = copySources(readFile(dockerfile), base);
    for (const f of files) {
      if (f === dockerfile || f === `${dockerfile}.dockerignore` || sources.some((s) => f === s || f.startsWith(`${s}/`))) {
        need(component, f, `copied by ${dockerfile}`);
      }
    }
  }

  // Each root-context image must keep its own ignore file (the root .dockerignore governs no CI image).
  for (const ignore of ['Dockerfile.dockerignore', 'backend/Dockerfile.runtime.dockerignore', 'scripts/backup/Dockerfile.dockerignore']) {
    if (!files.includes(ignore)) failures.push(`${ignore} is missing: that image would silently fall back to the root .dockerignore`);
  }

  // Smart: a test, docs or ledger edit must never start a build or a rollout.
  const NEVER_BUILDS = /^(?:docs\/|SESSION_STATE\.md$|TASKS\.json$|VERIFICATION\.md$|PROGRESS\.md$)|(?:^|\/)__tests__\/|\.(?:test|spec)\.[^/]+$/;
  for (const f of files) {
    if (NEVER_BUILDS.test(f) && buildInputsChanged([f], workflow)) {
      failures.push(`${f} is a test/docs/ledger file but the build-images.yml push paths would start a build and a rollout for it`);
    }
  }
  return failures;
}

export function validateManifest(value, repo, expectedSha) {
  if (!value || value.schema !== 1 || value.repo !== repo || !SHA.test(value.sha)
      || (expectedSha && value.sha !== expectedSha) || !Number.isSafeInteger(value.buildRunId) || value.buildRunId <= 0) {
    throw new Error('Invalid release manifest identity.');
  }
  for (const name of COMPONENTS) {
    const image = value.components?.[name];
    if (!image || image.image !== `ghcr.io/${repo}-${name}:${value.sha}` || !DIGEST.test(image.digest)
        || !SHA.test(image.sourceSha) || !Number.isSafeInteger(image.sourceRunId) || image.sourceRunId <= 0) {
      throw new Error(`Invalid release component provenance: ${name}`);
    }
  }
  const sql = value.migrations;
  if (!sql || sql.sha !== value.components.api.sourceSha || sql.runId !== value.components.api.sourceRunId
      || sql.artifact !== `api-release-${sql.sha}` || !CHECKSUM.test(sql.checksum) || sql.efVersion !== '10.0.5') {
    throw new Error('Invalid API migration provenance.');
  }
  return value;
}

export function apiNeedsMigrations(release, deployed) {
  // A successful build may have been superseded without ever applying its SQL.
  return !deployed || release.components.api.digest !== deployed.components.api.digest;
}

export function verifySqlArtifact(manifest, sql, { sha, runId, checksum }) {
  if (!SHA.test(sha) || !Number.isSafeInteger(runId) || runId <= 0 || !CHECKSUM.test(checksum)
      || manifest.sha !== sha || manifest.runId !== runId || manifest.checksum !== checksum
      || manifest.efVersion !== '10.0.5') throw new Error('API SQL artifact provenance mismatch.');
  if (sql.length === 0) throw new Error('Migration SQL is empty.');
  if (createHash('sha256').update(sql).digest('hex') !== checksum) throw new Error('Migration SQL checksum mismatch.');
}

export function eligibleBuild(run) {
  return run.status === 'completed' && run.conclusion === 'success' && run.headBranch === 'main'
    && ['push', 'workflow_dispatch'].includes(run.event) && SHA.test(run.headSha)
    && !run.displayTitle.startsWith('Benchmark build ');
}

export function comparisonContainsSha(comparison, sha) {
  return ['ahead', 'identical'].includes(comparison.status) && comparison.merge_base_commit?.sha === sha;
}

export function changedPaths(comparison) {
  if (!Array.isArray(comparison.files)) throw new Error('Comparison did not return changed files.');
  if (comparison.files.length >= 300) return null;
  return comparison.files.flatMap((file) => [file.filename, ...(file.previous_filename ? [file.previous_filename] : [])]);
}

export function buildInputsChanged(files, workflow) {
  if (files === null) return true;
  const lines = onBlock(workflow).split(/\r?\n/);
  const start = lines.findIndex((line) => /^    paths:\s*$/.test(line));
  if (start < 0) throw new Error('Build workflow push paths are unavailable.');
  const patterns = [];
  for (const line of lines.slice(start + 1)) {
    if (!line.trim() || /^\s*#/.test(line)) continue;
    if (/^\s{0,5}\S/.test(line)) break;
    const pattern = /^      - '([^']+)'\s*$/.exec(line)?.[1];
    if (!pattern) throw new Error('Unrecognized build workflow push-path syntax.');
    patterns.push(pattern);
  }
  if (!patterns.length) throw new Error('Build workflow push paths are empty.');
  return files.some((file) => {
    let included = false;
    for (const pattern of patterns) {
      const excluded = pattern.startsWith('!');
      if (posix.matchesGlob(file, excluded ? pattern.slice(1) : pattern)) included = !excluded;
    }
    return included;
  });
}

function command(bin, args) {
  return execFileSync(bin, args, { cwd: root, encoding: 'utf8', maxBuffer: 16 * 1024 * 1024 }).trim();
}

function ghJson(args) {
  return JSON.parse(command('gh', args));
}

function output(values) {
  for (const [key, value] of Object.entries(values)) {
    if (/[\r\n]/.test(String(value))) throw new Error(`Multiline workflow output refused: ${key}`);
    appendFileSync(process.env.GITHUB_OUTPUT, `${key}=${value}\n`);
  }
}

function saveManifest(manifest) {
  mkdirSync(join(root, 'output'), { recursive: true });
  writeFileSync(join(root, 'output', 'release.json'), `${JSON.stringify(manifest, null, 2)}\n`);
}

function runs(repo, workflow, status) {
  return ghJson(['run', 'list', '--repo', repo, '--workflow', workflow, '--branch', 'main',
    '--limit', '10', ...(status ? ['--status', status] : []),
    '--json', 'databaseId,headSha,headBranch,status,conclusion,event,displayTitle'])
    .filter(eligibleBuild);
}

function ancestor(repo, base, head) {
  if (base === head) return true;
  const compare = ghJson(['api', `repos/${repo}/compare/${base}...${head}`]);
  return comparisonContainsSha(compare, base);
}

function downloadManifest(repo, runId, name) {
  const inventory = ghJson(['api', `repos/${repo}/actions/runs/${runId}/artifacts?per_page=100`]);
  if (!inventory.artifacts.some((artifact) => artifact.name === name && !artifact.expired)) return null;
  const dir = mkdtempSync(join(tmpdir(), 'oet-release-'));
  try {
    command('gh', ['run', 'download', String(runId), '--repo', repo, '--name', name, '--dir', dir]);
    return JSON.parse(readFileSync(join(dir, 'release.json'), 'utf8'));
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

function baseline(repo, sha) {
  let checkBase = null;
  for (const run of runs(repo, 'build-images.yml', 'success')) {
    if (String(run.databaseId) === process.env.GITHUB_RUN_ID || !ancestor(repo, run.headSha, sha)) continue;
    checkBase ||= run.headSha;
    const manifest = downloadManifest(repo, run.databaseId, 'release-manifest');
    if (!manifest) continue;
    validateManifest(manifest, repo, run.headSha);
    if (manifest.buildRunId !== run.databaseId) throw new Error('Baseline build-run identity mismatch.');
    return { manifest, checkBase: manifest.sha };
  }
  console.warn('::notice::No verified successful ancestor manifest; rebuilding every component.');
  return { manifest: null, checkBase };
}

function imageDigest(image) {
  const text = command('docker', ['buildx', 'imagetools', 'inspect', image]);
  const digest = /^Digest:\s+(sha256:[a-f0-9]{64})\s*$/m.exec(text)?.[1];
  if (!digest) throw new Error(`Registry did not return a manifest digest for ${image}`);
  return digest;
}

function detect(repo, sha) {
  const selected = process.env.GITHUB_EVENT_NAME === 'pull_request' ? { manifest: null, checkBase: null } : baseline(repo, sha);
  const previous = selected.manifest;
  let files = null;
  if (selected.checkBase) {
    const compare = ghJson(['api', `repos/${repo}/compare/${selected.checkBase}...${sha}`]);
    files = changedPaths(compare);
    if (files === null) {
      console.warn('::notice::Truncated comparison; conservatively rebuilding every component.');
    }
  }
  const flags = classifyInputs(files);
  if (process.env.FORCE_WRITING === 'true') flags.writing = true;
  if (!previous || process.env.REBUILD_ALL === 'true') COMPONENTS.forEach((name) => { flags[name] = true; });
  const include = COMPONENTS.filter((name) => !flags[name]).map((name) => ({
    image: name, source: `ghcr.io/${repo}-${name}@${previous.components[name].digest}`,
    digest: previous.components[name].digest,
  }));
  output({
    web: flags.web, api: flags.api, backup: flags['db-backup'], gateway: flags['agent-gateway'],
    writing: flags.writing, baseline: JSON.stringify(previous), reuse_matrix: JSON.stringify({ include }),
    reuse_count: include.length,
  });
  console.log(`RELEASE_INPUTS baseline=${previous?.sha ?? 'none'} ${JSON.stringify(flags)}`);
}

function createManifest(repo, sha) {
  const previous = JSON.parse(process.env.BASELINE || 'null');
  if (previous) validateManifest(previous, repo);
  const buildRunId = Number(process.env.GITHUB_RUN_ID);
  const components = {};
  for (const name of COMPONENTS) {
    const envName = name.toUpperCase().replaceAll('-', '_');
    const changed = process.env[`${envName}_CHANGED`] === 'true';
    const source = changed ? null : previous?.components[name];
    const digest = changed ? process.env[`${envName}_DIGEST`] : source?.digest;
    const image = `ghcr.io/${repo}-${name}:${sha}`;
    if (!DIGEST.test(digest) || imageDigest(image) !== digest) throw new Error(`Release image digest mismatch: ${name}`);
    components[name] = { image, digest, sourceSha: changed ? sha : source.sourceSha,
      sourceRunId: changed ? buildRunId : source.sourceRunId };
  }
  const migrations = process.env.API_CHANGED === 'true'
    ? { sha, runId: buildRunId, artifact: `api-release-${sha}`, checksum: process.env.SQL_CHECKSUM, efVersion: '10.0.5' }
    : previous.migrations;
  saveManifest(validateManifest({ schema: 1, repo, sha, buildRunId, components, migrations }, repo, sha));
  console.log(`RELEASE_MANIFEST sha=${sha} build_run=${buildRunId}`);
}

function superseding(repo, sha, runId) {
  for (const run of runs(repo, 'build-images.yml', 'success')) {
    if (String(run.databaseId) === String(runId)) break;
    if (run.headSha !== sha && ancestor(repo, sha, run.headSha)) return run;
  }
  return null;
}

function resolveRelease(repo, sha) {
  const manual = process.env.MANUAL_ROLLBACK === 'true';
  const matching = process.env.BUILD_RUN_ID ? [] : ghJson(['run', 'list', '--repo', repo,
    '--workflow', 'build-images.yml', '--branch', 'main', '--commit', sha, '--status', 'success',
    '--limit', '10', '--json', 'databaseId,event,displayTitle']);
  const runId = Number(process.env.BUILD_RUN_ID || matching
    .find((run) => run.event !== 'pull_request' && !run.displayTitle.startsWith('Benchmark build '))?.databaseId);
  let release = null;
  if (Number.isSafeInteger(runId) && runId > 0) {
    const build = ghJson(['run', 'view', String(runId), '--repo', repo, '--json', 'headSha,headBranch,conclusion']);
    if (build.headSha !== sha || build.headBranch !== 'main' || build.conclusion !== 'success') throw new Error('Source build is not a successful exact-SHA main build.');
    release = downloadManifest(repo, runId, 'release-manifest');
    if (release) {
      validateManifest(release, repo, sha);
      if (release.buildRunId !== runId) throw new Error('Release manifest build-run identity mismatch.');
    }
  }
  let deployed = null;
  let rollbackProven = false;
  const history = ghJson(['run', 'list', '--repo', repo, '--workflow', 'production-deploy.yml',
    '--status', 'success', '--limit', manual ? '100' : '10', '--json', 'databaseId,headSha,displayTitle']);
  for (const run of history) {
    if (String(run.databaseId) === process.env.GITHUB_RUN_ID) continue;
    const manifest = downloadManifest(repo, run.databaseId, 'production-release');
    if (manifest) {
      validateManifest(manifest, repo);
      if (!deployed) deployed = manifest;
      if (manifest.sha === sha) rollbackProven = true;
    } else if (manual && run.displayTitle === `Deploy production ${sha}`) {
      const details = ghJson(['run', 'view', String(run.databaseId), '--repo', repo, '--json', 'jobs']);
      rollbackProven ||= details.jobs.some((job) => job.name === 'Roll out to the VPS'
        && job.conclusion === 'success'
        && job.steps.some((step) => step.name === 'Deploy to VPS over SSH (pull + blue/green redeploy)' && step.conclusion === 'success'));
    }
    if ((!manual && deployed) || (manual && rollbackProven)) break;
  }
  if (manual && !rollbackProven) throw new Error('Rollback requires a proven previously deployed SHA.');
  if (!release && !manual) throw new Error('Required exact-SHA release manifest is missing.');
  const newer = manual ? null : superseding(repo, sha, runId);
  const components = release?.components ?? Object.fromEntries(COMPONENTS.map((name) => {
    const image = `ghcr.io/${repo}-${name}:${sha}`;
    return [name, { image, digest: imageDigest(image), sourceSha: sha, sourceRunId: runId }];
  }));
  if (release) {
    for (const name of COMPONENTS) {
      if (imageDigest(components[name].image) !== components[name].digest) throw new Error(`Target image changed after build: ${name}`);
    }
  }
  if (release) saveManifest(release);
  const apiChanged = !manual && apiNeedsMigrations(release, deployed);
  output({
    sha, build_run_id: runId || '', deployable: !newer, api_changed: apiChanged,
    api_source_sha: release?.migrations.sha ?? '', api_source_run: release?.migrations.runId ?? '',
    sql_checksum: release?.migrations.checksum ?? '',
    web_image: `ghcr.io/${repo}-web@${components.web.digest}`,
    api_image: `ghcr.io/${repo}-api@${components.api.digest}`,
    backup_image: `ghcr.io/${repo}-db-backup@${components['db-backup'].digest}`,
    gateway_image: `ghcr.io/${repo}-agent-gateway@${components['agent-gateway'].digest}`,
    legacy: !release,
  });
  console.log(newer ? `RELEASE_SUPERSEDED_BY ${newer.headSha} run=${newer.databaseId}` : `RELEASE_RESOLVED ${sha} api_changed=${apiChanged}`);
}

function verifyApi(repo) {
  const manifest = JSON.parse(readFileSync(join(root, 'output', 'api-release', 'api.json'), 'utf8'));
  const sha = process.env.API_SOURCE_SHA;
  const runId = Number(process.env.API_SOURCE_RUN);
  const build = ghJson(['run', 'view', String(runId), '--repo', repo, '--json', 'headSha,headBranch,conclusion']);
  if (build.headSha !== sha || build.headBranch !== 'main' || build.conclusion !== 'success') {
    throw new Error('API SQL source build provenance mismatch.');
  }
  const sql = readFileSync(join(root, 'output', 'api-release', 'migrations.sql'));
  verifySqlArtifact(manifest, sql, { sha, runId, checksum: process.env.SQL_CHECKSUM });
}

export function main(argv) {
  const repo = process.env.GITHUB_REPOSITORY;
  const sha = process.env.RELEASE_SHA || process.env.GITHUB_SHA;
  if (!/^[\w.-]+\/[\w.-]+$/.test(repo) || !SHA.test(sha)) throw new Error('Invalid repository or release SHA.');
  switch (argv[0]) {
    case 'detect': return detect(repo, sha);
    case 'create': return createManifest(repo, sha);
    case 'resolve': return resolveRelease(repo, sha);
    case 'verify-api': return verifyApi(repo);
    case 'prove-no-build': {
      const base = process.env.PUSH_BASE_SHA;
      if (!SHA.test(base)) throw new Error('Missing exact before-push base; no-op cannot be proven.');
      const comparison = ghJson(['api', `repos/${repo}/compare/${base}...${sha}`]);
      if (!comparisonContainsSha(comparison, base)) throw new Error('Push base is not a proven ancestor.');
      if (buildInputsChanged(changedPaths(comparison), readFileSync(join(root, '.github', 'workflows', 'build-images.yml'), 'utf8'))) {
        console.log(`RELEASE_BUILD_EXPECTED sha=${sha}`);
        process.exitCode = 2;
      } else {
        console.log(`RELEASE_NO_DEPLOYMENT_INPUTS base=${base} sha=${sha}`);
      }
      return;
    }
    case 'superseded': {
      const newer = superseding(repo, sha, process.env.BUILD_RUN_ID);
      output({ superseded: Boolean(newer) });
      if (newer) console.log(`RELEASE_SUPERSEDED_BY ${newer.headSha} run=${newer.databaseId}`);
      return;
    }
    default: throw new Error('Expected detect, create, resolve, verify-api, prove-no-build or superseded.');
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try { main(process.argv.slice(2)); } catch (error) {
    console.error(`::error::${error.message}`);
    process.exitCode = 1;
  }
}
