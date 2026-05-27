import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
function collectFiles(directory: string): string[] {
  return readdirSync(directory).flatMap((entry) => {
    const fullPath = join(directory, entry);
    const stats = statSync(fullPath);
    if (stats.isDirectory()) {
      return collectFiles(fullPath);
    }

    return fullPath.endsWith('.tsx') ? [fullPath] : [];
  });
}

describe('admin route contracts', () => {
  // Filesystem-walk over the entire app/admin tree on a Docker-mounted volume —
  // raise the timeout above vitest's 5s default to avoid spurious failures.
  it('does not ship app/admin pages that import mock-admin-data', { timeout: 30_000 }, () => {
    const files = collectFiles(join(process.cwd(), 'app', 'admin'));
    const offenders = files.filter((file) => readFileSync(file, 'utf8').includes('mock-admin-data'));

    expect(offenders).toEqual([]);
  });
});
