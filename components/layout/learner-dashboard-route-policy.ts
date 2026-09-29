export const INCLUDED_LEARNER_DASHBOARD_PAGE_PATHS = [
  'app/(learner)/page.tsx',
  'app/(learner)/dashboard/page.tsx',
  'app/(learner)/achievements/page.tsx',
  'app/(learner)/billing/page.tsx',
  'app/(learner)/companion/page.tsx',
  'app/(learner)/conversation/page.tsx',
  'app/(learner)/goals/page.tsx',
  'app/(learner)/grammar/page.tsx',
  'app/(learner)/videos/page.tsx',
  'app/(learner)/videos/[id]/page.tsx',
  'app/(learner)/onboarding/page.tsx',
  'app/(learner)/progress/page.tsx',
  'app/(learner)/readiness/page.tsx',
  'app/(learner)/study-plan/page.tsx',
  'app/(learner)/settings/page.tsx',
  'app/(learner)/settings/[section]/page.tsx',
  'app/(learner)/submissions/page.tsx',
  'app/(learner)/submissions/[id]/page.tsx',
  'app/(learner)/submissions/compare/page.tsx',
  'app/(learner)/listening/page.tsx',
  'app/(learner)/listening/drills/[id]/page.tsx',
  'app/(learner)/listening/paper/[paperId]/page.tsx',
  'app/(learner)/listening/results/[id]/page.tsx',
  'app/(learner)/listening/review/[id]/page.tsx',
  'app/(learner)/mocks/page.tsx',
  'app/(learner)/mocks/player/[id]/page.tsx',
  'app/(learner)/mocks/report/[id]/page.tsx',
  'app/(learner)/mocks/setup/page.tsx',
  'app/(learner)/reading/page.tsx',
  'app/(learner)/reading/paper/[paperId]/page.tsx',
  'app/(learner)/reading/paper/[paperId]/results/page.tsx',
  'app/(learner)/speaking/page.tsx',
  'app/(learner)/speaking/expert-review/[id]/page.tsx',
  'app/(learner)/speaking/phrasing/[id]/page.tsx',
  'app/(learner)/speaking/results/[id]/page.tsx',
  'app/(learner)/speaking/roleplay/[id]/page.tsx',
  'app/(learner)/speaking/selection/page.tsx',
  'app/(learner)/speaking/transcript/[id]/page.tsx',
  'app/(learner)/writing/page.tsx',
  'app/(learner)/writing/expert-request/page.tsx',
  'app/(learner)/writing/model/page.tsx',
  'app/(learner)/writing/result/page.tsx',
  'app/(learner)/recalls/page.tsx',
  'app/(learner)/recalls/words/page.tsx',
  'app/(learner)/recalls/favourites/page.tsx',
  'app/(learner)/strategies/page.tsx',
  'app/(learner)/strategies/[id]/page.tsx',
] as const;

export const EXCLUDED_IMMERSIVE_LEARNER_PAGE_PATHS = [
  'app/(learner)/mocks/player/[id]/page.tsx',
  'app/(learner)/reading/player/[id]/page.tsx',
  'app/(learner)/listening/player/[id]/page.tsx',
  'app/(learner)/writing/feedback/page.tsx',
  'app/(learner)/speaking/task/[id]/page.tsx',
] as const;

export const LEARNER_DASHBOARD_REEXPORT_PAGE_PATHS = [
  'app/(learner)/dashboard/project/page.tsx',
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
  '/companion',
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

/**
 * Timed or live attempt routes not covered by the immersive list, as URL
 * patterns. Together with the immersive routes above they are the one list of
 * exam/live routes: nothing on them animates (DESIGN.md §5). Kept honest by
 * `__tests__/learner-breadcrumb-routability.test.ts`, which walks `app/`.
 */
export const EXAM_LIVE_ROUTE_PATTERNS = [
  '/reading/paper/[paperId]',
  '/listening/paper/[paperId]',
  '/writing/paper/session/[id]',
  '/writing/mocks/session/[id]',
  '/writing/practice/session/[scenarioId]',
  '/mocks/writing/[sectionAttemptId]',
  '/mocks/speaking-room/[bookingId]',
  '/speaking/task/[id]',
  '/speaking/exam/[id]',
  '/speaking/sessions/[id]/live-tutor',
  '/expert/speaking-room/[bookingId]',
  '/expert/speaking/live-room/[id]',
  '/expert/speaking/exam/[examId]',
] as const;

export function isExamOrLiveRoute(pathname: string | null | undefined) {
  const normalized = normalizeLearnerPathname(pathname);

  return isImmersiveLearnerRoute(normalized)
    || EXAM_LIVE_ROUTE_PATTERNS.some((pattern) => matchesRoutePattern(pattern, normalized));
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
