/**
 * Shared HTTP pipeline for all backend calls (extracted from `lib/api.ts`).
 *
 * Single home for URL resolution, auth/CSRF/device headers, retries, and
 * `ApiError` mapping so domain modules under `lib/api/` import it instead of
 * duplicating resolvers. `lib/api.ts` re-exports the public surface, so the
 * `@/lib/api` import surface is unchanged.
 */
import { ensureFreshAccessToken } from '../auth-client';
import { navigateAuthOnce } from '../navigation/auth-redirect';
import { loadStoredSession } from '../auth-storage';
import { env } from '../env';
import { fetchWithTimeout } from '../network/fetch-with-timeout';
import { getClientIdentitySnapshot } from '../client-version';
import { getDeviceIdForRequest } from '../device-id';

export const API_BASE_URL = env.apiBaseUrl;

export type ApiRecord = Record<string, any>;

export function asRecord(value: unknown): ApiRecord {
  return value && typeof value === 'object' ? (value as ApiRecord) : {};
}

export function asArray(value: unknown): ApiRecord[] {
  return Array.isArray(value) ? value.map(asRecord) : [];
}

export function toStringArray(value: unknown): string[] {
  if (!Array.isArray(value)) return [];
  return value
    .map((item) => {
      if (typeof item === 'string') return item;
      if (typeof item === 'number' || typeof item === 'boolean') return String(item);
      const record = item && typeof item === 'object' ? (item as ApiRecord) : {};
      return record.code ?? record.value ?? record.id ?? record.name ?? null;
    })
    .filter((item): item is string => typeof item === 'string' && item.length > 0);
}

export function toNullableString(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

export function resolveApiUrl(pathOrUrl: string): string {
  if (/^https?:\/\//i.test(pathOrUrl)) {
    return pathOrUrl;
  }

  return `${API_BASE_URL}${pathOrUrl.startsWith('/') ? pathOrUrl : `/${pathOrUrl}`}`;
}

export function resolveBrowserApiResourceUrl(pathOrUrl: string): string | null {
  if (typeof window !== 'undefined' && /^https?:\/\//i.test(pathOrUrl)) {
    try {
      const url = new URL(pathOrUrl);
      if (url.pathname.startsWith('/v1/')) {
        return resolveApiUrl(`${url.pathname}${url.search}`);
      }
    } catch {
      // Fall back to the normal resolver for malformed input.
    }
  }

  return null;
}

export function resolveApiUploadUrl(pathOrUrl: string): string {
  return resolveBrowserApiResourceUrl(pathOrUrl) ?? resolveApiUrl(pathOrUrl);
}

export async function getHeaders(path: string, extra?: HeadersInit, options?: { json?: boolean }): Promise<HeadersInit> {
  const headers = new Headers(extra);
  if (options?.json ?? true) {
    headers.set('Content-Type', 'application/json');
  }

  // Attach CSRF token (double-submit cookie pattern) for mutation requests
  if (typeof document !== 'undefined') {
    const csrfMatch = document.cookie.match(/(?:^|;\s*)oet_csrf=([^;]+)/);
    if (csrfMatch) {
      headers.set('x-csrf-token', csrfMatch[1]);
    }
  }

  // Identify the app shell (desktop/mobile) so the backend can enforce the
  // forced-update gate. Only shells send these headers — a plain web browser
  // never does, so the website is structurally exempt from the 426 gate.
  const identity = getClientIdentitySnapshot();
  if (identity && identity.platform !== 'web') {
    headers.set('X-Client-Platform', identity.platform);
    if (identity.version) {
      headers.set('X-App-Version', identity.version);
    }
  }

  // Bind protected resource calls (especially playback sessions) to the same
  // opaque device identity used at sign-in/refresh.
  const deviceId = await getDeviceIdForRequest();
  if (deviceId) {
    headers.set('X-OET-Device-Id', deviceId);
  }

  try {
    const token = await ensureFreshAccessToken();
    if (token) {
      headers.set('Authorization', `Bearer ${token}`);
    } else if (process.env.NODE_ENV === 'development') {
      console.debug('[API] No auth token available for request to', path);
    }
  } catch (err) {
    if (process.env.NODE_ENV === 'development') {
      console.debug('[API] Failed to retrieve auth token:', err);
    }
  }

  return headers;
}

/** Typed API error with status, error code, retryable flag, and user-friendly message. */
export class ApiError extends Error {
  status: number;
  code: string;
  retryable: boolean;
  userMessage: string;
  fieldErrors: Array<{ field: string; code: string; message: string }>;

  constructor(status: number, code: string, message: string, retryable: boolean, fieldErrors: Array<{ field: string; code: string; message: string }> = []) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.retryable = retryable;
    this.fieldErrors = fieldErrors;
    this.userMessage = mapErrorCodeToUserMessage(code, message);
  }
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError;
}

function mapErrorCodeToUserMessage(code: string, fallback: string): string {
  switch (code) {
    case 'not_authenticated': return 'Your session expired. Please sign in again.';
    case 'unauthorized': return 'Please sign in again to continue.';
    case 'draft_version_conflict': return 'Your draft was updated in another tab. Please refresh and try again.';
    case 'calibration_already_submitted': return 'This calibration case is already finalized and is now locked.';
    case 'idempotency_duplicate': return 'This action was already completed.';
    case 'not_found': return 'The requested resource was not found.';
    case 'forbidden': return 'You do not have permission to perform this action.';
    case 'validation_error': return 'Please check your input and try again.';
    case 'rate_limited': return 'Too many requests. Please wait a moment and try again.';
    case 'internal_server_error': return 'Server encountered an issue processing this request. Tap retry or reload.';
    case 'no_reading_tests':
    case 'no_listening_tests':
    case 'no_ai_package_credits':
    case 'ai_credits_insufficient':
    case 'ai_package_expired':
    case 'no_mock_exams':
    case 'no_credits':
    case 'insufficient_review_credits':
    case 'speaking_exam_insufficient_credits':
      return fallback || 'You do not have enough credits to start this. Purchase a package to continue.';
    default: return fallback;
  }
}

const MAX_RETRIES = 2;
const RETRY_DELAYS = [1000, 3000];

export function isRetryable(status: number): boolean {
  return status >= 500 || status === 408 || status === 429;
}

export async function maybe<T>(promise: Promise<T>, fallback: T | null = null): Promise<T | null> {
  try {
    return await promise;
  } catch (err) {
    if (err instanceof ApiError && (err.status === 404 || err.status === 501)) {
      return fallback;
    }
    throw err;
  }
}

export async function apiRequest<T = any>(path: string, init?: RequestInit, options?: { json?: boolean; acceptedStatuses?: number[]; timeoutMs?: number }): Promise<T> {
  let lastError: Error | null = null;

  for (let attempt = 0; attempt <= MAX_RETRIES; attempt++) {
    try {
      const response = await fetchWithTimeout(resolveApiUrl(path), {
        ...init,
        headers: await getHeaders(path, init?.headers, options),
      }, options?.timeoutMs);

      const acceptedStatuses = options?.acceptedStatuses ?? [];
      if (!response.ok && acceptedStatuses.includes(response.status)) {
        if (response.status === 204) {
          return undefined as T;
        }

        return (await response.json()) as T;
      }

      if (!response.ok) {
        let code = 'unknown_error';
        let message = `Request failed: ${response.status}`;
        let retryable = false;
        let fieldErrors: Array<{ field: string; code: string; message: string }> = [];
        try {
          const error = await response.json();
          code = error.code ?? (response.status === 401 ? 'not_authenticated' : response.status === 403 ? 'forbidden' : code);
          message = error.message ?? error.title ?? message;
          retryable = error.retryable ?? isRetryable(response.status);
          fieldErrors = Array.isArray(error.fieldErrors) ? error.fieldErrors : [];

          // Forced-update gate: the backend rejects out-of-date shells with 426.
          // Broadcast so the AppVersionGateProvider can raise the blocking
          // overlay mid-session, then fall through to throw as a normal ApiError.
          if (response.status === 426 && typeof window !== 'undefined') {
            window.dispatchEvent(new CustomEvent('oet:upgrade-required', { detail: error }));
          }

          // Security spec §4.2 learner hard gate: once the owner flips
          // Security.RequireVerifiedEmailForLearners, unverified learners get
          // this 403 on every learner endpoint — route them to the verify
          // screen (skip if we're already on it to avoid a redirect loop).
          // Single-flight: every concurrent query fails at once, but only
          // the first performs the full document navigation.
          if (
            response.status === 403 &&
            code === 'email_verification_required' &&
            typeof window !== 'undefined' &&
            !window.location.pathname.startsWith('/verify-email')
          ) {
            const params = new URLSearchParams();
            const storedEmail = loadStoredSession()?.currentUser?.email;
            if (storedEmail) {
              params.set('email', storedEmail);
            }
            const nextPath = `${window.location.pathname}${window.location.search}`;
            if (nextPath && nextPath !== '/verify-email') {
              params.set('next', nextPath);
            }
            const query = params.toString();
            navigateAuthOnce(query ? `/verify-email?${query}` : '/verify-email', false);
          }
        } catch (err) {
          if (response.status === 401) {
            code = 'not_authenticated';
          } else if (response.status === 403) {
            code = 'forbidden';
          }
          // Body wasn't JSON (e.g. backend returned an HTML error page). This
          // isn't actionable for the user and we still surface the status code
          // via the thrown ApiError; demote to debug so it doesn't spam the
          // console in production.
          if (process.env.NODE_ENV === 'development') {
            console.debug('[API] Non-JSON error response body:', err);
          }
          retryable = isRetryable(response.status);
        }

        const apiError = new ApiError(response.status, code, message, retryable, fieldErrors);

        // Retry on 5xx/408/429, but not on 4xx client errors
        if (retryable && attempt < MAX_RETRIES) {
          lastError = apiError;
          await new Promise(resolve => setTimeout(resolve, RETRY_DELAYS[attempt]));
          continue;
        }

        throw apiError;
      }

      if (response.status === 204) {
        return undefined as T;
      }

      // Some endpoints legitimately return 200 with an empty body (e.g. the
      // expert private-speaking profile returns Results.Ok(null), which
      // ASP.NET writes as no content). Treat that as undefined instead of
      // failing JSON parsing and burning the retry budget on a phantom
      // "network error".
      const text = await response.text();
      if (!text) {
        return undefined as T;
      }
      return JSON.parse(text) as T;
    } catch (err) {
      if (err instanceof ApiError) {
        throw err;
      }

      if (err instanceof DOMException && err.name === 'AbortError') {
        const timeoutError = new ApiError(408, 'request_timeout', 'The request timed out. Please try again.', true);
        lastError = timeoutError;
        if (attempt < MAX_RETRIES) {
          await new Promise((resolve) => setTimeout(resolve, RETRY_DELAYS[attempt]));
          continue;
        }
        throw timeoutError;
      }

      // Network errors (TypeError from fetch) are retryable
      lastError = err instanceof Error ? err : new Error(String(err));
      if (attempt < MAX_RETRIES) {
        await new Promise(resolve => setTimeout(resolve, RETRY_DELAYS[attempt]));
        continue;
      }
      throw new ApiError(0, 'network_error', 'Unable to connect to the server. Please check your internet connection.', true);
    }
  }

  throw lastError ?? new Error('Request failed');
}

export async function apiBlobRequest(path: string, init?: RequestInit): Promise<Blob> {
  const response = await fetchWithTimeout(resolveApiUrl(path), {
    ...init,
    credentials: init?.credentials ?? 'include',
    headers: await getHeaders(path, init?.headers),
  });

  if (!response.ok) {
    let code = response.status === 401 ? 'not_authenticated' : response.status === 403 ? 'forbidden' : 'unknown_error';
    let message = `Request failed: ${response.status}`;
    try {
      const error = await response.json();
      code = error.code ?? code;
      message = error.message ?? error.title ?? message;
    } catch {
      // Binary endpoints often return non-JSON error bodies; status/code still carry the failure.
    }
    throw new ApiError(response.status, code, message, isRetryable(response.status));
  }

  return response.blob();
}
