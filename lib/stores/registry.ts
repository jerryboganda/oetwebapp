/**
 * Client store-reset registry (FE-001).
 *
 * Any client-side store (Zustand, etc.) that holds user-scoped state registers
 * its reset function here on module load. `auth-context.signOut()` then calls
 * `resetAllStores()` from a single choke point so EVERY store — including
 * persisted ones (localStorage) — is cleared on logout. Without this, the next
 * user on a shared device can see the previous user's cached data / review
 * drafts (`expert-console-store`).
 *
 * Kept dependency-free and synchronous so it is safe to call inside the
 * `finally` of a logout flow that may itself be mid-failure.
 */
type ResetFn = () => void;

const resetFns = new Set<ResetFn>();

/** Register a store's reset routine. Idempotent (Set-deduped). */
export function registerResettable(fn: ResetFn): void {
  resetFns.add(fn);
}

/** Run every registered reset. One throwing store never blocks the others. */
export function resetAllStores(): void {
  for (const fn of resetFns) {
    try {
      fn();
    } catch {
      // A failed reset must never prevent logout from completing.
    }
  }
}
