/**
 * Rulebook Registry — Filesystem Discovery (Node / Vitest only)
 *
 * The runtime loader at [./loader.ts](./loader.ts) uses static imports so the
 * Next.js bundler can tree-shake and so the engine works in the browser. That
 * means every rulebook on disk must also be registered in `loader.ts` by
 * hand. Drift between the filesystem and the loader is the single most
 * common rulebook bug — a JSON file gets added to `rulebooks/...` and the
 * loader silently ignores it.
 *
 * This module is the CI gate: it walks `rulebooks/**\/rulebook.v1.json` at
 * test time, asserts every file exists in the loader registry, validates each
 * file against `rulebooks/schema/rulebook.schema.json`, and asserts the UI
 * profession coverage matrix.
 *
 * NOTE: `fs` and `path` make this Node-only. Do NOT import from client/SSR
 * code; only from tests and CLI tools.
 */

import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import type { ExamProfession, Rule, Rulebook, RuleKind } from './types';
import { isRuleEnforced } from './coverage';

/** Resolve the repo root from this file's location at `lib/rulebook/`. */
const REPO_ROOT = join(__dirname, '..', '..');
const RULEBOOKS_ROOT = join(REPO_ROOT, 'rulebooks');

/** A single rulebook discovered on disk, with its file path and parsed body. */
export interface DiscoveredRulebook {
  /** e.g. "writing" or "listening-exam-mode" — the directory under `rulebooks/`. */
  kind: RuleKind;
  /** e.g. "medicine" or "_exam-mode" — the directory under `rulebooks/{kind}/`. */
  profession: ExamProfession | '_exam-mode';
  /** Absolute path on disk. */
  absolutePath: string;
  /** Path relative to the repo root, using forward slashes. */
  relativePath: string;
  /** Parsed JSON body. */
  rulebook: Rulebook;
}

/**
 * Discover every rulebook on disk. Returns the same shape regardless of
 * platform (slashes are normalised to forward slashes in `relativePath`).
 */
export function discoverRulebooks(): DiscoveredRulebook[] {
  const result: DiscoveredRulebook[] = [];
  const kinds = readdirSync(RULEBOOKS_ROOT, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => entry.name)
    // 'schema' holds the JSON Schema, not a rulebook.
    // 'drills' is the writing drill content (not standalone rulebooks).
    .filter((name) => name !== 'schema' && name !== 'drills');

  for (const kind of kinds) {
    const kindDir = join(RULEBOOKS_ROOT, kind);
    const professionEntries = readdirSync(kindDir, { withFileTypes: true });
    for (const entry of professionEntries) {
      if (!entry.isDirectory()) continue;
      // 'common' holds assessment criteria, not a rulebook.
      // 'drills' lives only under writing/.
      if (entry.name === 'common' || entry.name === 'drills') continue;

      const ruleFile = join(kindDir, entry.name, 'rulebook.v1.json');
      try {
        statSync(ruleFile);
      } catch {
        // No rulebook.v1.json in this profession directory yet — skip.
        continue;
      }
      const raw = readFileSync(ruleFile, 'utf-8').replace(/^﻿/, '');
      let parsed: Rulebook;
      try {
        parsed = JSON.parse(raw) as Rulebook;
      } catch (err) {
        throw new Error(
          `Failed to parse rulebook at ${relative(REPO_ROOT, ruleFile)}: ${(err as Error).message}`,
        );
      }
      result.push({
        kind: parsed.kind ?? (kind as RuleKind),
        profession: parsed.profession ?? (entry.name as ExamProfession),
        absolutePath: ruleFile,
        relativePath: relative(REPO_ROOT, ruleFile).split(sep).join('/'),
        rulebook: parsed,
      });
    }
  }
  return result;
}

/** Read the JSON Schema document that all rulebooks are validated against. */
export function readRulebookSchema(): unknown {
  const schemaPath = join(RULEBOOKS_ROOT, 'schema', 'rulebook.schema.json');
  return JSON.parse(readFileSync(schemaPath, 'utf-8'));
}

// ---------------------------------------------------------------------------
// Lightweight JSON-Schema validator
// ---------------------------------------------------------------------------
// We deliberately avoid a runtime dependency (ajv) so this stays in tree.
// The validator supports the subset of JSON Schema actually used by
// `rulebook.schema.json`: type, required, enum, properties, $ref to $defs,
// items, oneOf, format=date-time (treated as string), additionalProperties=false.
// If we ever need a fuller validator, swap to ajv here and the public API
// (`validateRulebook`) stays the same.

interface SchemaContext {
  defs: Record<string, unknown>;
  path: string;
  errors: string[];
}

function _validate(value: unknown, schema: unknown, ctx: SchemaContext): void {
  if (schema === null || typeof schema !== 'object') return;
  const s = schema as Record<string, unknown>;

  if (typeof s.$ref === 'string') {
    const ref = (s.$ref as string).replace(/^#\/\$defs\//, '');
    const def = ctx.defs[ref];
    if (!def) {
      ctx.errors.push(`${ctx.path}: unknown $ref ${s.$ref}`);
      return;
    }
    _validate(value, def, ctx);
    return;
  }

  if (Array.isArray(s.oneOf)) {
    let matched = 0;
    for (const sub of s.oneOf as unknown[]) {
      const branchCtx: SchemaContext = { defs: ctx.defs, path: ctx.path, errors: [] };
      _validate(value, sub, branchCtx);
      if (branchCtx.errors.length === 0) matched++;
    }
    if (matched !== 1) {
      ctx.errors.push(`${ctx.path}: expected exactly one oneOf branch to match, matched ${matched}`);
    }
    return;
  }

  const expectedType = s.type as string | undefined;
  if (expectedType) {
    const actualType = Array.isArray(value)
      ? 'array'
      : value === null
        ? 'null'
        : typeof value;
    if (expectedType === 'integer') {
      if (typeof value !== 'number' || !Number.isInteger(value)) {
        ctx.errors.push(`${ctx.path}: expected integer, got ${actualType}`);
        return;
      }
    } else if (actualType !== expectedType) {
      ctx.errors.push(`${ctx.path}: expected ${expectedType}, got ${actualType}`);
      return;
    }
  }

  if (Array.isArray(s.enum)) {
    if (!(s.enum as unknown[]).includes(value as unknown)) {
      ctx.errors.push(`${ctx.path}: value ${JSON.stringify(value)} not in enum ${JSON.stringify(s.enum)}`);
      return;
    }
  }

  if (expectedType === 'object' && value && typeof value === 'object' && !Array.isArray(value)) {
    const obj = value as Record<string, unknown>;
    const required = (s.required as string[] | undefined) ?? [];
    for (const key of required) {
      if (!(key in obj)) ctx.errors.push(`${ctx.path}: missing required field "${key}"`);
    }
    const props = (s.properties as Record<string, unknown> | undefined) ?? {};
    const additional = s.additionalProperties;
    for (const [key, child] of Object.entries(obj)) {
      const childSchema = props[key];
      if (childSchema) {
        _validate(child, childSchema, { ...ctx, path: `${ctx.path}.${key}` });
      } else if (additional === false) {
        ctx.errors.push(`${ctx.path}: unexpected property "${key}"`);
      }
      // additionalProperties === true or undefined: pass.
    }
  }

  if (expectedType === 'array' && Array.isArray(value) && s.items) {
    for (let i = 0; i < value.length; i++) {
      _validate(value[i], s.items, { ...ctx, path: `${ctx.path}[${i}]` });
    }
  }
}

/**
 * Validate a rulebook against `rulebooks/schema/rulebook.schema.json`.
 * Returns an empty array if the rulebook is valid; otherwise a list of
 * human-readable error strings.
 */
export function validateRulebook(rulebook: unknown, schemaDoc: unknown = readRulebookSchema()): string[] {
  const schema = schemaDoc as Record<string, unknown>;
  const defs = (schema.$defs as Record<string, unknown> | undefined) ?? {};
  const ctx: SchemaContext = { defs, path: '$', errors: [] };
  _validate(rulebook, schema, ctx);
  return ctx.errors;
}

// ---------------------------------------------------------------------------
// Coverage helpers
// ---------------------------------------------------------------------------

/**
 * Assert that every (kind, profession) pair in `pairs` has a rulebook on
 * disk. Returns the list of missing pairs (empty when fully covered).
 */
export function findMissingCoverage(
  pairs: Array<{ kind: RuleKind; profession: ExamProfession }>,
  discovered: DiscoveredRulebook[] = discoverRulebooks(),
): Array<{ kind: RuleKind; profession: ExamProfession }> {
  const have = new Set(discovered.map((d) => `${d.kind}:${d.profession}`));
  return pairs.filter((p) => !have.has(`${p.kind}:${p.profession}`));
}

/**
 * Walk every rule across every discovered rulebook and return the
 * critical/major rules that are NOT genuinely enforced — i.e. lack a BACKED
 * `checkId`, `forbiddenPatterns`, or an explicit `ai-grounded` /
 * `human-review-only` marker. An empty array means no critical/major rule is
 * silently unenforced.
 *
 * The enforcement contract lives once in `coverage.ts` (`isRuleEnforced`), so
 * this Node-only gate and the browser-safe dashboard classifier can never
 * diverge (see `coverage.test.ts`). Note this is STRICTER than a bare
 * `rule.checkId` check: a `checkId` with no backing detector (a dead check)
 * counts as unenforced, which is exactly the silent gap we want surfaced.
 */
export function findUnenforcedRules(
  discovered: DiscoveredRulebook[] = discoverRulebooks(),
): Array<{ kind: RuleKind; profession: ExamProfession | '_exam-mode'; rule: Rule }> {
  const result: Array<{ kind: RuleKind; profession: ExamProfession | '_exam-mode'; rule: Rule }> = [];
  for (const d of discovered) {
    for (const rule of d.rulebook.rules) {
      if (!isRuleEnforced(rule, d.kind)) {
        result.push({ kind: d.kind, profession: d.profession, rule });
      }
    }
  }
  return result;
}
