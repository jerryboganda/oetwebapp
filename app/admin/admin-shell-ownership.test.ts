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

describe('admin shell ownership', () => {
  it('keeps AdminDashboardShell owned by app/admin/layout only', () => {
    const adminDir = path.join(process.cwd(), 'app', 'admin');
    const offenders = walk(adminDir)
      .filter((file) => /\.(ts|tsx)$/.test(file))
      .filter((file) => !file.endsWith(`${path.sep}layout.tsx`))
      .filter((file) => !file.endsWith(`${path.sep}layout.test.tsx`))
      .filter((file) => !file.endsWith(`${path.sep}admin-shell-ownership.test.ts`))
      .filter((file) => readFileSync(file, 'utf8').includes('AdminDashboardShell'))
      .map((file) => path.relative(process.cwd(), file).replaceAll(path.sep, '/'));

    expect(offenders).toEqual([]);
  });
});
