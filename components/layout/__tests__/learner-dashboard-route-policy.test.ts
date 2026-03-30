import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import {
  EXCLUDED_IMMERSIVE_LEARNER_PAGE_PATHS,
  INCLUDED_LEARNER_DASHBOARD_PAGE_PATHS,
  LEARNER_DASHBOARD_REEXPORT_PAGE_PATHS,
} from '../learner-dashboard-route-policy';

function readRepoFile(relativePath: string) {
  return readFileSync(path.join(process.cwd(), relativePath), 'utf8');
}

const LEARNER_GUTTER_OVERRIDE_AUDIT: Array<{ pagePath: string; forbidden: string[] }> = [
  {
    pagePath: 'app/reading/page.tsx',
    forbidden: ['className="max-w-5xl mx-auto px-4 sm:px-6 lg:px-8 py-8 space-y-10"'],
  },
  {
    pagePath: 'app/submissions/page.tsx',
    forbidden: ['className="max-w-4xl mx-auto px-4 sm:px-6 lg:px-8 py-8 space-y-8"'],
  },
  {
    pagePath: 'app/settings/page.tsx',
    forbidden: ['className="max-w-3xl mx-auto px-4 sm:px-6 lg:px-8 py-8 space-y-8"'],
  },
  {
    pagePath: 'app/mocks/report/[id]/page.tsx',
    forbidden: [
      'className="max-w-3xl mx-auto px-4 py-8"',
      'className="max-w-3xl mx-auto px-4 sm:px-6 lg:px-8 py-8 space-y-8"',
    ],
  },
];

describe('learner dashboard route policy', () => {
  it('keeps included learner pages on the shared dashboard shell', () => {
    for (const pagePath of INCLUDED_LEARNER_DASHBOARD_PAGE_PATHS) {
      const content = readRepoFile(pagePath);
      expect(content, `${pagePath} should adopt LearnerDashboardShell`).toContain('LearnerDashboardShell');
    }
  });

  it('keeps excluded immersive learner pages off the shared dashboard shell', () => {
    for (const pagePath of EXCLUDED_IMMERSIVE_LEARNER_PAGE_PATHS) {
      const content = readRepoFile(pagePath);
      expect(content, `${pagePath} should remain off LearnerDashboardShell`).not.toContain('LearnerDashboardShell');
    }
  });

  it('preserves learner dashboard alias routes as re-exports', () => {
    for (const pagePath of LEARNER_DASHBOARD_REEXPORT_PAGE_PATHS) {
      const content = readRepoFile(pagePath);
      expect(content, `${pagePath} should remain a re-export`).toContain("export { default } from '../../page';");
    }
  });

  it('does not allow page-root gutter overrides on audited learner routes', () => {
    for (const { pagePath, forbidden } of LEARNER_GUTTER_OVERRIDE_AUDIT) {
      const content = readRepoFile(pagePath);
      for (const legacyWrapper of forbidden) {
        expect(content, `${pagePath} should not reintroduce ${legacyWrapper}`).not.toContain(legacyWrapper);
      }
    }
  });
});
