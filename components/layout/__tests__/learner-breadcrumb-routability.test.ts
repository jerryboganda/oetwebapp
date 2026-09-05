import fs from 'node:fs';
import path from 'node:path';

import {
  NON_ROUTABLE_LEARNER_PATHS,
  isRoutableLearnerPath,
  shouldShowLearnerBreadcrumbs,
} from '../learner-dashboard-route-policy';

// Matches the filesystem-walking convention in lib/__tests__/admin-contract.test.ts.
const APP_DIR = path.join(process.cwd(), 'app');

// Mirrors LEARNER_WORKSPACE_ROUTE_ROOTS: breadcrumbs only render under these.
const LEARNER_ROOTS = [
  '/dashboard', '/achievements', '/billing', '/conversation', '/goals', '/grammar',
  '/videos', '/listening', '/mocks', '/onboarding', '/progress', '/readiness',
  '/reading', '/recalls', '/settings', '/speaking', '/strategies', '/study-plan',
  '/submissions', '/writing',
];

function collectRoutes(dir: string, url: string, out: Set<string>) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (!entry.isDirectory() || entry.name.startsWith('_')) continue;
    // Route groups `(foo)` don't contribute a URL segment.
    const segment = /^\(.*\)$/.test(entry.name) ? '' : `/${entry.name}`;
    const child = path.join(dir, entry.name);
    const childUrl = url + segment;
    if (fs.readdirSync(child).some((file) => /^page\.(tsx|jsx|ts|js|mdx)$/.test(file))) {
      out.add(childUrl || '/');
    }
    collectRoutes(child, childUrl, out);
  }
}

/** Every folder-only ancestor a learner breadcrumb could link to. */
function findNonRoutableLearnerAncestors() {
  const routes = new Set<string>();
  collectRoutes(APP_DIR, '', routes);

  const patterns = [...routes].map((route) => route.split('/').filter(Boolean));
  const isRoute = (segments: string[]) =>
    patterns.some(
      (pattern) =>
        pattern.length === segments.length &&
        pattern.every((part, i) => part === segments[i] || /^\[.*\]$/.test(part)),
    );

  const dead = new Set<string>();
  for (const route of routes) {
    const segments = route.split('/').filter(Boolean);
    for (let i = 1; i < segments.length; i += 1) {
      const ancestor = segments.slice(0, i);
      // A dynamic ancestor's href depends on the id, so it is never a static folder link.
      if (ancestor.some((part) => /^\[.*\]$/.test(part))) continue;
      const href = `/${ancestor.join('/')}`;
      const underLearnerRoot = LEARNER_ROOTS.some((root) => href === root || href.startsWith(`${root}/`));
      if (underLearnerRoot && !isRoute(ancestor)) dead.add(href);
    }
  }
  return [...dead].sort();
}

describe('learner breadcrumb routability', () => {
  it('lists exactly the learner paths that have no page of their own', () => {
    // Fails the moment someone adds a nested route without an index page, or
    // adds the missing index page, so the hardcoded list cannot silently rot.
    expect([...NON_ROUTABLE_LEARNER_PATHS].sort()).toEqual(findNonRoutableLearnerAncestors());
  });

  it('never links a breadcrumb ancestor that would 404', () => {
    // The regression that shipped: /speaking/roleplay/[id] rendered a
    // "Roleplay" crumb linking to /speaking/roleplay, which has no page.
    expect(shouldShowLearnerBreadcrumbs('/speaking/roleplay/ci-seed-speaking-medicine-04')).toBe(true);
    expect(isRoutableLearnerPath('/speaking/roleplay')).toBe(false);
    expect(isRoutableLearnerPath('/speaking/sessions')).toBe(false);
  });

  it('still links ancestors that really are pages', () => {
    expect(isRoutableLearnerPath('/speaking')).toBe(true);
    expect(isRoutableLearnerPath('/speaking/selection')).toBe(true);
    expect(isRoutableLearnerPath('/listening')).toBe(true);
    expect(isRoutableLearnerPath('/writing')).toBe(true);
  });
});
