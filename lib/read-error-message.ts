/**
 * Extract a human-readable message from an unknown caught error.
 *
 * Handles the backend JSON-problem shape `{ detail: { message, error } }`,
 * `userMessage` field (used by auth endpoints), standard `Error` instances,
 * and falls back to a configurable default.
 */
export function readErrorMessage(
  err: unknown,
  fallback = 'Something went wrong. Please try again.',
): string {
  const detail = (err as { detail?: { message?: string; error?: string } })?.detail;
  if (detail?.message) return detail.message;
  if (detail?.error) return detail.error;
  if (err && typeof err === 'object' && 'userMessage' in err && typeof (err as { userMessage: unknown }).userMessage === 'string') {
    return (err as { userMessage: string }).userMessage;
  }
  if (err instanceof Error) return err.message;
  if (err && typeof err === 'object' && 'message' in err && typeof (err as { message: unknown }).message === 'string') {
    return (err as { message: string }).message;
  }
  return fallback;
}
