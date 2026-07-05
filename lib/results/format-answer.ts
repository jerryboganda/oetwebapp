/**
 * Shared formatting helpers for the results / review surfaces.
 *
 * Reading stores a learner answer as `unknown` (string, string[], or object,
 * depending on question type) while Listening/Speaking store plain strings.
 * Centralising the display formatting here keeps every module's "Your answer"
 * cell identical.
 */

export function formatAnswerValue(value: unknown): string {
  if (value === null || value === undefined || value === '') return 'No answer';
  if (Array.isArray(value)) {
    const parts = value
      .filter((v) => v !== null && v !== undefined && v !== '')
      .map((v) => (typeof v === 'object' ? JSON.stringify(v) : String(v)));
    return parts.length ? parts.join(', ') : 'No answer';
  }
  if (typeof value === 'object') {
    try {
      return JSON.stringify(value);
    } catch {
      return String(value);
    }
  }
  return String(value);
}

/** Human-friendly duration from milliseconds, e.g. `45s`, `2m`, `2m 5s`. */
export function formatDurationMs(ms: number): string {
  const totalSeconds = Math.round(ms / 1000);
  if (totalSeconds < 60) return `${totalSeconds}s`;
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return seconds === 0 ? `${minutes}m` : `${minutes}m ${seconds}s`;
}
