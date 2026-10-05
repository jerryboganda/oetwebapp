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
    path.join(repoRoot, 'app', '(learner)', 'writing'),
    path.join(repoRoot, 'components', 'domain', 'writing'),
  ];
  const files = sourceRoots.map(listFiles);
  // listFiles returns [] for a missing dir, so a moved root would drop out silently.
  files.forEach((rootFiles, i) => expect(rootFiles.length, sourceRoots[i]).toBeGreaterThan(0));

  const keys = new Set<string>();
  for (const file of files.flat()) {
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

  it('memoizes the merged bundle per locale instead of rebuilding it on every call', async () => {
    const { loadAllMessages, loadWritingMessages } = await import('@/i18n');

    for (const locale of ['en', 'ar'] as const) {
      expect(await loadAllMessages(locale)).toBe(await loadAllMessages(locale));
      expect(await loadAllMessages(locale, { includeWriting: false })).toBe(await loadAllMessages(locale, { includeWriting: false }));
      expect(await loadWritingMessages(locale)).toBe(await loadWritingMessages(locale));
    }
    expect(await loadAllMessages('en')).not.toBe(await loadAllMessages('ar'));
  });

  it('leaves the Writing bundle out of the root messages and serves it on its own', async () => {
    const { loadAllMessages, loadWritingMessages } = await import('@/i18n');

    for (const locale of ['en', 'ar'] as const) {
      const root = await loadAllMessages(locale, { includeWriting: false }) as Record<string, unknown>;
      const writing = await loadWritingMessages(locale) as Record<string, unknown>;
      const everything = await loadAllMessages(locale) as Record<string, unknown>;

      // The default keeps loading everything, so existing callers are unchanged.
      expect(Object.keys(everything).sort()).toEqual([...Object.keys(root), 'writing'].sort());
      expect(root).not.toHaveProperty('writing');
      expect(root).toHaveProperty('companion');
      expect(root).toHaveProperty('freeSample');
      expect(Object.keys(writing)).toEqual(['writing']);
      expect(writing.writing).toEqual(everything.writing);
    }
  });

  it('resolves the Writing copy once the learner layout merges its bundle into the root messages', async () => {
    const { loadAllMessages, loadWritingMessages } = await import('@/i18n');
    const { mergeMessageTrees } = await import('@/components/providers/extra-messages-provider');

    for (const locale of ['en', 'ar'] as const) {
      const root = await loadAllMessages(locale, { includeWriting: false });
      const writing = await loadWritingMessages(locale);
      const merged = mergeMessageTrees(root as Record<string, unknown>, writing as Record<string, unknown>);
      const t = createTranslator({
        locale,
        messages: merged as Parameters<typeof createTranslator>[0]['messages'],
        getMessageFallback: ({ key }) => `__MISSING__${key}`,
        onError: () => {},
      });

      expect(t('writing.hub.pageTitle')).not.toContain('__MISSING__');
      expect(t('companion.page.title', { persona: 'Jana' })).not.toContain('__MISSING__');
      // The merge changed neither input.
      expect(root).not.toHaveProperty('writing');
    }
  });

  it('keeps the root layout light and the learner layout responsible for the Writing bundle', () => {
    const rootLayout = fs.readFileSync(path.join(repoRoot, 'app', 'layout.tsx'), 'utf8');
    const learnerLayout = fs.readFileSync(path.join(repoRoot, 'app', '(learner)', 'layout.tsx'), 'utf8');

    expect(rootLayout).toContain('loadAllMessages(locale, { includeWriting: false })');
    expect(learnerLayout).toContain('loadWritingMessages');
    expect(learnerLayout).toContain('ExtraMessagesProvider');
    expect(learnerLayout).toContain('LearnerShellLayout');
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
