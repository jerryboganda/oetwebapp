/**
 * Writing result-release window (15 minutes) — pure helpers shared by every
 * surface that waits for, or opens, a Writing result.
 *
 * The countdown is anchored to the SERVER clock difference
 * (`releaseAt - serverNow`), never to the device clock, so a wrong device
 * clock, a refresh, an app restart or another device cannot move it.
 */

/** Candidate copy for the whole window; the text lives in messages/{en,ar}/writing.json. */
export const WRITING_RELEASE_NOTICE_KEY = 'writing.release.notice';

/**
 * Epoch-ms deadline for the release, re-anchored on every fetch:
 * `receivedAtMs + (releaseAt - serverNow)`. Null when there is nothing to wait
 * for (no `releaseAt`) or a timestamp does not parse.
 */
export function releaseDeadlineMs(
  releaseAt: string | null | undefined,
  serverNow: string | undefined,
  receivedAtMs: number = Date.now(),
): number | null {
  if (!releaseAt) return null;
  const release = Date.parse(releaseAt);
  const server = Date.parse(serverNow ?? '');
  if (!Number.isFinite(release) || !Number.isFinite(server)) return null;
  return receivedAtMs + (release - server);
}

/**
 * True when the result may be shown. An old API (no `releaseState`) falls back
 * to the plain `graded` status.
 */
export function isReleased(dto: { status?: string; releaseState?: string | null }): boolean {
  return dto.releaseState ? dto.releaseState === 'released' : dto.status === 'graded';
}
