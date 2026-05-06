export const INCLUDED_LEARNER_DASHBOARD_PAGE_PATHS = [
  'app/page.tsx',
  'app/dashboard/page.tsx',
  'app/achievements/page.tsx',
  'app/billing/page.tsx',
  'app/conversation/page.tsx',
  'app/diagnostic/page.tsx',
  'app/diagnostic/hub/page.tsx',
  'app/diagnostic/results/page.tsx',
  'app/goals/page.tsx',
  'app/grammar/page.tsx',
  'app/lessons/page.tsx',
  'app/lessons/[id]/page.tsx',
  'app/lessons/discover/page.tsx',
  'app/lessons/programs/[programId]/page.tsx',
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
  'app/speaking/check/page.tsx',
  'app/speaking/expert-review/[id]/page.tsx',
  'app/speaking/phrasing/[id]/page.tsx',
  'app/speaking/results/[id]/page.tsx',
  'app/speaking/roleplay/[id]/page.tsx',
  'app/speaking/selection/page.tsx',
  'app/speaking/transcript/[id]/page.tsx',
  'app/writing/page.tsx',
  'app/writing/expert-request/page.tsx',
  'app/writing/library/page.tsx',
  'app/writing/model/page.tsx',
  'app/writing/result/page.tsx',
  'app/writing/revision/page.tsx',
  'app/recalls/page.tsx',
  'app/recalls/words/page.tsx',
  'app/recalls/cards/page.tsx',
  'app/recalls/library/page.tsx',
  'app/strategies/page.tsx',
  'app/strategies/[id]/page.tsx',
] as const;

export const EXCLUDED_IMMERSIVE_LEARNER_PAGE_PATHS = [
  'app/diagnostic/listening/page.tsx',
  'app/diagnostic/reading/page.tsx',
  'app/diagnostic/speaking/page.tsx',
  'app/diagnostic/writing/page.tsx',
  'app/mocks/player/[id]/page.tsx',
  'app/reading/player/[id]/page.tsx',
  'app/listening/player/[id]/page.tsx',
  'app/writing/player/page.tsx',
  'app/writing/feedback/page.tsx',
  'app/speaking/task/[id]/page.tsx',
] as const;

export const LEARNER_DASHBOARD_REEXPORT_PAGE_PATHS = [
  'app/dashboard/project/page.tsx',
] as const;

const LEARNER_WORKSPACE_ROUTE_ROOTS = [
  '/',
  '/dashboard',
  '/achievements',
  '/billing',
  '/conversation',
  '/diagnostic',
  '/goals',
  '/grammar',
  '/lessons',
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
