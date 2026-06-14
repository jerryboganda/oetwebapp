import fs from 'node:fs';
import path from 'node:path';
import { describe, expect, it, vi } from 'vitest';

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
      for (const key of requiredHubKeys) {
        expect(messages[key], `${locale} should resolve ${key}`).toEqual(expect.any(String));
        expect(messages[key], `${locale} should not expose raw key ${key}`).not.toBe(key);
      }
    }
  });

  it('keeps static writing translation keys covered by the message bundle', async () => {
    const { loadAllMessages } = await import('@/i18n');
    const enMessages = await loadAllMessages('en');
    const arMessages = await loadAllMessages('ar');
    const keys = collectStaticWritingKeys();

    expect(keys.length).toBeGreaterThan(100);

    const missingEnglish = keys.filter((key) => typeof enMessages[key] !== 'string');
    const missingArabicAfterFallback = keys.filter((key) => typeof arMessages[key] !== 'string');

    expect(missingEnglish).toEqual([]);
    expect(missingArabicAfterFallback).toEqual([]);
  });
});
