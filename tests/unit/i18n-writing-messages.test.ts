import fs from 'node:fs';
import path from 'node:path';
import { createTranslator } from 'next-intl';
import { describe, expect, it, vi } from 'vitest';

// The global test setup mocks `next-intl` down to a key-echoing stub, which
// hides exactly the nested-vs-flat resolution bug this suite guards against.
// Restore the real module here so `createTranslator` resolves like production.
vi.mock('next-intl', async (importOriginal) => importOriginal());

vi.mock('next/headers', () => ({
  cookies: vi.fn(async () => ({ get: () => undefined })),
  headers: vi.fn(async () => ({ get: () => null })),
}));

vi.mock('next-intl/server', () => ({
  getRequestConfig: vi.fn((factory: unknown) => factory),
}));

const repoRoot = process.cwd();
const staticWritingKeyPattern = /\bt\(\s*['"`](writing\.[^'"`$]+)['"`]/g;

function listFiles(dir: string): string[] {
  if (!fs.existsSync(dir)) return [];

  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const entryPath = path.join(dir, entry.name);
    if (entry.isDirectory() && entry.name === '__tests__') return [];
    if (entry.isDirectory()) return listFiles(entryPath);
    if (/\.(test|spec)\.(tsx?|jsx?)$/.test(entry.name)) return [];
    if (/\.(tsx?|jsx?)$/.test(entry.name)) return [entryPath];
    return [];
  });
}

function collectStaticWritingKeys() {
  const sourceRoots = [
    path.join(repoRoot, 'app', 'writing'),
    path.join(repoRoot, 'components', 'domain', 'writing'),
  ];

  const keys = new Set<string>();
  for (const file of sourceRoots.flatMap(listFiles)) {
    const source = fs.readFileSync(file, 'utf8');
    for (const match of source.matchAll(staticWritingKeyPattern)) {
      keys.add(match[1]);
    }
  }

  return [...keys].sort();
}

describe('writing i18n messages', () => {
  it('keeps message bundles statically imported for standalone builds', () => {
    const i18nSource = fs.readFileSync(path.join(repoRoot, 'i18n.ts'), 'utf8');

    expect(i18nSource).not.toMatch(/import\(\s*`\.\/messages\//);
    expect(i18nSource).toContain("import enWritingMessages from './messages/en/writing.json'");
    expect(i18nSource).toContain("import arWritingMessages from './messages/ar/writing.json'");
  });

  it('loads the writing hub copy for every supported locale', async () => {
    const { loadAllMessages } = await import('@/i18n');
    const requiredHubKeys = [
      'writing.hub.pageTitle',
      'writing.hub.hero.title',
      'writing.hub.hero.description',
      'writing.hub.cards.mocks.title',
      'writing.hub.cards.practice.title',
      'writing.hub.cards.diagnostic.title',
    ];

    for (const locale of ['en', 'ar'] as const) {
      const messages = await loadAllMessages(locale);
      // Resolve exactly as the app does at runtime (next-intl), not by reading
      // the raw bundle — dotted keys must resolve against the nested shape, or
      // the learner UI silently falls back to "Writing copy unavailable".
      const t = createTranslator({
        locale,
        messages: messages as Parameters<typeof createTranslator>[0]['messages'],
        getMessageFallback: ({ key }) => `__MISSING__${key}`,
        onError: () => {},
      });

      for (const key of requiredHubKeys) {
        const resolved = t(key);
        expect(resolved, `${locale} should resolve ${key}`).toEqual(expect.any(String));
        expect(resolved, `${locale} should not fall back for ${key}`).not.toContain('__MISSING__');
      }
    }
  });

  it('resolves every static writing translation key through next-intl', async () => {
    const { loadAllMessages } = await import('@/i18n');
    const keys = collectStaticWritingKeys();

    expect(keys.length).toBeGreaterThan(100);

    for (const locale of ['en', 'ar'] as const) {
      const messages = await loadAllMessages(locale);
      // Capture next-intl's error code per lookup: a genuinely absent key raises
      // `MISSING_MESSAGE` (the bug we guard against), whereas a present key that
      // needs ICU arguments raises a formatting error here only because the test
      // passes no values — that's expected and must not fail the suite.
      let lastErrorCode: string | undefined;
      const t = createTranslator({
        locale,
        messages: messages as Parameters<typeof createTranslator>[0]['messages'],
        onError: (error: { code?: string }) => {
          lastErrorCode = error.code;
        },
      });

      const missing = keys.filter((key) => {
        lastErrorCode = undefined;
        t(key);
        return lastErrorCode === 'MISSING_MESSAGE';
      });

      expect(missing, `${locale} bundle is missing keys`).toEqual([]);
    }
  });
});
