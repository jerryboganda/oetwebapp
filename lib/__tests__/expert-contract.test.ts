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

describe('expert route contracts', () => {
  it('does not ship expert routes that import mock-expert-data', () => {
    const files = collectFiles(join(process.cwd(), 'app', 'expert'));
    const offenders = files.filter((file) => readFileSync(file, 'utf8').includes('mock-expert-data'));

    expect(offenders).toEqual([]);
  });

  it('keeps /expert as a real dashboard instead of redirecting to the queue', () => {
    const expertIndex = readFileSync(join(process.cwd(), 'app', 'expert', 'page.tsx'), 'utf8');

    expect(expertIndex).not.toContain("redirect('/expert/queue')");
    expect(expertIndex).toContain('fetchExpertDashboard');
  });
});
