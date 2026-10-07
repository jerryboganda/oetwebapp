/**
 * Learner paths that exist only to namespace a dynamic child (e.g.
 * `/speaking/roleplay` is just the parent folder of `/speaking/roleplay/[id]`).
 * They have no `page.tsx`, so a breadcrumb that links them is a guaranteed 404.
 * The check that kept this list honest against `app/`,
 * `__tests__/learner-breadcrumb-routability.test.ts`, was deleted 2026-10-08, so
 * nothing checks it now.
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
  '/vocabulary/terms',
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
  '/materials',
  '/mocks',
  '/onboarding',
  '/progress',
  '/pronunciation',
  '/readiness',
  '/reading',
  '/recalls',
  '/settings',
  '/speaking',
  '/strategies',
  '/study-plan',
  '/submissions',
  '/vocabulary',
  '/writing',
] as const;

function normalizeLearnerPathname(pathname: string | null | undefined) {
  if (!pathname) return '/';
  const [pathWithoutQuery] = pathname.split(/[?#]/);
  const normalized = pathWithoutQuery.replace(/\/+$/, '');
  return normalized || '/';
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

/** Player routes that never show learner breadcrumbs, as URL patterns. */
export const IMMERSIVE_LEARNER_ROUTE_PATTERNS = [
  '/mocks/player/[id]',
  '/reading/player/[id]',
  '/listening/player/[id]',
  '/writing/feedback',
  '/speaking/task/[id]',
] as const;

export function isImmersiveLearnerRoute(pathname: string | null | undefined) {
  const normalized = normalizeLearnerPathname(pathname);

  return IMMERSIVE_LEARNER_ROUTE_PATTERNS.some((pattern) => matchesRoutePattern(pattern, normalized));
}

/**
 * Timed or live attempt routes not covered by the immersive list, as URL
 * patterns. Together with the immersive routes above they are the one list of
 * exam/live routes: nothing on them animates (DESIGN.md §5). The check that kept this honest,
 * `__tests__/learner-breadcrumb-routability.test.ts`, was deleted 2026-10-08.
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

/**
 * How the learner chrome treats a route under `app/(learner)`:
 * - `workspace`: TopNav, Sidebar, BottomNav and breadcrumbs.
 * - `focus`: distraction-free header only, titled from the page's own copy.
 * - `none`: the page renders its own shell, or none at all.
 * The check that kept this in step with the pages,
 * `__tests__/learner-shell-policy-parity.test.ts`, was deleted 2026-10-08.
 */
export type LearnerChromeMode = 'workspace' | 'focus' | 'none';

export interface LearnerChrome {
  mode: LearnerChromeMode;
  requireAuth: boolean;
  title?: string;
  titleKey?: string;
  examOrLive: boolean;
}

/**
 * Pages that render AppShell, LearnerLiveRoomShell or a local shell themselves,
 * or no shell: attempt/live screens and a transient payment return. Content,
 * hub, results and product pages belong in the workspace chrome so the
 * learner always has navigation.
 */
export const LEARNER_SELF_CHROMED_ROUTES = [
  '/billing/payment-return',
  '/listening/mocks/[sessionId]',
  '/listening/player/[id]',
  '/speaking/exam/[id]',
  '/speaking/sessions/[id]',
  '/speaking/sessions/[id]/live-tutor',
  '/speaking/sessions/[id]/prep',
  '/speaking/task/[id]',
  '/writing/feedback',
] as const;

/** Distraction-free pages. `title`/`titleKey` is the literal or `t()` key the page passes as `pageTitle`. */
export const LEARNER_FOCUS_ROUTES: ReadonlyArray<{ route: string; title?: string; titleKey?: string }> = [
  { route: '/onboarding', title: 'Getting Started' },
  { route: '/placement-test', title: 'Placement Test' },
  { route: '/writing/mocks/session/[id]', titleKey: 'writing.mocks.session.pageTitle' },
  { route: '/writing/paper/session/[id]', titleKey: 'writing.paper.pageTitle' },
  { route: '/writing/practice/session/[scenarioId]', titleKey: 'writing.practice.session.pageTitle' },
];

/** Workspace pages that render without a session (also public in `proxy.ts`). */
export const LEARNER_PUBLIC_ROUTES = [
  '/speaking/assessment-criteria',
  '/speaking/intro-questions',
] as const;

export function resolveLearnerChrome(pathname: string | null | undefined): LearnerChrome {
  const normalized = normalizeLearnerPathname(pathname);
  const matches = (pattern: string) => matchesRoutePattern(pattern, normalized);
  const examOrLive = isExamOrLiveRoute(normalized);

  if (LEARNER_SELF_CHROMED_ROUTES.some(matches)) {
    return { mode: 'none', requireAuth: true, examOrLive };
  }

  const focus = LEARNER_FOCUS_ROUTES.find(({ route }) => matches(route));
  if (focus) {
    return { mode: 'focus', requireAuth: true, title: focus.title, titleKey: focus.titleKey, examOrLive };
  }

  return { mode: 'workspace', requireAuth: !LEARNER_PUBLIC_ROUTES.some(matches), examOrLive };
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
