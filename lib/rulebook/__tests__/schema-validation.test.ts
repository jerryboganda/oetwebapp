import { describe, it, expect } from 'vitest';
import { discoverRulebooks, readRulebookSchema, validateRulebook } from '../registry';

/**
 * Phase 0 CI gate — every JSON rulebook on disk must validate against
 * `rulebooks/schema/rulebook.schema.json`.
 *
 * This test enforces the "zero deviation" contract: the schema is the
 * canonical shape, and any rulebook that drifts (missing field, wrong type,
 * unknown kind, wrong profession) fails CI.
 */

describe('rulebook schema validation', () => {
  const schema = readRulebookSchema();
  const discovered = discoverRulebooks();

  it('discovers at least one rulebook', () => {
    expect(discovered.length).toBeGreaterThan(0);
  });

  it.each(discovered.map((d) => [d.relativePath, d]))(
    '%s passes JSON Schema',
    (_label, d) => {
      const errors = validateRulebook(d.rulebook, schema);
      expect(errors, errors.join('\n')).toEqual([]);
    },
  );

  it('every rulebook declares a non-empty version string', () => {
    for (const d of discovered) {
      expect(d.rulebook.version, `${d.relativePath} missing version`).toMatch(/^\d+\.\d+\.\d+/);
    }
  });

  it('every rulebook has a non-empty rules array', () => {
    for (const d of discovered) {
      expect(d.rulebook.rules.length, `${d.relativePath} has zero rules`).toBeGreaterThan(0);
    }
  });

  it('every rulebook has unique rule IDs', () => {
    for (const d of discovered) {
      const ids = d.rulebook.rules.map((r) => r.id);
      const dup = ids.filter((id, i) => ids.indexOf(id) !== i);
      expect(dup, `${d.relativePath} has duplicate rule IDs: ${dup.join(', ')}`).toEqual([]);
    }
  });

  it('every rule references a section that exists', () => {
    for (const d of discovered) {
      const sectionIds = new Set(d.rulebook.sections.map((s) => s.id));
      for (const rule of d.rulebook.rules) {
        expect(sectionIds.has(rule.section), `${d.relativePath} rule ${rule.id} → unknown section "${rule.section}"`).toBe(true);
      }
    }
  });
});
