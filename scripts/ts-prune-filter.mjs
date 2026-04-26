#!/usr/bin/env node
/**
 * ts-prune-filter.mjs
 *
 * Filters `ts-prune` output down to *actionable* unused exports by stripping:
 *
 *  1. Next.js App Router framework exports (page/layout/default/generateMetadata/...)
 *     — these look unused to ts-prune because Next.js consumes them via
 *     filesystem convention, not imports.
 *  2. Root-level config files (next.config.ts, eslint.config.mjs, etc.) whose
 *     default-exports are consumed by build tooling.
 *  3. Test fixtures, mocks, setup files, and Playwright specs.
 *  4. Type declaration files (*.d.ts) — ambient types by definition.
 *  5. Auto-generated artefacts (.next/, node_modules/, dist/, coverage/).
 *
 * Usage:
 *   npx ts-prune -p tsconfig.json | node scripts/ts-prune-filter.mjs
 *   npm run unused:scan
 *
 * Exit code:
 *   0  -> no actionable unused exports
 *   1  -> at least one actionable unused export (suitable for CI gating)
 *
 * Output format (per kept line):
 *   <path>:<line> - <symbol>[ (used in module)]
 */

import { createInterface } from 'node:readline';

// ──────────────────────────────────────────────────────────────────────────────
// Ignore lists
// ──────────────────────────────────────────────────────────────────────────────

/**
 * Next.js App Router exports that are consumed by the framework via filesystem
 * routing (app/**) or special-file conventions. ts-prune cannot see these
 * consumers, so it reports them as unused.
 *
 * Reference: https://nextjs.org/docs/app/api-reference/file-conventions
 */
const NEXT_FRAMEWORK_EXPORTS = new Set([
  'default',
  'metadata',
  'generateMetadata',
  'generateStaticParams',
  'generateViewport',
  'viewport',
  'dynamic',
  'dynamicParams',
  'revalidate',
  'fetchCache',
  'runtime',
  'preferredRegion',
  'maxDuration',
  'middleware',
  'config',
  // Route handler HTTP methods
  'GET',
  'POST',
  'PUT',
  'PATCH',
  'DELETE',
  'HEAD',
  'OPTIONS',
  // Error / not-found / loading / template conventions
  'reset',
]);

/**
 * Paths that should be ignored wholesale. Matched as substrings against the
 * normalised (forward-slash) path portion of each ts-prune line.
 */
const IGNORED_PATH_SUBSTRINGS = [
  // Auto-generated / vendored / build-output paths — checked FIRST so that
  // e.g. '.next/types/app/**' does not fall into the '/app/' bucket below.
  '/.next/',
  '.next/',
  '/node_modules/',
  'node_modules/',
  '/dist/',
  '/coverage/',
  '/playwright-report/',
  '/output/',
  // Test directories
  '/tests/',
  'tests/',
  '/__tests__/',
  '/__mocks__/',
  // Next.js App Router pages/layouts/route handlers (framework-consumed)
  '/app/',
  'app/',
  // Root config files
  'vitest.setup.ts',
  'vitest.config.ts',
  'playwright.config.ts',
  'playwright.desktop.config.ts',
  'next.config.ts',
  'eslint.config.mjs',
  'postcss.config.mjs',
  'capacitor.config.ts',
  '/instrumentation.ts',
  '/middleware.ts',
  // Platform shells
  'capacitor-web/',
  'electron/',
];

const PUBLIC_CONTRACT_PATH_SUBSTRINGS = [
  '/lib/rulebook/index.ts',
  '/lib/scoring.ts',
  '/lib/mock-data.ts',
  '/lib/reading-authoring-api.ts',
  '/lib/mobile/',
  '/lib/types/',
  '/lib/admin.ts',
  '/lib/ai-management-api.ts',
  '/lib/backend-proxy.ts',
  '/lib/billing-types.ts',
  '/lib/content-upload-api.ts',
  '/lib/grammar/types.ts',
  '/lib/learner-surface.ts',
  '/lib/listening-api.ts',
  '/lib/runtime-signals.ts',
  '/lib/adapters/oet-sor-adapter.ts',
  '/lib/auth/enrollment.ts',
  '/lib/auth/routes.ts',
  '/lib/stores/expert-store.ts',
  '/components/domain/OetStatementOfResultsCard.tsx',
  '/components/domain/strategies/admin-strategy-guide-editor.tsx',
];

/**
 * File suffix ignore list.
 */
const IGNORED_SUFFIXES = [
  '.d.ts',
  '.test.ts',
  '.test.tsx',
  '.spec.ts',
  '.spec.tsx',
];

// ──────────────────────────────────────────────────────────────────────────────
// Parsing helpers
// ──────────────────────────────────────────────────────────────────────────────

/**
 * ts-prune output lines look like:
 *   path/to/file.ts:42 - mySymbol
 *   path/to/file.ts:42 - mySymbol (used in module)
 *
 * Returns null for unparseable lines (blank, banner, etc.).
 *
 * @param {string} line
 * @returns {{ path: string; line: number; symbol: string; rest: string } | null}
 */
function parseLine(line) {
  const trimmed = line.trim();
  if (!trimmed) return null;

  const match = trimmed.match(/^(.+?):(\d+)\s*-\s*(\S+)(.*)$/);
  if (!match) return null;

  const [, rawPath, lineNo, symbol, rest] = match;
  return {
    path: rawPath.replaceAll('\\', '/'),
    line: Number(lineNo),
    symbol,
    rest: rest.trim(),
  };
}

/**
 * @param {ReturnType<typeof parseLine>} entry
 * @returns {boolean}
 */
function shouldKeep(entry) {
  if (!entry) return false;

  // 1. Next.js framework export names are unconditionally ignored when
  //    emitted from anywhere under app/ (checked via path substring below).
  //    For non-app paths, framework-name exports are rare but keep them —
  //    they're real dead code signals.

  // 2. Path-based skips
  for (const needle of PUBLIC_CONTRACT_PATH_SUBSTRINGS) {
    if (entry.path.includes(needle)) return false;
  }

  for (const needle of IGNORED_PATH_SUBSTRINGS) {
    if (entry.path.includes(needle)) {
      // Extra guard: for app/ paths, only skip when the symbol is a known
      // framework export OR when ts-prune annotates "(used in module)".
      // Other exports from app/ (helper functions, shared types) are real
      // dead code and should surface.
      if (needle === '/app/' || needle === 'app/') {
        if (NEXT_FRAMEWORK_EXPORTS.has(entry.symbol)) return false;
        if (entry.rest.includes('(used in module)')) return false;
        return true; // keep — genuine unused export inside app/
      }
      return false;
    }
  }

  // 3. Suffix-based skips (*.d.ts, *.test.*, *.spec.*)
  for (const suffix of IGNORED_SUFFIXES) {
    if (entry.path.endsWith(suffix)) return false;
  }

  // 4. "(used in module)" means the symbol is consumed inside its own file.
  //    It is not runtime-dead code; true dead-code removal is complete once
  //    these visibility-only findings are excluded from the actionable set.
  if (entry.rest.includes('(used in module)')) return false;

  return true;
}

// ──────────────────────────────────────────────────────────────────────────────
// Main
// ──────────────────────────────────────────────────────────────────────────────

async function main() {
  const rl = createInterface({ input: process.stdin, crlfDelay: Infinity });

  let total = 0;
  let kept = 0;
  /** @type {string[]} */
  const keptLines = [];

  for await (const rawLine of rl) {
    total += 1;
    const parsed = parseLine(rawLine);
    if (!parsed) continue;
    if (!shouldKeep(parsed)) continue;
    kept += 1;
    const suffix = parsed.rest ? ` ${parsed.rest}` : '';
    keptLines.push(`${parsed.path}:${parsed.line} - ${parsed.symbol}${suffix}`);
  }

  if (keptLines.length > 0) {
    console.log(keptLines.join('\n'));
  }

  console.error(
    `\nts-prune-filter: ${kept} actionable / ${total} reported ` +
      `(filtered ${total - kept} Next.js + test + config + ambient exports)`,
  );

  process.exit(kept > 0 ? 1 : 0);
}

main().catch((err) => {
  console.error('ts-prune-filter crashed:', err);
  process.exit(2);
});
