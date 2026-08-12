/**
 * Convert a server response timestamp into a client-side clock correction.
 * The server remains authoritative for all expiry and grading decisions; the
 * correction is only for making learner-facing countdowns and boundaries line
 * up with the server clock when the device clock is inaccurate.
 */
export function readServerClockOffsetMs(serverNow?: string | null): number {
  if (!serverNow) return 0;
  const parsed = Date.parse(serverNow);
  return Number.isFinite(parsed) ? parsed - Date.now() : 0;
}

export function correctedNowMs(serverClockOffsetMs = 0): number {
  return Date.now() + serverClockOffsetMs;
}
