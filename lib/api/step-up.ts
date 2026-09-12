/**
 * Admin step-up (re-authentication) proof for money-moving actions.
 *
 * The backend gates high-risk admin endpoints behind a fresh TOTP check: the
 * caller gets `403 { code: 'step_up_required' }` until it proves possession by
 * exchanging a live authenticator code at `POST /v1/auth/step-up`, then echoing
 * the returned token back in the `X-OET-Step-Up` header.
 *
 * The issued token is a live credential, so it is cached in module memory only
 * (never localStorage/sessionStorage) — one code entry can therefore authorise
 * several actions within the same few minutes. `lib/api/client.ts` looks the
 * token up per request; this module deliberately does not import `client.ts` at
 * module scope, so the two never form a static import cycle.
 */

export interface StepUpProof {
  stepUpToken: string;
  expiresAt: string;
  scope: string;
}

/** Treat a token as expired this long before its real `expiresAt` (clock skew). */
const STEP_UP_SAFETY_MARGIN_MS = 10_000;

const stepUpTokens = new Map<string, { token: string; expiresAt: number }>();

function rememberStepUpToken(proof: StepUpProof): void {
  const expiresAt = Date.parse(proof.expiresAt);
  stepUpTokens.set(proof.scope, {
    token: proof.stepUpToken,
    expiresAt: Number.isFinite(expiresAt) ? expiresAt : Date.now(),
  });
}

/** Returns the cached token for `scope` while it is still (safely) unexpired. */
export function getStepUpToken(scope: string): string | null {
  const entry = stepUpTokens.get(scope);
  if (!entry) return null;

  if (entry.expiresAt - Date.now() <= STEP_UP_SAFETY_MARGIN_MS) {
    stepUpTokens.delete(scope);
    return null;
  }

  return entry.token;
}

/** Drop every cached proof — call on sign-out so no token outlives the session. */
export function clearStepUpTokens(): void {
  stepUpTokens.clear();
}

export async function requestStepUp(scope: string, code: string): Promise<StepUpProof> {
  const { apiRequest } = await import('./client');
  const proof = await apiRequest<StepUpProof>('/v1/auth/step-up', {
    method: 'POST',
    body: JSON.stringify({ code, scope }),
  });

  rememberStepUpToken(proof);
  return proof;
}

export function isStepUpRequiredError(error: unknown): boolean {
  return error instanceof Error && (error as { code?: unknown }).code === 'step_up_required';
}
