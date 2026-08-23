/**
 * Session-scoped record of which email already had a verification OTP
 * auto-sent for it.
 *
 * Lives at module scope — NOT inside the React component — because the exact
 * bug this guards against is a remount: on Android/iOS the OS kills the
 * backgrounded WebView while the learner checks their mail app, Capacitor
 * cold-reloads the page on return, every ref dies with the old tree, and the
 * mount effect used to fire again and silently rotate the OTP. A module-level
 * value survives that reload within the same WebView session.
 *
 * This is the fallback layer when the localStorage challenge record is
 * unavailable (private mode / blocked storage). The backend additionally
 * reuses any still-valid unexpired challenge when forceNew is false.
 */
let autoSendRequestedForEmail: string | null = null;

export function markAutoSendRequested(email: string): void {
  autoSendRequestedForEmail = email;
}

export function hasAutoSendRequested(email: string): boolean {
  return autoSendRequestedForEmail === email;
}

export function clearAutoSendRequest(): void {
  autoSendRequestedForEmail = null;
}
