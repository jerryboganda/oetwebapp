/**
 * Derives the AI Learning Companion's surface hint from the current route.
 *
 * The companion answers "explain *this* question" and "what does the examiner
 * want *here*" by knowing which page the learner is on. This module turns a
 * pathname into the identifiers the server understands.
 *
 * **Identifiers only, never content.** The server decides what each id means and
 * whether the learner may see it (docs/ai-learning-companion/). Nothing here can
 * widen entitlement, and exam mode is resolved server-side from attempt state —
 * a learner cannot unlock hints by editing or withholding any of this.
 */

/** Bounded surface hint sent with a turn. Mirrors `CompanionContextEnvelope`. */
export interface CompanionSurfaceContext {
  surface?: string;
  routeId?: string;
  resourceId?: string;
  attemptId?: string;
  questionId?: string;
  videoTimeSeconds?: number;
  subtestCode?: string;
}

/** Route roots that map to an OET subtest, for retrieval bias. */
const SUBTEST_BY_ROOT: Record<string, string> = {
  writing: 'writing',
  speaking: 'speaking',
  reading: 'reading',
  listening: 'listening',
  pronunciation: 'speaking',
};

/**
 * Route segments that carry an id we can pass along. Anything not listed yields
 * no resource id at all — an allowlist, so a new route cannot start leaking
 * path segments to the model by accident.
 */
const RESOURCE_ROOTS = new Set([
  'writing',
  'speaking',
  'reading',
  'listening',
  'pronunciation',
  'mocks',
  'videos',
  'materials',
  'strategies',
  'grammar',
  'recalls',
  'submissions',
  'review',
]);

/** Segments that introduce an attempt rather than a content resource. */
const ATTEMPT_SEGMENTS = new Set(['attempt', 'attempts', 'player', 'task', 'roleplay', 'paper']);

/** Looks like an id worth sending: a guid, a slug, or a numeric id. */
function isIdLike(segment: string): boolean {
  if (!segment || segment.length > 64) return false;
  return /^[a-zA-Z0-9][a-zA-Z0-9_-]*$/.test(segment) && /[0-9a-f]{6,}|^\d+$/i.test(segment);
}

/**
 * Builds the hint for a pathname.
 *
 * @param pathname   Current route, e.g. `/reading/paper/rp-12/results`.
 * @param videoTimeSeconds Playback position, when a video is on screen.
 */
export function buildSurfaceContext(
  pathname: string | null | undefined,
  videoTimeSeconds?: number,
): CompanionSurfaceContext | undefined {
  if (!pathname) return undefined;

  const segments = pathname.split('?')[0].split('#')[0].split('/').filter(Boolean);
  if (segments.length === 0) return { surface: 'dashboard', routeId: '/' };

  const root = segments[0].toLowerCase();

  const context: CompanionSurfaceContext = {
    // A readable name for the prompt ("Currently viewing: reading/paper"),
    // deliberately without ids so the prompt line stays stable and legible.
    surface: segments.filter((s) => !isIdLike(s)).join('/') || root,
    routeId: `/${segments.join('/')}`,
  };

  const subtest = SUBTEST_BY_ROOT[root];
  if (subtest) context.subtestCode = subtest;

  if (RESOURCE_ROOTS.has(root)) {
    for (let i = 1; i < segments.length; i += 1) {
      const segment = segments[i];
      if (!isIdLike(segment)) continue;

      const previous = segments[i - 1].toLowerCase();
      if (ATTEMPT_SEGMENTS.has(previous)) {
        context.attemptId ??= segment;
      } else if (previous === 'question' || previous === 'questions') {
        context.questionId ??= segment;
      } else {
        context.resourceId ??= segment;
      }
    }
  }

  if (typeof videoTimeSeconds === 'number' && Number.isFinite(videoTimeSeconds) && videoTimeSeconds >= 0) {
    context.videoTimeSeconds = Math.floor(videoTimeSeconds);
  }

  return context;
}
