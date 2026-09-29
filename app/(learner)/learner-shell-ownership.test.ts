import { readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';

function walk(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const fullPath = path.join(dir, entry);
    if (statSync(fullPath).isDirectory()) {
      return walk(fullPath);
    }
    return fullPath;
  });
}

function relative(file: string) {
  return path.relative(process.cwd(), file).replaceAll(path.sep, '/');
}

describe('learner shell ownership', () => {
  // Walks all of app/ on a read-only Docker volume mount — the I/O cost can
  // exceed vitest's 5s default.
  it('keeps the learner shell owned by app/(learner)/layout only', { timeout: 60_000 }, () => {
    const sources = walk(path.join(process.cwd(), 'app'))
      .filter((file) => /\.(ts|tsx)$/.test(file) && !/\.test\.(ts|tsx)$/.test(file))
      .map((file) => ({ file: relative(file), source: readFileSync(file, 'utf8') }));

    const layoutOffenders = sources
      .filter(({ file, source }) => file !== 'app/(learner)/layout.tsx' && source.includes('LearnerShellLayout'))
      .map(({ file }) => file);
    const nestedShellOffenders = sources
      .filter(({ source }) => source.includes('LearnerDashboardShell'))
      .map(({ file }) => file);

    expect(layoutOffenders).toEqual([]);
    expect(nestedShellOffenders).toEqual([]);
    // Guard against a vacuous pass if the walk or the path format breaks.
    expect(sources.find(({ file }) => file === 'app/(learner)/layout.tsx')?.source).toContain('LearnerShellLayout');
  });
});
