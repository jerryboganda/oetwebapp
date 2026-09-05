export const INCLUDED_LEARNER_DASHBOARD_PAGE_PATHS = [
  'app/page.tsx',
  'app/dashboard/page.tsx',
  'app/achievements/page.tsx',
  'app/billing/page.tsx',
  'app/conversation/page.tsx',
  'app/goals/page.tsx',
  'app/grammar/page.tsx',
  'app/videos/page.tsx',
  'app/videos/[id]/page.tsx',
  'app/onboarding/page.tsx',
  'app/progress/page.tsx',
  'app/readiness/page.tsx',
  'app/study-plan/page.tsx',
  'app/settings/page.tsx',
  'app/settings/[section]/page.tsx',
  'app/submissions/page.tsx',
  'app/submissions/[id]/page.tsx',
  'app/submissions/compare/page.tsx',
  'app/listening/page.tsx',
  'app/listening/drills/[id]/page.tsx',
  'app/listening/paper/[paperId]/page.tsx',
  'app/listening/results/[id]/page.tsx',
  'app/listening/review/[id]/page.tsx',
  'app/mocks/page.tsx',
  'app/mocks/player/[id]/page.tsx',
  'app/mocks/report/[id]/page.tsx',
  'app/mocks/setup/page.tsx',
  'app/reading/page.tsx',
  'app/reading/paper/[paperId]/page.tsx',
  'app/reading/paper/[paperId]/results/page.tsx',
  'app/speaking/page.tsx',
  'app/speaking/expert-review/[id]/page.tsx',
  'app/speaking/phrasing/[id]/page.tsx',
  'app/speaking/results/[id]/page.tsx',
  'app/speaking/roleplay/[id]/page.tsx',
  'app/speaking/selection/page.tsx',
  'app/speaking/transcript/[id]/page.tsx',
  'app/writing/page.tsx',
  'app/writing/expert-request/page.tsx',
  'app/writing/model/page.tsx',
  'app/writing/result/page.tsx',
  'app/recalls/page.tsx',
  'app/recalls/words/page.tsx',
  'app/recalls/favourites/page.tsx',
  'app/strategies/page.tsx',
  'app/strategies/[id]/page.tsx',
] as const;

export const EXCLUDED_IMMERSIVE_LEARNER_PAGE_PATHS = [
  'app/mocks/player/[id]/page.tsx',
  'app/reading/player/[id]/page.tsx',
  'app/listening/player/[id]/page.tsx',
  'app/writing/feedback/page.tsx',
  'app/speaking/task/[id]/page.tsx',
] as const;

export const LEARNER_DASHBOARD_REEXPORT_PAGE_PATHS = [
  'app/dashboard/project/page.tsx',
] as const;

/**
 * Learner paths that exist only to namespace a dynamic child (e.g.
 * `/speaking/roleplay` is just the parent folder of `/speaking/roleplay/[id]`).
 * They have no `page.tsx`, so a breadcrumb that links them is a guaranteed 404.
 * Kept honest by `__tests__/learner-breadcrumb-routability.test.ts`, which walks
 * `app/` and fails if this list drifts from the filesystem.
 */
export const NON_ROUTABLE_LEARNER_PATHS = [
  '/listening/drills',
  '/listening/paper',
  '/listening/player',
  '/listening/practice',
  '/listening/review',
  '/reading/community',
  '/reading/paper',
  '/reading/parts',
  '/reading/player',
  '/reading/results',
  '/speaking/expert-review',
  '/speaking/phrasing',
  '/speaking/roleplay',
  '/speaking/sessions',
  '/speaking/task',
  '/speaking/transcript',
  '/writing/mocks/session',
  '/writing/paper',
  '/writing/practice',
  '/writing/practice/session',
  '/writing/submissions',
  '/writing/tools',
] as const;

const NON_ROUTABLE_LEARNER_PATH_SET = new Set<string>(NON_ROUTABLE_LEARNER_PATHS);

const LEARNER_WORKSPACE_ROUTE_ROOTS = [
  '/',
  '/dashboard',
  '/achievements',
  '/billing',
  '/conversation',
  '/goals',
  '/grammar',
  '/videos',
  '/listening',
  '/mocks',
  '/onboarding',
  '/progress',
  '/readiness',
  '/reading',
  '/recalls',
  '/settings',
  '/speaking',
  '/strategies',
  '/study-plan',
  '/submissions',
  '/writing',
] as const;

function normalizeLearnerPathname(pathname: string | null | undefined) {
  if (!pathname) return '/';
  const [pathWithoutQuery] = pathname.split(/[?#]/);
  const normalized = pathWithoutQuery.replace(/\/+$/, '');
  return normalized || '/';
}

function appPagePathToRoutePattern(pagePath: string) {
  const withoutAppPrefix = pagePath
    .replace(/^app\//, '')
    .replace(/\/page\.tsx$/, '')
    .replace(/\([^/]+\)\//g, '');

  if (withoutAppPrefix === '' || withoutAppPrefix === 'page.tsx') {
    return '/';
  }

  return `/${withoutAppPrefix}`;
}

function routeSegments(route: string) {
  return normalizeLearnerPathname(route).split('/').filter(Boolean);
}

function matchesRoutePattern(pattern: string, pathname: string) {
  const patternSegments = routeSegments(pattern);
  const pathSegments = routeSegments(pathname);

  if (patternSegments.length !== pathSegments.length) {
    return false;
  }

  return patternSegments.every((segment, index) => {
    if (segment.startsWith('[') && segment.endsWith(']')) {
      return pathSegments[index]?.length > 0;
    }

    return segment === pathSegments[index];
  });
}

export function isImmersiveLearnerRoute(pathname: string | null | undefined) {
  const normalized = normalizeLearnerPathname(pathname);

  return EXCLUDED_IMMERSIVE_LEARNER_PAGE_PATHS.some((pagePath) =>
    matchesRoutePattern(appPagePathToRoutePattern(pagePath), normalized),
  );
}

export function isLearnerWorkspaceRoute(pathname: string | null | undefined) {
  const normalized = normalizeLearnerPathname(pathname);

  return LEARNER_WORKSPACE_ROUTE_ROOTS.some((routeRoot) => {
    if (routeRoot === '/') {
      return normalized === '/';
    }

    return normalized === routeRoot || normalized.startsWith(`${routeRoot}/`);
  });
}

export function shouldShowLearnerBreadcrumbs(pathname: string | null | undefined) {
  const normalized = normalizeLearnerPathname(pathname);

  if (normalized === '/' || normalized === '/dashboard') {
    return false;
  }

  return isLearnerWorkspaceRoute(normalized) && !isImmersiveLearnerRoute(normalized);
}

/**
 * False for a folder-only path that has no page of its own. Breadcrumbs must
 * render these as plain text — linking them navigates the learner into a 404.
 */
export function isRoutableLearnerPath(pathname: string | null | undefined) {
  return !NON_ROUTABLE_LEARNER_PATH_SET.has(normalizeLearnerPathname(pathname));
}
